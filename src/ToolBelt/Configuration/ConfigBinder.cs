// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

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
    /// sections addressed by a separator, scalar conversion (numbers, booleans, enumerations, <see cref="Guid"/>,
    /// dates), durations, and arrays/lists via indexed keys (<c>section:0</c>, <c>section:1</c>, ...). All conversion
    /// problems are collected and reported together, because configuration errors come in batches. Missing keys leave
    /// the property at its default: a nested object is only touched when a key for its section exists, and then the
    /// existing instance (if any) is bound into rather than replaced.
    /// <list type="bullet">
    /// <item>Booleans accept true/false, 1/0, yes/no, on/off (any case).</item>
    /// <item>Enumerations accept a member name or number, and [Flags] combinations such as "Read, Write"; a value
    /// that is not a defined member (or combination) is an error.</item>
    /// <item>Durations accept the human form ("1m 30s", "250ms", "2d") or a TimeSpan string with a colon
    /// ("00:05:00", "1.02:00:00"). A bare number is rejected as ambiguous (TimeSpan would read "30" as 30 days).</item>
    /// <item>Lists may be typed as arrays, <see cref="List{T}"/> or the list/collection/enumerable interfaces; their
    /// indexes must run 0, 1, 2, ... without gaps. A get-only list property is filled in place.</item>
    /// <item>Numbers are invariant: "0,5" is an error, not 5.</item>
    /// <item>A configured key is never dropped silently: a type the binder doesn't handle (a dictionary, a list of
    /// objects, <see cref="Uri"/>), a read-only property (one without a public setter), a list given one value
    /// ("Hosts=a,b", also alongside its indexed keys) or a non-numeric index, a section given a value of its own
    /// ("Server=localhost" beside "Server:Port"), or a setter that throws is reported as an error.</item>
    /// <item>A key whose value is null is unset: the property keeps its default.</item>
    /// <item>A class may contain its own type (a fallback <c>Opts</c> inside <c>Opts</c>): each configured level is bound,
    /// down to <see cref="MaxDepth"/> sections; a key nested deeper is reported as an error.</item>
    /// <item>A <see cref="DateTimeOffset"/> without an offset is read as UTC (not the machine's zone). Only a
    /// <see cref="char"/> setting keeps its whitespace; other scalars are trimmed.</item>
    /// </list>
    /// </summary>
    public static class ConfigBinder
    {
        /// <summary>The deepest section nesting bound (a self-containing type configured deeper is an error). A key nested
        /// about 2,000 sections deep overflowed the stack, which kills the process.</summary>
        public const int MaxDepth = 64;

        public static T Bind<T>(IReadOnlyDictionary<string, string?> values, string separator = ":") where T : new()
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (string.IsNullOrEmpty(separator)) throw new ArgumentException("Separator must not be empty.", nameof(separator));

            var errors = new List<string>();
            // Normalize to case-insensitive lookup so "Server:Port" and "server:port" resolve alike.
            var lookup = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in values) lookup[kv.Key] = kv.Value;

            var result = new T();
            object boxed = result!;
            BindInto(boxed, typeof(T), lookup, "", separator, errors, 0);
            if (errors.Count > 0) throw new ConfigBindingException(errors);
            return (T)boxed;
        }

        private static void BindInto(object obj, Type type, Dictionary<string, string?> values, string prefix, string sep,
            List<string> errors, int depth)
        {
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length != 0) continue;
                string key = prefix.Length == 0 ? prop.Name : prefix + sep + prop.Name;
                Type pt = prop.PropertyType;

                if (IsScalar(pt))
                {
                    // Null is unset, as it is for every other kind of property: it overwrote a string's default, and was a
                    // conversion error ("cannot convert ''") for an int.
                    if (values.TryGetValue(key, out string? raw) && raw != null)
                    {
                        if (!CanSet(prop)) errors.Add($"'{key}': the property is read-only, so it cannot be bound.");
                        else if (TryConvertScalar(raw, pt, out object? value)) TrySet(prop, obj, value, key, errors);
                        else errors.Add($"'{key}': cannot convert '{raw}' to {FriendlyName(pt)}.");
                    }
                    // "Port:0=5" on an int was dropped without a word.
                    if (HasSection(values, key + sep))
                        errors.Add($"'{key}': {FriendlyName(pt)} is a single value, so nested keys ('{key}{sep}...') cannot be bound.");
                }
                else if (TryGetElementType(pt, out Type elementType))
                {
                    object? sequence = BindSequence(pt, elementType, values, key, sep, errors);
                    // "Hosts=a,b": a list is not split from one value (EnvironmentVariables.GetList does that). Beside indexed
                    // keys too: there the value was dropped without a word.
                    if (values.TryGetValue(key, out string? whole) && !string.IsNullOrEmpty(whole))
                        errors.Add($"'{key}': a list takes indexed keys ('{key}{sep}0', '{key}{sep}1', ...), not one value.");
                    if (sequence == null) continue;
                    if (CanSet(prop)) { TrySet(prop, obj, sequence, key, errors); continue; }
                    // A get-only list (List<string> Hosts { get; } = new()) is filled in place rather than skipped.
                    if (prop.CanRead && TryGet(prop, obj, key, errors, out object? existing))
                    {
                        if (existing is System.Collections.IList list && !list.IsReadOnly && !list.IsFixedSize)
                        {
                            try
                            {
                                list.Clear();
                                foreach (object? item in (System.Collections.IEnumerable)sequence) list.Add(item);
                            }
                            catch (Exception ex) when (!(ex is OutOfMemoryException)) { errors.Add($"'{key}': {ex.Message}"); }
                        }
                        else errors.Add($"'{key}': the property is read-only and holds no list that can be filled, so it cannot be bound.");
                    }
                }
                else if (!IsBindableClass(pt))
                {
                    // Configured but of a type the binder doesn't handle (Uri, a struct, a dictionary, a list of objects):
                    // say so rather than drop the values.
                    if (IsConfigured(values, key, sep))
                        errors.Add($"'{key}': {FriendlyName(pt)} is not a supported setting type (scalars, lists of scalars and classes are).");
                }
                else if (HasSection(values, key + sep))
                {
                    // "Server=localhost" beside "Server:Port=5": the section's own value was dropped without a word.
                    if (values.TryGetValue(key, out string? own) && !string.IsNullOrEmpty(own))
                        errors.Add($"'{key}': a section takes nested keys ('{key}{sep}...'), not a value of its own.");
                    // A type may contain itself (Opts.Fallback is an Opts): recursion still ends, because each level needs
                    // a configured key one section longer, and is capped well before the stack runs out.
                    if (depth >= MaxDepth)
                    {
                        errors.Add($"'{key}': sections nest more than {MaxDepth} deep, so they cannot be bound.");
                        continue;
                    }
                    // Only a section that is actually configured is bound: an untouched property keeps its default
                    // (an initializer's object, or null), and a type the binder can't construct is never instantiated.
                    object? target = null;
                    if (prop.CanRead && !TryGet(prop, obj, key, errors, out target)) continue;
                    if (target == null)
                    {
                        if (!CanSet(prop))
                        {
                            errors.Add($"'{key}': the property is read-only and null, so its section cannot be bound.");
                            continue;
                        }
                        if (pt.IsAbstract || pt.GetConstructor(Type.EmptyTypes) == null)
                        {
                            errors.Add($"'{key}': {FriendlyName(pt)} has no public parameterless constructor, so its section cannot be bound.");
                            continue;
                        }
                        try { target = Activator.CreateInstance(pt)!; }
                        catch (TargetInvocationException ex) { errors.Add($"'{key}': {(ex.InnerException ?? ex).Message}"); continue; }
                    }
                    BindInto(target, pt, values, key, sep, errors, depth + 1);
                    if (CanSet(prop)) TrySet(prop, obj, target, key, errors);
                }
                else if (values.TryGetValue(key, out string? whole) && !string.IsNullOrEmpty(whole))
                {
                    // A plain value for a class (Uri, Version, or a section such as "Server=localhost") has nothing to
                    // bind into: say so rather than drop it.
                    errors.Add($"'{key}': {FriendlyName(pt)} is not a supported setting type; a section takes nested keys ('{key}{sep}...').");
                }
            }
        }

        // A setter or getter that throws (a validating property) is a binding error for that key, not a crash.
        private static void TrySet(PropertyInfo prop, object obj, object? value, string key, List<string> errors)
        {
            try { prop.SetValue(obj, value); }
            catch (TargetInvocationException ex) { errors.Add($"'{key}': {(ex.InnerException ?? ex).Message}"); }
        }

        private static bool TryGet(PropertyInfo prop, object obj, string key, List<string> errors, out object? value)
        {
            try { value = prop.GetValue(obj); return true; }
            catch (TargetInvocationException ex) { errors.Add($"'{key}': {(ex.InnerException ?? ex).Message}"); value = null; return false; }
        }

        // A class with properties to bind into: not a delegate or a collection (a dictionary or a list of objects).
        private static bool IsBindableClass(Type type)
            => type.IsClass && type != typeof(object) && !typeof(Delegate).IsAssignableFrom(type) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

        // A public setter only: a private one (a value the type keeps out of outside hands) was set from configuration.
        private static bool CanSet(PropertyInfo prop) => prop.GetSetMethod() != null;

        private static bool IsConfigured(Dictionary<string, string?> values, string key, string sep)
            => (values.TryGetValue(key, out string? v) && !string.IsNullOrEmpty(v)) || HasSection(values, key + sep);

        private static bool HasSection(Dictionary<string, string?> values, string sectionPrefix)
        {
            foreach (string k in values.Keys)
                if (k.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static object? BindSequence(Type sequenceType, Type elementType,
            Dictionary<string, string?> values, string key, string sep, List<string> errors)
        {
            var items = new List<object?>();
            int contiguous = 0;
            for (int i = 0; ; i++)
            {
                if (!values.TryGetValue(key + sep + i.ToString(CultureInfo.InvariantCulture), out string? raw)) break;
                // Counted even when it fails to convert: "Ports:0=x, Ports:1=2" also reported a gap at the valid ':1'.
                contiguous++;
                if (TryConvertScalar(raw, elementType, out object? value)) items.Add(value);
                else errors.Add($"'{key}{sep}{i}': cannot convert '{raw}' to {FriendlyName(elementType)}.");
            }

            // An index past a gap (":0", ":2") would otherwise be dropped silently, along with everything after it.
            string itemPrefix = key + sep;
            foreach (string k in values.Keys)
            {
                if (!k.StartsWith(itemPrefix, StringComparison.OrdinalIgnoreCase) || k.Length == itemPrefix.Length) continue;
                string index = k.Substring(itemPrefix.Length);
                bool digits = true;
                foreach (char ch in index) if (ch < '0' || ch > '9') { digits = false; break; }
                int nested = index.IndexOf(sep, StringComparison.Ordinal);
                string first = nested < 0 ? index : index.Substring(0, nested);
                bool firstDigits = first.Length > 0;
                foreach (char ch in first) if (ch < '0' || ch > '9') { firstDigits = false; break; }
                // The elements are scalars, so "Hosts:0:Name" has nothing to bind into (it was dropped without a word).
                if (!digits && firstDigits)
                {
                    errors.Add($"'{k}': the items of '{key}' are single values, so they take no nested keys.");
                    continue;
                }
                if (!digits)
                {
                    errors.Add($"'{k}': list items are addressed by index ('{itemPrefix}0', '{itemPrefix}1', ...).");
                    continue;
                }
                if (!int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n >= contiguous
                    || n.ToString(CultureInfo.InvariantCulture) != index)
                    errors.Add($"'{k}': list indexes must run 0, 1, 2, ... without gaps (found {contiguous} contiguous item(s)).");
            }
            if (items.Count == 0) return null; // nothing supplied; leave default

            Array array = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
            if (sequenceType.IsArray) return array;

            // List<T> or one of the interfaces it implements: fill via the typed list.
            object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            var add = list.GetType().GetMethod("Add")!;
            foreach (object? item in array) add.Invoke(list, new[] { item });
            return list;
        }

        // "1e400" overflows to ∞ on .NET Core but fails on .NET Framework: an error on both. "Infinity" itself is allowed.
        private static bool Overflowed(string s, double v)
            => double.IsInfinity(v) && s.IndexOf("Infinity", StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf('\u221E') < 0;

        private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

        private static bool IsScalar(Type type)
        {
            Type t = Unwrap(type);
            return t.IsPrimitive || t.IsEnum
                || t == typeof(string) || t == typeof(decimal)
                || t == typeof(TimeSpan) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid);
        }

        private static bool TryConvertScalar(string? raw, Type targetType, out object? value)
        {
            Type t = Unwrap(targetType);
            value = null;
            if (raw is null) return !targetType.IsValueType || t != targetType;      // null only for reference/nullable
            if (t == typeof(string)) { value = raw; return true; }
            // Before the trim: a tab or space character setting could not be bound at all.
            if (t == typeof(char))
            {
                if (raw.Length == 1) { value = raw[0]; return true; }
                return t != targetType && raw.Length == 0;                           // empty → null for char?
            }

            string s = raw.Trim();
            if (t != targetType && s.Length == 0) return true;                       // empty → null for Nullable<T>
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (t == typeof(TimeSpan))
            {
                if (TryParseDuration(s, out TimeSpan ts)) { value = ts; return true; }
                return false;
            }
            if (t == typeof(bool))
            {
                switch (s.ToLowerInvariant())
                {
                    case "true": case "1": case "yes": case "on": value = true; return true;
                    case "false": case "0": case "no": case "off": value = false; return true;
                    default: return false;
                }
            }
            if (t.IsEnum) return TryParseEnum(s, t, out value);
            if (t == typeof(Guid))
            {
                if (Guid.TryParse(s, out Guid g)) { value = g; return true; }
                return false;
            }
            if (t == typeof(DateTimeOffset))
            {
                // UTC when no offset is given: None took the machine's zone, so one file bound differently on each machine.
                if (DateTimeOffset.TryParse(s, inv, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto)) { value = dto; return true; }
                return false;
            }
            if (t == typeof(DateTime))
            {
                if (DateTime.TryParse(s, inv, DateTimeStyles.RoundtripKind, out DateTime dt)) { value = dt; return true; }
                return false;
            }
            // Floating point without AllowThousands: "0,5" (a decimal comma) is an error, not 5.
            if (t == typeof(double))
            {
                if (double.TryParse(s, NumberStyles.Float, inv, out double d) && !Overflowed(s, d)) { value = d; return true; }
                return false;
            }
            if (t == typeof(float))
            {
                if (float.TryParse(s, NumberStyles.Float, inv, out float fl) && !Overflowed(s, fl)) { value = fl; return true; }
                return false;
            }
            if (t == typeof(decimal))
            {
                if (decimal.TryParse(s, NumberStyles.Float, inv, out decimal m)) { value = m; return true; }
                return false;
            }
            // Convert.ChangeType can't make these, so every value failed.
            if (t == typeof(IntPtr))
            {
                if (long.TryParse(s, NumberStyles.Integer, inv, out long l) && (IntPtr.Size == 8 || (l >= int.MinValue && l <= int.MaxValue)))
                { value = new IntPtr(l); return true; }
                return false;
            }
            if (t == typeof(UIntPtr))
            {
                if (ulong.TryParse(s, NumberStyles.None, inv, out ulong u) && (UIntPtr.Size == 8 || u <= uint.MaxValue))
                { value = new UIntPtr(u); return true; }
                return false;
            }
            try
            {
                value = Convert.ChangeType(s, t, inv);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                value = null;
                return false;
            }
        }

        // A member name or number; [Flags] enums also accept a combination of defined bits ("Read, Write" or 3).
        private static bool TryParseEnum(string s, Type enumType, out object? value)
        {
            value = null;
            object parsed;
            try { parsed = Enum.Parse(enumType, s, ignoreCase: true); }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException) { return false; }

            if (Enum.IsDefined(enumType, parsed)) { value = parsed; return true; }
            if (enumType.GetCustomAttribute<FlagsAttribute>() == null) return false;
            ulong bits = ToBits(parsed), allowed = 0;
            foreach (object member in Enum.GetValues(enumType)) allowed |= ToBits(member);
            if ((bits & ~allowed) != 0) return false;
            value = parsed;
            return true;
        }

        private static ulong ToBits(object enumValue)
            => unchecked(Type.GetTypeCode(Enum.GetUnderlyingType(enumValue.GetType())) switch
            {
                TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => (ulong)Convert.ToInt64(enumValue, CultureInfo.InvariantCulture),
                _ => Convert.ToUInt64(enumValue, CultureInfo.InvariantCulture),
            });

        // "1m 30s", "250ms", "2d" (integer terms, optionally negative), or a TimeSpan string containing a colon.
        private static bool TryParseDuration(string s, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (s.Length == 0) return false;
            if (s.IndexOf(':') >= 0) return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out value);

            int i = 0;
            bool negative = s[0] == '-';
            if (negative) i++;
            long ticks = 0;
            bool sawTerm = false;
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                int start = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == start) return false;
                if (!long.TryParse(s.Substring(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out long number)) return false;
                int unitStart = i;
                while (i < s.Length && char.IsLetter(s[i])) i++;
                long perUnit = s.Substring(unitStart, i - unitStart).ToLowerInvariant() switch
                {
                    "d" => TimeSpan.TicksPerDay,
                    "h" => TimeSpan.TicksPerHour,
                    "m" => TimeSpan.TicksPerMinute,
                    "s" => TimeSpan.TicksPerSecond,
                    "ms" => TimeSpan.TicksPerMillisecond,
                    _ => -1,                                                    // includes a bare number (no unit)
                };
                if (perUnit < 0) return false;
                try { ticks = checked(ticks + number * perUnit); }
                catch (OverflowException) { return false; }
                sawTerm = true;
            }
            if (!sawTerm) return false;
            value = TimeSpan.FromTicks(negative ? -ticks : ticks);
            return true;
        }

        private static bool TryGetElementType(Type type, out Type elementType)
        {
            if (type.IsArray)
            {
                elementType = type.GetElementType()!;
                return type.GetArrayRank() == 1 && IsScalar(elementType);
            }
            if (type.IsGenericType)
            {
                Type def = type.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(ICollection<>) || def == typeof(IEnumerable<>)
                    || def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return IsScalar(elementType);
                }
            }
            elementType = typeof(object);
            return false;
        }

        private static string FriendlyName(Type type)
        {
            Type? inner = Nullable.GetUnderlyingType(type);
            return inner != null ? FriendlyName(inner) + "?" : type.Name;
        }
    }
}
