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
                // C# writes the outermost rank first: int[][,] is a one-dimensional array of int[,].
                var suffix = new StringBuilder();
                Type element = type;
                while (element.IsArray)
                {
                    suffix.Append('[').Append(',', element.GetArrayRank() - 1).Append(']');
                    element = element.GetElementType()!;
                }
                return FriendlyName(element) + suffix;
            }

            if (Aliases.TryGetValue(type, out string? alias))
                return alias;

            if (type.IsGenericType)
            {
                // A type nested in a generic type carries its outer type's arguments first: Outer<int>.Inner<string> has
                // the arguments (int, string), and Outer<int>.Plain is "generic" with just (int).
                Type[] args = type.GetGenericArguments();
                var chain = new List<Type>();
                for (Type? t = type; t != null; t = t.IsNested ? t.DeclaringType : null) chain.Add(t);
                var sb = new StringBuilder();
                int used = 0;
                for (int c = chain.Count - 1; c >= 0; c--)
                {
                    Type part = chain[c];
                    int total = part.IsGenericType ? part.GetGenericArguments().Length : 0;
                    int own = Math.Max(0, total - used);
                    bool outerGeneric = c > 0 && total > 0;
                    if (c > 0 && !outerGeneric && sb.Length == 0) continue;   // non-generic outer types are omitted, as before
                    if (sb.Length > 0) sb.Append('.');
                    string name = part.Name;
                    int tick = name.IndexOf('`');
                    sb.Append(tick >= 0 ? name.Substring(0, tick) : name);
                    if (own > 0)
                    {
                        sb.Append('<');
                        for (int i = used; i < used + own; i++)
                        {
                            if (i > used) sb.Append(", ");
                            sb.Append(FriendlyName(args[i]));
                        }
                        sb.Append('>');
                    }
                    used += own;
                }
                return sb.ToString();
            }

            return type.Name;
        }

        /// <summary>
        /// The value of <c>default(T)</c> for a runtime <see cref="Type"/>. Throws <see cref="ArgumentException"/> for a type
        /// that has no values (void, an open generic type, or a by-ref type) or whose default cannot be boxed (a by-ref-like
        /// struct such as <c>Span&lt;T&gt;</c>).
        /// </summary>
        public static object? DefaultValue(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (type == typeof(void) || type.ContainsGenericParameters || type.IsByRef)
                throw new ArgumentException($"{type} has no default value.", nameof(type));
            if (IsByRefLike(type))
                throw new ArgumentException($"{type} is a by-ref-like type; its default cannot be boxed.", nameof(type));
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

        /// <summary>
        /// Attempts to convert <paramref name="value"/> to <paramref name="targetType"/>, with no exception on failure. Text
        /// parses with the invariant culture, including to Guid, TimeSpan, DateTimeOffset, Version and Uri, and those types
        /// (like any formattable value) convert to text. A number converts to an enum only if it fits the enum's underlying
        /// type, and text converts to a floating type only if it is in range (or is an infinity).
        /// </summary>
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
                    if (value is string s)
                    {
                        result = Enum.Parse(underlying, s, ignoreCase: true);
                        return true;
                    }
                    // Enum.ToObject reinterprets a double's bits on .NET 8 (1.0 became Sunday) and takes bool and char;
                    // only integers, and floating values that are whole numbers, convert.
                    if (!EnumNumber(value, out object? number)) return Fail(targetType, out result);
                    // Enum.ToObject wraps a number too wide for the enum (2^32 + 1 became Monday): range-check it first.
                    object fitted = Convert.ChangeType(number!, Enum.GetUnderlyingType(underlying), CultureInfo.InvariantCulture);
                    result = Enum.ToObject(underlying, fitted);
                    return true;
                }
                if (underlying == typeof(string) && (value is IFormattable || value is Uri || value is Version))
                {
                    // Guid, TimeSpan, Version and Uri are not IConvertible, so ChangeType failed for them.
                    result = value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
                    return true;
                }
                if (value is string str && ParseSpecial(str, underlying, out result)) return true;
                // A floating target parses without thousands separators: the invariant "1,5" was 15, not a failure.
                if (value is string text && (underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(decimal)))
                {
                    result = underlying == typeof(double) ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
                        : underlying == typeof(float) ? float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
                        : (object)decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    // .NET Core parses an out-of-range "1e400" to infinity where .NET Framework throws: fail on both.
                    bool infinite = result is double dr ? double.IsInfinity(dr) : result is float fr && float.IsInfinity(fr);
                    if (infinite && text.IndexOf("Infinity", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf('\u221E') < 0)
                        return Fail(targetType, out result);
                    return true;
                }
                result = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                return Fail(targetType, out result);
            }
        }

        // The failure result: the type's default, or null for a type that has none (void, open generic, by-ref), which
        // made DefaultValue throw out of a method that promises not to.
        private static bool Fail(Type targetType, out object? result)
        {
            result = targetType == typeof(void) || targetType.ContainsGenericParameters || targetType.IsByRef || targetType.IsPointer
                || IsByRefLike(targetType)
                ? null
                : DefaultValue(targetType);
            return false;
        }

        // Span<T> and other ref structs: Activator cannot create one, and no value of one can be boxed.
        private static bool IsByRefLike(Type type)
        {
            if (!type.IsValueType) return false;
            foreach (object a in type.GetCustomAttributes(false))
                if (a.GetType().FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") return true;
            return false;
        }

        // Text to the common value types ChangeType does not parse (it has no IConvertible route to them).
        private static bool ParseSpecial(string text, Type type, out object? result)
        {
            result = null;
            if (type == typeof(Guid)) result = Guid.Parse(text);
            else if (type == typeof(TimeSpan)) result = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            else if (type == typeof(DateTimeOffset)) result = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
            else if (type == typeof(Version)) result = Version.Parse(text);
            else if (type == typeof(Uri)) result = new Uri(text, UriKind.RelativeOrAbsolute);
            return result != null;
        }

        private static bool EnumNumber(object value, out object? number)
        {
            number = null;
            switch (value)
            {
                case Enum _:
                case sbyte _: case byte _: case short _: case ushort _:
                case int _: case uint _: case long _: case ulong _:
                    number = value;
                    return true;
                case float f:
                    return WholeLong(f, out number);
                case double d:
                    return WholeLong(d, out number);
                case decimal m:
                    if (decimal.Truncate(m) != m || m < long.MinValue || m > long.MaxValue) return false;
                    number = decimal.ToInt64(m);
                    return true;
                default:
                    return false;
            }
        }

        private static bool WholeLong(double d, out object? number)
        {
            number = null;
            // −2^63 ≤ d < 2^63, a whole number (NaN fails both tests).
            if (!(d >= -9223372036854775808.0 && d < 9223372036854775808.0) || Math.Floor(d) != d) return false;
            number = (long)d;
            return true;
        }

        /// <summary>Converts if possible, otherwise returns <paramref name="fallback"/>.</summary>
        public static object? ConvertOrDefault(object? value, Type targetType, object? fallback = null)
            => TryConvert(value, targetType, out object? result) ? result : fallback;

        /// <summary>Concrete, non-abstract, closed (not open generic) types in the assembly assignable to <paramref name="baseType"/>.</summary>
        public static IReadOnlyList<Type> GetDerivedTypes(Type baseType, Assembly assembly)
        {
            if (baseType is null) throw new ArgumentNullException(nameof(baseType));
            if (assembly is null) throw new ArgumentNullException(nameof(assembly));
            var result = new List<Type>();
            foreach (Type t in SafeGetTypes(assembly))
                if (t != baseType && !t.IsAbstract && !t.IsInterface && !t.ContainsGenericParameters && baseType.IsAssignableFrom(t))
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
