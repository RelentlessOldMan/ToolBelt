// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ToolBelt.Objects
{
    /// <summary>
    /// Small type-reflection helpers that the mapper, binder and comparer all lean on and that callers
    /// otherwise re-derive: friendly display names (including generic arguments, which the framework renders
    /// unreadably), a type's default value, nullable unwrapping, numeric detection, safe conversion with a
    /// fallback, and enumerating derived or attributed types in an assembly.
    /// </summary>
    public static class TypeUtils
    {
        private static readonly Dictionary<Type, string> Aliases = new Dictionary<Type, string>
        {
            [typeof(void)] = "void", [typeof(object)] = "object", [typeof(string)] = "string",
            [typeof(bool)] = "bool", [typeof(char)] = "char", [typeof(decimal)] = "decimal",
            [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
            [typeof(short)] = "short", [typeof(ushort)] = "ushort",
            [typeof(int)] = "int", [typeof(uint)] = "uint",
            [typeof(long)] = "long", [typeof(ulong)] = "ulong",
            [typeof(float)] = "float", [typeof(double)] = "double",
        };

        private static readonly HashSet<Type> NumericTypes = new HashSet<Type>
        {
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
        };

        /// <summary>A readable type name, e.g. <c>Dictionary&lt;string, List&lt;int&gt;&gt;</c> or <c>int?</c>.</summary>
        public static string FriendlyName(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));

            Type? nullableUnderlying = Nullable.GetUnderlyingType(type);
            if (nullableUnderlying != null)
                return FriendlyName(nullableUnderlying) + "?";

            if (type.IsArray)
            {
                string commas = new string(',', type.GetArrayRank() - 1);
                return FriendlyName(type.GetElementType()!) + "[" + commas + "]";
            }

            if (Aliases.TryGetValue(type, out string? alias))
                return alias;

            if (type.IsGenericType)
            {
                var sb = new StringBuilder();
                string name = type.Name;
                int tick = name.IndexOf('`');
                sb.Append(tick >= 0 ? name.Substring(0, tick) : name);
                sb.Append('<');
                Type[] args = type.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FriendlyName(args[i]));
                }
                sb.Append('>');
                return sb.ToString();
            }

            return type.Name;
        }

        /// <summary>The value of <c>default(T)</c> for a runtime <see cref="Type"/>.</summary>
        public static object? DefaultValue(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            return type.IsValueType && Nullable.GetUnderlyingType(type) == null
                ? Activator.CreateInstance(type)
                : null;
        }

        /// <summary>Returns the underlying type of a <see cref="Nullable{T}"/>, or the type itself.</summary>
        public static Type UnwrapNullable(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        /// <summary>True if the type (or its nullable underlying type) is a numeric type.</summary>
        public static bool IsNumeric(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            return NumericTypes.Contains(UnwrapNullable(type));
        }

        /// <summary>Attempts to convert <paramref name="value"/> to <paramref name="targetType"/>, with no exception on failure.</summary>
        public static bool TryConvert(object? value, Type targetType, out object? result)
        {
            if (targetType is null) throw new ArgumentNullException(nameof(targetType));
            Type underlying = UnwrapNullable(targetType);

            if (value is null)
            {
                result = null;
                return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
            }
            if (targetType.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }

            try
            {
                if (underlying.IsEnum)
                {
                    result = value is string s
                        ? Enum.Parse(underlying, s, ignoreCase: true)
                        : Enum.ToObject(underlying, value);
                    return true;
                }
                result = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                result = DefaultValue(targetType);
                return false;
            }
        }

        /// <summary>Converts if possible, otherwise returns <paramref name="fallback"/>.</summary>
        public static object? ConvertOrDefault(object? value, Type targetType, object? fallback = null)
            => TryConvert(value, targetType, out object? result) ? result : fallback;

        /// <summary>Concrete non-abstract types in the assembly assignable to <paramref name="baseType"/>.</summary>
        public static IReadOnlyList<Type> GetDerivedTypes(Type baseType, Assembly assembly)
        {
            if (baseType is null) throw new ArgumentNullException(nameof(baseType));
            if (assembly is null) throw new ArgumentNullException(nameof(assembly));
            var result = new List<Type>();
            foreach (Type t in SafeGetTypes(assembly))
                if (t != baseType && !t.IsAbstract && !t.IsInterface && baseType.IsAssignableFrom(t))
                    result.Add(t);
            return result;
        }

        /// <summary>Types in the assembly decorated with <typeparamref name="TAttribute"/>.</summary>
        public static IReadOnlyList<Type> GetTypesWithAttribute<TAttribute>(Assembly assembly) where TAttribute : Attribute
        {
            if (assembly is null) throw new ArgumentNullException(nameof(assembly));
            var result = new List<Type>();
            foreach (Type t in SafeGetTypes(assembly))
                if (t.GetCustomAttribute<TAttribute>() != null)
                    result.Add(t);
            return result;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                var loaded = new List<Type>();
                foreach (Type? t in ex.Types)
                    if (t != null) loaded.Add(t);
                return loaded;
            }
        }
    }
}
