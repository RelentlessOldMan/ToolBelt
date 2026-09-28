// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ToolBelt.Objects;
using ToolBelt.Time;

namespace ToolBelt.Configuration
{
    /// <summary>Thrown when binding a flat key space to a typed object fails; carries every problem found.</summary>
    public sealed class ConfigBindingException : Exception
    {
        public ConfigBindingException(IReadOnlyList<string> errors)
            : base("Configuration binding failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors))
            => Errors = errors;

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>
    /// Binds a flat key space (as produced by <see cref="ConfigLayers"/>) to a typed options object: nested
    /// sections addressed by a separator, scalar conversion (numbers, booleans, enumerations), durations via
    /// the human-duration parser, and arrays/lists via indexed keys (<c>section:0</c>, <c>section:1</c>,
    /// ...). All conversion problems are collected and reported together, because configuration errors come
    /// in batches. Missing keys leave the property at its default.
    /// </summary>
    public static class ConfigBinder
    {
        public static T Bind<T>(IReadOnlyDictionary<string, string?> values, string separator = ":") where T : new()
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (string.IsNullOrEmpty(separator)) throw new ArgumentException("Separator must not be empty.", nameof(separator));

            var errors = new List<string>();
            // Normalize to case-insensitive lookup so "Server:Port" and "server:port" resolve alike.
            var lookup = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in values) lookup[kv.Key] = kv.Value;

            var result = (T)BindObject(typeof(T), lookup, "", separator, new HashSet<Type> { typeof(T) }, errors);
            if (errors.Count > 0) throw new ConfigBindingException(errors);
            return result;
        }

        private static object BindObject(Type type, Dictionary<string, string?> values, string prefix, string sep,
            HashSet<Type> path, List<string> errors)
        {
            object obj = Activator.CreateInstance(type)!;
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite || prop.GetIndexParameters().Length != 0) continue;
                string key = prefix.Length == 0 ? prop.Name : prefix + sep + prop.Name;
                Type pt = prop.PropertyType;

                if (IsScalar(pt))
                {
                    if (values.TryGetValue(key, out string? raw))
                    {
                        if (TryConvertScalar(raw, pt, out object? value)) prop.SetValue(obj, value);
                        else errors.Add($"'{key}': cannot convert '{raw}' to {TypeUtils.FriendlyName(pt)}.");
                    }
                }
                else if (TryGetElementType(pt, out Type elementType))
                {
                    object? sequence = BindSequence(pt, elementType, values, key, sep, errors);
                    if (sequence != null) prop.SetValue(obj, sequence);
                }
                else if (pt.IsClass && !path.Contains(pt))
                {
                    // Guard against a cyclic type reference on the current path (would recurse forever).
                    path.Add(pt);
                    prop.SetValue(obj, BindObject(pt, values, key, sep, path, errors));
                    path.Remove(pt);
                }
            }
            return obj;
        }

        private static object? BindSequence(Type sequenceType, Type elementType,
            Dictionary<string, string?> values, string key, string sep, List<string> errors)
        {
            var items = new List<object?>();
            for (int i = 0; ; i++)
            {
                if (!values.TryGetValue(key + sep + i, out string? raw)) break;
                if (TryConvertScalar(raw, elementType, out object? value)) items.Add(value);
                else errors.Add($"'{key}{sep}{i}': cannot convert '{raw}' to {TypeUtils.FriendlyName(elementType)}.");
            }
            if (items.Count == 0) return null; // nothing supplied; leave default

            Array array = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
            if (sequenceType.IsArray) return array;

            // List<T> (or an assignable generic list): fill via the typed list.
            object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            var add = list.GetType().GetMethod("Add")!;
            foreach (object? item in array) add.Invoke(list, new[] { item });
            return list;
        }

        private static bool IsScalar(Type type)
        {
            Type t = TypeUtils.UnwrapNullable(type);
            return t.IsPrimitive || t.IsEnum
                || t == typeof(string) || t == typeof(decimal)
                || t == typeof(TimeSpan) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid);
        }

        private static bool TryConvertScalar(string? raw, Type targetType, out object? value)
        {
            Type t = TypeUtils.UnwrapNullable(targetType);
            if (t == typeof(TimeSpan))
            {
                if (raw != null && HumanDuration.TryParse(raw, out TimeSpan ts)) { value = ts; return true; }
                if (TimeSpan.TryParse(raw, out TimeSpan parsed)) { value = parsed; return true; }
                value = null;
                return false;
            }
            return TypeUtils.TryConvert(raw, targetType, out value);
        }

        private static bool TryGetElementType(Type type, out Type elementType)
        {
            if (type.IsArray)
            {
                elementType = type.GetElementType()!;
                return IsScalar(elementType);
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return IsScalar(elementType);
            }
            elementType = typeof(object);
            return false;
        }
    }
}
