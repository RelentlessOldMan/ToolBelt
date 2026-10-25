// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Reads and writes a value through a dotted member path with optional indexers, for example
    /// <c>order.lines[2].price</c> or <c>settings["theme"]</c>. On failure it throws an error identifying
    /// the exact path segment that could not be resolved, rather than a null-reference somewhere inside.
    /// The single-level by-name accessor is the trivial special case.
    /// <para>
    /// A missing dictionary key throws <see cref="KeyNotFoundException"/>; an index token that does not convert to the
    /// dictionary's key type, or a list index out of range, throws <see cref="ArgumentException"/>. <see cref="TryGet"/>
    /// returns false for all of these; an exception thrown by a property getter itself still propagates. Setting through a
    /// struct-valued member writes the modified copy back, so <c>Set(holder, "Point.X", 5)</c> changes the holder.
    /// </para>
    /// </summary>
    public static class PropertyPath
    {
        public static object? Get(object root, string path)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            var steps = Parse(path);
            object? current = root;
            for (int i = 0; i < steps.Count; i++)
            {
                if (current is null)
                    throw new InvalidOperationException($"Path '{path}' hit a null before segment '{steps[i]}'.");
                current = steps[i].Read(current, path);
            }
            return current;
        }

        public static bool TryGet(object root, string path, out object? value)
        {
            try { value = Get(root, path); return true; }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException
                                       || ex is FormatException || ex is OverflowException)
            {
                value = null;
                return false;
            }
        }

        public static void Set(object root, string path, object? value)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            var steps = Parse(path);
            // targets[i] is the object segment i is read from (or, for the last segment, written to).
            var targets = new object[steps.Count];
            object? target = root;
            for (int i = 0; i < steps.Count - 1; i++)
            {
                if (target is null)
                    throw new InvalidOperationException($"Path '{path}' hit a null before segment '{steps[i]}'.");
                targets[i] = target;
                target = steps[i].Read(target, path);
            }
            if (target is null)
                throw new InvalidOperationException($"Path '{path}' hit a null before the final segment.");
            targets[steps.Count - 1] = target;
            steps[steps.Count - 1].Write(target, value, path);

            // A struct read along the way is a boxed copy: store each modified copy back into its parent.
            for (int i = steps.Count - 2; i >= 0 && targets[i + 1].GetType().IsValueType; i--)
                steps[i].Write(targets[i], targets[i + 1], path);
        }

        // ---- Path parsing ----

        private abstract class Step
        {
            public abstract object? Read(object target, string fullPath);
            public abstract void Write(object target, object? value, string fullPath);
        }

        private sealed class MemberStep : Step
        {
            private readonly string _name;
            public MemberStep(string name) => _name = name;
            public override string ToString() => _name;

            public override object? Read(object target, string fullPath)
            {
                PropertyInfo? p = FindProperty(target.GetType(), _name);
                if (p is null)
                    throw new ArgumentException($"Segment '{_name}' is not a public property of {target.GetType().Name} (path '{fullPath}').");
                return p.GetValue(target);
            }

            public override void Write(object target, object? value, string fullPath)
            {
                PropertyInfo? p = FindProperty(target.GetType(), _name);
                if (p is null || p.GetSetMethod() is null)
                    throw new ArgumentException($"Segment '{_name}' is not a writable property of {target.GetType().Name} (path '{fullPath}').");
                p.SetValue(target, value);
            }

            // The most derived public, non-indexed property of that name (a "new" property hides its base's, which
            // GetProperty would report as ambiguous).
            private static PropertyInfo? FindProperty(Type type, string name)
            {
                for (Type? t = type; t != null; t = t.BaseType)
                {
                    foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (p.Name == name && p.GetIndexParameters().Length == 0) return p;
                }
                return null;
            }
        }

        private sealed class IndexStep : Step
        {
            private readonly string _token;
            public IndexStep(string token) => _token = token;
            public override string ToString() => $"[{_token}]";

            public override object? Read(object target, string fullPath)
            {
                if (target is IDictionary dict)
                {
                    object key = ConvertKey(dict, _token, fullPath);
                    if (!dict.Contains(key))
                        throw new KeyNotFoundException($"Key '[{_token}]' is not in the dictionary (path '{fullPath}').");
                    return dict[key];
                }
                if (target is IList list)
                    return list[ParseIndex(list, fullPath)];
                throw new ArgumentException($"Segment '[{_token}]' requires a list or dictionary, got {target.GetType().Name} (path '{fullPath}').");
            }

            public override void Write(object target, object? value, string fullPath)
            {
                if (target is IDictionary dict) { dict[ConvertKey(dict, _token, fullPath)] = value; return; }
                if (target is IList list) { list[ParseIndex(list, fullPath)] = value; return; }
                throw new ArgumentException($"Segment '[{_token}]' requires a list or dictionary, got {target.GetType().Name} (path '{fullPath}').");
            }

            // Range-checked here: an array threw IndexOutOfRangeException, which is not an ArgumentException and escaped TryGet.
            private int ParseIndex(IList list, string fullPath)
            {
                if (!int.TryParse(_token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    throw new ArgumentException($"Index '[{_token}]' is not an integer (path '{fullPath}').");
                if (index < 0 || index >= list.Count)
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Index '[{_token}]' is out of range for {list.Count} item(s) (path '{fullPath}').");
                return index;
            }

            private static object ConvertKey(IDictionary dict, string token, string fullPath)
            {
                // Convert the textual key to the key type of the IDictionary<TKey, TValue> the object implements.
                Type keyType = typeof(object);
                foreach (Type i in dict.GetType().GetInterfaces())
                {
                    if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                    {
                        keyType = i.GetGenericArguments()[0];
                        break;
                    }
                }
                if (keyType == typeof(string) || keyType == typeof(object)) return token;
                try
                {
                    Type underlying = Nullable.GetUnderlyingType(keyType) ?? keyType;
                    if (underlying.IsEnum) return Enum.Parse(underlying, token, ignoreCase: true);
                    if (underlying == typeof(Guid)) return Guid.Parse(token);
                    return Convert.ChangeType(token, underlying, CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException || ex is ArgumentException)
                {
                    throw new ArgumentException($"Key '[{token}]' does not convert to {keyType.Name} (path '{fullPath}').", ex);
                }
            }
        }

        private static List<Step> Parse(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must not be empty.", nameof(path));

            var steps = new List<Step>();
            int i = 0;
            while (i < path.Length)
            {
                if (path[i] == '.')
                {
                    i++;
                    continue;
                }
                if (path[i] == '[')
                {
                    int close = path.IndexOf(']', i);
                    if (close < 0) throw new ArgumentException($"Unclosed '[' in path '{path}'.");
                    string token = path.Substring(i + 1, close - i - 1).Trim().Trim('"', '\'');
                    steps.Add(new IndexStep(token));
                    i = close + 1;
                }
                else
                {
                    int j = i;
                    while (j < path.Length && path[j] != '.' && path[j] != '[') j++;
                    steps.Add(new MemberStep(path.Substring(i, j - i)));
                    i = j;
                }
            }
            if (steps.Count == 0) throw new ArgumentException("Path must not be empty.", nameof(path));
            return steps;
        }
    }
}
