// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

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
    /// ("Server=localhost" beside "Server:Port"), a key given twice in different casing with different values ("Port",
    /// "PORT"), or a setter that throws is reported as an error.</item>
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
            var spelled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in values)
            {
                // The dictionary's own ArgumentNullException("key") escaped; ConfigLayers says this.
                if (kv.Key is null) throw new ArgumentException("A source holds a null key.", nameof(values));
                // "Port=1" beside "PORT=2" kept whichever came last, without a word. The same value twice is no conflict.
                if (spelled.TryGetValue(kv.Key, out string? first) && lookup[kv.Key] != kv.Value)
                    errors.Add($"'{first}' and '{kv.Key}' are the same key in different casing, with different values.");
                else spelled[kv.Key] = kv.Key;
                lookup[kv.Key] = kv.Value;
            }

            var result = new T();
            object boxed = result!;
            // A null value is unset, so its key is no section member either: "Port:0" = null on an int was "nested keys
            // cannot be bound", and "Server:Port" = null created a Server that was meant to stay at its default.
            var scope = new List<string>();
            foreach (var kv in lookup) if (kv.Value != null) scope.Add(kv.Key);
            BindInto(boxed, typeof(T), lookup, scope, "", separator, errors, 0);
            if (errors.Count > 0) throw new ConfigBindingException(errors);
            return (T)boxed;
        }

        // scope holds the keys under prefix: every key a property of this section can see. Scanning all keys for each
        // property of each section made a self-containing type quadratic (8,000 keys took 29 s).
        private static void BindInto(object obj, Type type, Dictionary<string, string?> values, List<string> sectionScope, string prefix,
            string sep, List<string> errors, int depth)
        {
            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo prop in props)
            {
                if (prop.GetIndexParameters().Length != 0) continue;
                // A property hidden with 'new' is listed beside the one hiding it: the key belongs to the most derived. The
                // base one was bound too, so a derived string X over a base int X failed as "cannot convert 'hello'".
                if (IsHidden(prop, props)) continue;
                string key = prefix.Length == 0 ? prop.Name : prefix + sep + prop.Name;
                Type pt = prop.PropertyType;
                List<string> scope = OwnScope(sectionScope, props, prop, prefix, sep);

                if (IsScalar(pt))
                {
                    // Null is unset, as it is for every other kind of property: it overwrote a string's default, and was a
                    // conversion error ("cannot convert ''") for an int.
                    if (values.TryGetValue(key, out string? raw) && raw != null)
                    {
                        if (!CanSet(prop)) errors.Add($"'{key}': the property is read-only, so it cannot be bound.");
                        else if (TryConvertScalar(raw, pt, out object? value)) TrySet(prop, obj, value, key, errors);
                        else errors.Add($"'{key}': cannot convert '{Shown(raw)}' to {FriendlyName(pt)}.");
                    }
                    // "Port:0=5" on an int was dropped without a word.
                    if (HasSection(scope, key + sep))
                        errors.Add($"'{key}': {FriendlyName(pt)} is a single value, so nested keys ('{key}{sep}...') cannot be bound.");
                }
                else if (TryGetElementType(pt, out Type elementType))
                {
                    object? sequence = BindSequence(pt, elementType, values, scope, key, sep, errors);
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
                    if (IsConfigured(values, scope, key, sep))
                        errors.Add($"'{key}': {FriendlyName(pt)} is not a supported setting type (scalars, lists of scalars and classes are).");
                }
                else if (Under(scope, key + sep) is { Count: > 0 } section)
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
                    else if (!CanSet(prop))
                    {
                        // A computed getter (Child => new Inner()) gives a new instance on every read: the section was bound
                        // into one nobody kept, and its values were lost without a word.
                        if (!TryGet(prop, obj, key, errors, out object? again)) continue;
                        if (!ReferenceEquals(again, target))
                        {
                            errors.Add($"'{key}': the property is read-only and returns a new {FriendlyName(pt)} on each read, so its section cannot be bound.");
                            continue;
                        }
                    }
                    // Into the instance's own type: a derived instance's extra properties were dropped silently.
                    BindInto(target, target.GetType(), values, section, key, sep, errors, depth + 1);
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

        private static bool IsConfigured(Dictionary<string, string?> values, List<string> scope, string key, string sep)
            => (values.TryGetValue(key, out string? v) && !string.IsNullOrEmpty(v)) || HasSection(scope, key + sep);

        // The keys of scope that belong to prop: not those of a sibling whose name extends prop's past a separator
        // ("Port_Name" beside "Port" with "_"). They were read as prop's nested keys: an int Port beside a string
        // Port_Name was "nested keys cannot be bound", and a "Max__Size" key created an empty Max with "__".
        private static List<string> OwnScope(List<string> scope, PropertyInfo[] props, PropertyInfo prop, string prefix, string sep)
        {
            List<string>? longer = null;
            foreach (PropertyInfo p in props)
                if (p.Name.Length > prop.Name.Length + sep.Length && p.Name.StartsWith(prop.Name + sep, StringComparison.OrdinalIgnoreCase)
                    && p.GetIndexParameters().Length == 0 && !IsHidden(p, props))
                    (longer ??= new List<string>()).Add(prefix.Length == 0 ? p.Name : prefix + sep + p.Name);
            if (longer == null) return scope;
            var own = new List<string>();
            foreach (string k in scope)
            {
                bool sibling = false;
                foreach (string s in longer)
                    if (k.Equals(s, StringComparison.OrdinalIgnoreCase) || k.StartsWith(s + sep, StringComparison.OrdinalIgnoreCase)) { sibling = true; break; }
                if (!sibling) own.Add(k);
            }
            return own;
        }

        // At most 100 characters of a value in an error message, never splitting a surrogate pair: a 1,000,000-character
        // value made a 1,000,000-character exception message.
        private static string Shown(string raw)
        {
            if (raw.Length <= 100) return raw;
            int n = char.IsHighSurrogate(raw[99]) ? 99 : 100;
            return raw.Substring(0, n) + "... (" + raw.Length.ToString(CultureInfo.InvariantCulture) + " characters)";
        }

        private static bool HasSection(List<string> scope, string sectionPrefix)
        {
            foreach (string k in scope)
                if (k.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static List<string> Under(List<string> scope, string sectionPrefix)
        {
            var keys = new List<string>();
            foreach (string k in scope)
                if (k.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase)) keys.Add(k);
            return keys;
        }

        private static object? BindSequence(Type sequenceType, Type elementType,
            Dictionary<string, string?> values, List<string> scope, string key, string sep, List<string> errors)
        {
            var items = new List<object?>();
            int contiguous = 0;
            for (int i = 0; ; i++)
            {
                // A null item is unset, as a null key is: converted, it failed for value types ("cannot convert ''") and
                // put a null into a list of strings.
                if (!values.TryGetValue(key + sep + i.ToString(CultureInfo.InvariantCulture), out string? raw) || raw is null) break;
                // Counted even when it fails to convert: "Ports:0=x, Ports:1=2" also reported a gap at the valid ':1'.
                contiguous++;
                if (TryConvertScalar(raw, elementType, out object? value)) items.Add(value);
                else errors.Add($"'{key}{sep}{i}': cannot convert '{Shown(raw)}' to {FriendlyName(elementType)}.");
            }

            // An index past a gap (":0", ":2") would otherwise be dropped silently, along with everything after it.
            string itemPrefix = key + sep;
            foreach (string k in scope)
            {
                // An empty index ("Hosts:") was skipped here, so its value was dropped without a word.
                if (!k.StartsWith(itemPrefix, StringComparison.OrdinalIgnoreCase) || values[k] is null) continue;
                string index = k.Substring(itemPrefix.Length);
                bool digits = index.Length > 0;
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

        // "1e400" overflows to ∞ on .NET Core but fails on .NET Framework: an error on both. "Infinity" and "∞" are allowed.
        private static bool Overflowed(string s, double v)
            => double.IsInfinity(v) && s.IndexOf("Infinity", StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf('\u221E') < 0;

        // The same on every runtime: .NET Framework refused "infinity" and "nan" in any case but one, and read "-0" as +0.
        // A sign, then Infinity, ∞ or NaN in any case, is the value it names; otherwise the invariant parse, with the sign
        // of a zero kept.
        private static bool TryParseFloat(string s, out double value)
        {
            string t = s.Trim();
            bool minus = t.StartsWith("-", StringComparison.Ordinal);
            string word = t.Length > 0 && (t[0] == '-' || t[0] == '+') ? t.Substring(1) : t;
            if (word.Equals("Infinity", StringComparison.OrdinalIgnoreCase) || word == "\u221E")
            {
                value = minus ? double.NegativeInfinity : double.PositiveInfinity;
                return true;
            }
            if (word.Equals("NaN", StringComparison.OrdinalIgnoreCase)) { value = double.NaN; return true; }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            if (value == 0 && minus) value = -0.0;
            return true;
        }

        // The float nearest the decimal s. .NET Framework parses a float as a double and narrows it, rounding twice: a
        // decimal just off a midpoint between two floats rounded onto it as a double, then to even ("1.0000000596046448"
        // was 1, not 1.0000001). Here the narrowed double is checked against the exact decimal at the midpoints on each
        // side and moved one float if the decimal is past one; at a midpoint exactly, to even.
        private static bool TryParseNearestFloat(string s, out float value)
        {
            value = 0;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
            value = (float)d;
            if (d == 0 || double.IsNaN(d) || double.IsInfinity(d) || !TryExactDecimal(s, out BigInteger digits, out int exp10, out bool sticky))
                return true;
            float f = Math.Abs(value);
            if (float.IsInfinity(f)) f = float.MaxValue;                    // past the last midpoint as a double, maybe not exactly
            int bits = FloatBits(f);
            // The midpoint with the next float up (from float.MaxValue, with 2^128, where it overflows).
            double up = bits == 0x7F7FFFFF ? float.MaxValue + Math.Pow(2, 103) : ((double)f + BitsFloat(bits + 1)) / 2;
            int c = CompareExact(digits, exp10, sticky, up);
            if (c > 0 || c == 0 && (bits & 1) == 1) f = BitsFloat(bits + 1);
            else if (bits > 0)
            {
                c = CompareExact(digits, exp10, sticky, ((double)f + BitsFloat(bits - 1)) / 2);
                if (c < 0 || c == 0 && (bits & 1) == 1) f = BitsFloat(bits - 1);
            }
            value = d < 0 ? -f : f;
            return true;
        }

        private static int FloatBits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        private static float BitsFloat(int bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);

        // The magnitude of an invariant decimal ("-1.5e3") as digits * 10^exp10. Past 800 significant digits the rest only
        // set sticky (the value is above digits * 10^exp10). False for anything else, or an exponent far out of range.
        private static bool TryExactDecimal(string s, out BigInteger digits, out int exp10, out bool sticky)
        {
            digits = BigInteger.Zero;
            exp10 = 0;
            sticky = false;
            int i = 0;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            var sb = new StringBuilder();
            long scale = 0;
            bool dot = false;
            for (; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '.' && !dot) { dot = true; continue; }
                if (ch < '0' || ch > '9') break;
                if (sb.Length == 0 && ch == '0') { if (dot) scale--; continue; }
                if (sb.Length < 800) { sb.Append(ch); if (dot) scale--; }
                else { if (ch != '0') sticky = true; if (!dot) scale++; }
            }
            if (i < s.Length)
            {
                if (s[i] != 'e' && s[i] != 'E') return false;
                if (!int.TryParse(s.Substring(i + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int e)) return false;
                scale += e;
            }
            if (sb.Length == 0 || Math.Abs(scale) > 2000) return false;
            digits = BigInteger.Parse(sb.ToString(), CultureInfo.InvariantCulture);
            exp10 = (int)scale;
            return true;
        }

        // The sign of digits * 10^exp10 (+ sticky) minus m, a positive finite double, exactly.
        private static int CompareExact(BigInteger digits, int exp10, bool sticky, double m)
        {
            long b = BitConverter.DoubleToInt64Bits(m);
            int e = (int)((b >> 52) & 0x7FF);
            BigInteger a = digits, man = b & ((1L << 52) - 1);
            if (e == 0) e = 1; else man += 1L << 52;
            int k = e - 1075;
            if (exp10 >= 0) a *= BigInteger.Pow(10, exp10); else man *= BigInteger.Pow(10, -exp10);
            if (k >= 0) man <<= k; else a <<= -k;
            int c = a.CompareTo(man);
            return c == 0 && sticky ? 1 : c;
        }

        private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

        private static bool IsScalar(Type type)
        {
            Type t = Unwrap(type);
            return t.IsPrimitive || t.IsEnum
                || t == typeof(string) || t == typeof(decimal)
                || t == typeof(TimeSpan) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid);
        }

        // Not optimized: on .NET 8.0.3, tier-1 PGO code for this method evaluated the char branch's "nullable and empty"
        // test as false for char? and "" with both operands true (seen by logging them), so an empty char? setting failed
        // to bind in about one run in four; rewriting the test did not help. Binding is not a hot path.
        [MethodImpl(MethodImplOptions.NoOptimization)]
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
                if (TryParseFloat(s, out double d) && !Overflowed(s, d)) { value = d; return true; }
                return false;
            }
            if (t == typeof(float))
            {
                // The nearest float to the decimal (.NET Framework's float parse rounded twice); the words and -0 as a double.
                if (TryParseFloat(s, out double fd) && (double.IsNaN(fd) || fd == 0 || (double.IsInfinity(fd) && !Overflowed(s, fd))))
                {
                    value = (float)fd;
                    return true;
                }
                if (TryParseNearestFloat(s, out float fl) && !Overflowed(s, fl)) { value = fl; return true; }
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

            bool flags = enumType.GetCustomAttribute<FlagsAttribute>() != null;
            // Enum.Parse ORs comma-separated names whatever the type: "Red, Green" of a plain enum bound as Blue (1 | 2).
            if (!flags && s.IndexOf(',') >= 0) return false;
            if (Enum.IsDefined(enumType, parsed)) { value = parsed; return true; }
            if (!flags) return false;
            ulong bits = ToBits(parsed), allowed = 0;
            foreach (object member in Enum.GetValues(enumType)) allowed |= ToBits(member);
            if ((bits & ~allowed) != 0) return false;
            value = parsed;
            return true;
        }

        private static bool IsHidden(PropertyInfo prop, PropertyInfo[] props)
        {
            foreach (PropertyInfo other in props)
                if (other != prop && other.Name == prop.Name && other.GetIndexParameters().Length == 0
                    && other.DeclaringType != null && prop.DeclaringType != null && other.DeclaringType.IsSubclassOf(prop.DeclaringType))
                    return true;
            return false;
        }

        private static ulong ToBits(object enumValue)
            => unchecked(Type.GetTypeCode(Enum.GetUnderlyingType(enumValue.GetType())) switch
            {
                TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => (ulong)Convert.ToInt64(enumValue, CultureInfo.InvariantCulture),
                _ => Convert.ToUInt64(enumValue, CultureInfo.InvariantCulture),
            });

        // "1m 30s", "250ms", "2d" (integer terms, optionally negative), or a TimeSpan string containing a colon.
        private static bool TryParseDuration(string s, out TimeSpan value) => TryParseDurationCore(s, allowNegative: true, out value);

        // One duration grammar, copied verbatim in ConfigBinder and EnvironmentVariables (the same value often reaches
        // both, and "1.5h" or "5 s" worked in one but not the other, "1m 30s" the reverse): a colon form ("00:05:00",
        // "1.02:00:00"), or one or more terms of a decimal number and a unit (d, h, m, s, ms, any case), as in "1h 30m",
        // "1.5h", "5 s" or "250ms". A bare number has no unit and fails. Ticks are truncated toward zero, from the exact
        // sum of the terms.
        private static bool TryParseDurationCore(string s, bool allowNegative, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            s = s.Trim();
            if (s.Length == 0) return false;
            if (s.IndexOf(':') >= 0)
                return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out value) && (allowNegative || value >= TimeSpan.Zero);
            int i = 0;
            bool negative = s[0] == '-';
            if (negative) { if (!allowNegative) return false; i++; }
            else if (s[0] == '+') i++;
            BigInteger ticks = 0;                                               // the sum so far is ticks / 10^scale
            int scale = 0;
            // TimeSpan.MinValue's magnitude is one past long.MaxValue: "-922337203685.4775808s" was refused.
            BigInteger limit = negative ? (BigInteger)long.MaxValue + 1 : long.MaxValue;
            bool sawTerm = false;
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                int start = i;
                while (i < s.Length && (s[i] >= '0' && s[i] <= '9' || s[i] == '.')) i++;
                // An exponent ("1e3s"): an 'e' followed by a digit, or a sign and a digit.
                if (i > start && i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    int j = i + 1;
                    if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
                    if (j < s.Length && s[j] >= '0' && s[j] <= '9')
                    {
                        i = j;
                        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                    }
                }
                string token = s.Substring(start, i - start);
                if (!double.TryParse(token, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture, out double number) || double.IsInfinity(number)) return false;
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
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
                // In double first, so a term far past TimeSpan's range stops before the exact sum grows; loosely, since the
                // double rounds "922337203685.4775807s" (the largest TimeSpan) to 2^63. The sum itself is exact: through a
                // double, "99999999.99999999s" kept 15 digits and rounded up to 10^15 ticks.
                double t = number * perUnit;
                if (!(t < 1e19)) return false;
                AddExact(token, perUnit, ref ticks, ref scale);
                if (ticks > limit * BigInteger.Pow(10, scale)) return false;
                sawTerm = true;
            }
            if (!sawTerm) return false;
            BigInteger whole = ticks / BigInteger.Pow(10, scale);
            value = TimeSpan.FromTicks((long)(negative ? -whole : whole));
            return true;

            // Adds the term's number ("12.5", "1e3", "0.5E-2") times perUnit to sum / 10^scale. Digits more than 400 places
            // below a unit are cut (worth under 10^-388 of a tick), so a long run of digits costs nothing.
            static void AddExact(string token, long perUnit, ref BigInteger sum, ref int scale)
            {
                int e = token.IndexOfAny(new[] { 'e', 'E' });
                string mantissa = e < 0 ? token : token.Substring(0, e);
                long exp = 0;
                // An exponent too big for a long is negative here (a positive one failed the double check): the term vanishes.
                if (e >= 0 && !long.TryParse(token.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exp)) return;
                int dot = mantissa.IndexOf('.');
                string digits = (dot < 0 ? mantissa : mantissa.Remove(dot, 1)).TrimStart('0');
                if (digits.Length == 0) return;
                long termScale = (dot < 0 ? 0 : mantissa.Length - dot - 1) - exp;      // the number is digits / 10^termScale
                const int MaxScale = 400;
                if (termScale > MaxScale)
                {
                    long cut = termScale - MaxScale;
                    if (cut >= digits.Length) return;
                    digits = digits.Substring(0, digits.Length - (int)cut);
                    termScale = MaxScale;
                }
                BigInteger m = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * perUnit;
                // The double check bounds the term, so a negative scale is at most about 19 places.
                if (termScale < 0) { m *= BigInteger.Pow(10, (int)-termScale); termScale = 0; }
                int ts = (int)termScale;
                if (ts > scale) { sum *= BigInteger.Pow(10, ts - scale); scale = ts; }
                else m *= BigInteger.Pow(10, scale - ts);
                sum += m;
            }
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
