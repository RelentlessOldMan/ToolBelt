// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
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

        /// <summary>
        /// A readable type name, e.g. <c>Dictionary&lt;string, List&lt;int&gt;&gt;</c> or <c>int?</c>. A type nested
        /// thousands of generic levels deep throws <see cref="InsufficientExecutionStackException"/> (each level is a
        /// call; without the check, about 2,400 levels overflowed the stack and ended the process).
        /// </summary>
        public static string FriendlyName(Type type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            RuntimeHelpers.EnsureSufficientExecutionStack();

            Type? nullableUnderlying = Nullable.GetUnderlyingType(type);
            if (nullableUnderlying != null)
                return FriendlyName(nullableUnderlying) + "?";

            // Type.Name spelled these from the element's raw name: Int32*, List`1&.
            if (type.IsPointer) return FriendlyName(type.GetElementType()!) + "*";
            if (type.IsByRef) return FriendlyName(type.GetElementType()!) + "&";

            if (type.IsArray)
            {
                // C# writes the outermost rank first: int[][,] is a one-dimensional array of int[,].
                var suffix = new StringBuilder();
                Type element = type;
                while (element.IsArray)
                {
                    int rank = element.GetArrayRank();
                    // A multi-dimensional array of rank 1 (int[*], lower bounds allowed) is not int[]: C# has no syntax for
                    // it, so it is spelled as the runtime does.
                    if (rank == 1 && element != element.GetElementType()!.MakeArrayType()) suffix.Append("[*]");
                    else suffix.Append('[').Append(',', rank - 1).Append(']');
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
        /// type, and text converts to a floating type only if it is in range (or is an infinity). Conversions do not lose
        /// information silently: a fractional number does not convert to an integer type (2.5 is not rounded to 2), a finite
        /// double beyond float's range does not become an infinity, a nonzero number (or text) does not underflow to zero,
        /// an integer converts to a floating type only if it is exactly representable (2^53 + 1 is not a double), a double
        /// or float converts to decimal only if decimal holds its value exactly as its shortest round-trip text spells it,
        /// a double or float converts to its shortest round-trip text, and a DateTime or DateTimeOffset to its round-trip
        /// "O" text. Text "-0" is negative zero on every target framework. Half (.NET 5 and later) converts to the other
        /// numeric types through double.
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

            // Half is not IConvertible, so ChangeType refused it; double holds every Half exactly.
            if (value.GetType().FullName == "System.Half" && underlying != typeof(string) && HalfToDouble(value) is double half)
                return TryConvert(half, targetType, out result);

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
                    // A date's default text dropped fractions of a second (and a DateTime's Kind).
                    result = value is double d ? RoundTrip(d) : value is float fl ? RoundTrip(fl)
                        : value is DateTime dt ? dt.ToString("O", CultureInfo.InvariantCulture)
                        : value is DateTimeOffset dto ? dto.ToString("O", CultureInfo.InvariantCulture)
                        : value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
                    return true;
                }
                if (underlying == typeof(decimal) && (value is double || value is float))
                {
                    // Convert.ToDecimal(double) rounds to 15 significant digits (0.1 + 0.2 became 0.3) and underflows to 0.
                    string r = value is double dd ? RoundTrip(dd) : RoundTrip((float)value);
                    decimal m = decimal.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture);
                    string back = m.ToString(CultureInfo.InvariantCulture);
                    bool exact = value is double dv
                        ? double.Parse(back, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(dv)
                        : float.Parse(back, NumberStyles.Float, CultureInfo.InvariantCulture).Equals((float)value);
                    if (!exact && !(m == 0 && (value is double z ? z == 0 : (float)value == 0))) return Fail(targetType, out result);
                    result = m;
                    return true;
                }
                if (IsIntegral(underlying) && !IsWhole(value)) return Fail(targetType, out result);
                if (underlying == typeof(float) && value is double big && !double.IsInfinity(big) && !double.IsNaN(big)
                    && float.IsInfinity((float)big)) return Fail(targetType, out result);
                // 1e-50 became 0f.
                if (underlying == typeof(float) && value is double tiny && tiny != 0 && (float)tiny == 0) return Fail(targetType, out result);
                if ((underlying == typeof(double) || underlying == typeof(float)) && !IntegerFits(value, underlying))
                    return Fail(targetType, out result);
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
                    bool zero = result is double d0 ? d0 == 0 : result is float f0 ? f0 == 0 : (decimal)result! == 0;
                    if (zero)
                    {
                        // "1e-400" underflowed to 0, which is not the number the text names.
                        if (NonzeroMantissa(text)) return Fail(targetType, out result);
                        // .NET Framework dropped the sign of "-0", so the text of -0.0 did not round-trip.
                        if (text.TrimStart().StartsWith("-", StringComparison.Ordinal))
                        {
                            if (underlying == typeof(double)) result = -0.0;
                            else if (underlying == typeof(float)) result = -0.0f;
                        }
                    }
                    return true;
                }
                result = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                return true;
            }
            // NotSupportedException: an IConvertible may refuse a conversion that way.
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is ArgumentException
                                       || ex is NotSupportedException)
            {
                return Fail(targetType, out result);
            }
        }

        // Whether the digits before any exponent include a nonzero one.
        private static bool NonzeroMantissa(string text)
        {
            foreach (char c in text)
            {
                if (c == 'e' || c == 'E') break;
                if (c >= '1' && c <= '9') return true;
            }
            return false;
        }

        // An integer converts to double or float only if the floating value is exactly that integer: 2^53 + 1 became
        // 2^53. The floating value is checked through double, which holds every float exactly.
        private static bool IntegerFits(object value, Type target)
        {
            const double TwoTo63 = 9223372036854775808.0, TwoTo64 = 18446744073709551616.0;
            switch (value)
            {
                case long l:
                {
                    double r = target == typeof(float) ? (float)l : (double)l;
                    return r < TwoTo63 && (long)r == l;
                }
                case ulong u:
                {
                    double r = target == typeof(float) ? (float)u : (double)u;
                    return r < TwoTo64 && (ulong)r == u;
                }
                case int i:
                {
                    double r = (float)i;
                    return target == typeof(double) || (r < 2147483648.0 && (int)r == i);
                }
                case uint ui:
                {
                    double r = (float)ui;
                    return target == typeof(double) || (r < 4294967296.0 && (uint)r == ui);
                }
                default: return true;
            }
        }

        // System.Half's explicit conversion to double, by reflection (Half is not in netstandard2.0).
        private static object? HalfToDouble(object half)
        {
            foreach (MethodInfo m in half.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "op_Explicit" && m.ReturnType == typeof(double) && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == half.GetType())
                    return m.Invoke(null, new[] { half });
            return null;
        }

        // .NET Framework's default and even "R" formats can drop a digit; fall back to all the digits when they do.
        private static string RoundTrip(double d)
        {
            // .NET Framework prints -0.0 as "0" (and 0.0 equals -0.0, so the check below cannot see it).
            if (d == 0 && double.IsNegativeInfinity(1 / d)) return "-0";
            string r = d.ToString("R", CultureInfo.InvariantCulture);
            return double.IsNaN(d) || double.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(d)
                ? r : d.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string RoundTrip(float f)
        {
            if (f == 0 && float.IsNegativeInfinity(1 / f)) return "-0";
            string r = f.ToString("R", CultureInfo.InvariantCulture);
            return float.IsNaN(f) || float.Parse(r, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(f)
                ? r : f.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static bool IsIntegral(Type t)
            => t == typeof(sbyte) || t == typeof(byte) || t == typeof(short) || t == typeof(ushort) || t == typeof(int)
               || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) || t == typeof(char);

        // False for a fractional (or non-finite) double, float or decimal; true for anything else.
        private static bool IsWhole(object value) => value switch
        {
            double d => Math.Floor(d) == d && !double.IsInfinity(d),
            float f => Math.Floor(f) == f && !float.IsInfinity(f),
            decimal m => decimal.Truncate(m) == m,
            _ => true,
        };

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
            // .NET Framework does not mark its own by-ref-like types with the attribute.
            if (type == typeof(TypedReference) || type.FullName == "System.ArgIterator" || type.FullName == "System.RuntimeArgumentHandle") return true;
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
                // IsDefined: GetCustomAttribute throws AmbiguousMatchException for an attribute applied twice (AllowMultiple).
                if (t.IsDefined(typeof(TAttribute), inherit: true))
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
