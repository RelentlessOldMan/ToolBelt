// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace ToolBelt.Configuration
{
    /// <summary>
    /// Typed environment-variable reading with clear errors: <c>GetInt("PORT", 8080)</c>, <c>GetBool("VERBOSE")</c>,
    /// <c>GetTimeSpan("TIMEOUT")</c>, <c>Require("API_URL")</c>. A variable that is set but malformed throws a
    /// <see cref="FormatException"/> naming it (never silently falling back to the default — that hides typos in
    /// deployment config); an empty value counts as unset. Booleans accept true/false/1/0/yes/no/on/off; durations accept
    /// "30s", "5m", "1.5h", "250ms", "2d" or a TimeSpan string with a colon ("00:05:00"); a bare number is rejected as
    /// ambiguous (TimeSpan would read "30" as 30 days). Enumerations accept a defined member, or a combination of
    /// defined bits for a [Flags] type. The variable source is injectable for tests.
    /// </summary>
    public sealed class EnvironmentVariables
    {
        private readonly Func<string, string?> _get;

        /// <param name="source">Lookup function (default: the process environment).</param>
        public EnvironmentVariables(Func<string, string?>? source = null) => _get = source ?? Environment.GetEnvironmentVariable;

        /// <summary>Reads from a fixed dictionary (tests, or a parsed .env file).</summary>
        public static EnvironmentVariables From(IReadOnlyDictionary<string, string> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            return new EnvironmentVariables(k => values.TryGetValue(k, out var v) ? v : null);
        }

        /// <summary>The process environment.</summary>
        public static EnvironmentVariables Process { get; } = new EnvironmentVariables();

        /// <summary>The value, or null when unset or empty.</summary>
        public string? Get(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0) throw new ArgumentException("Name is required.", nameof(name));
            // The OS reads a name only up to a NUL, so "X\0junk" returned X's value. '=' ends a name too (one may start
            // with it on Windows: "=C:").
            if (name.IndexOf('\0') >= 0 || name.IndexOf('=', 1) >= 0)
                throw new ArgumentException("A variable name cannot contain NUL or '=' (other than a leading '=').", nameof(name));
            string? v = _get(name);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public string GetString(string name, string defaultValue) => Get(name) ?? defaultValue;

        /// <summary>The value, or an <see cref="InvalidOperationException"/> naming the missing variable.</summary>
        public string Require(string name)
            => Get(name) ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

        /// <summary>Checks several at once and lists every missing one in a single error.</summary>
        public void RequireAll(params string[] names)
        {
            if (names is null) throw new ArgumentNullException(nameof(names));   // was LINQ's "source"
            // A null or empty element was reported as Get's "name", not as the caller's argument.
            foreach (string n in names)
            {
                if (string.IsNullOrEmpty(n)) throw new ArgumentException("Variable names must not be null or empty.", nameof(names));
                // As for an empty one: Get rejected "A=B" and "A\0B" as its own "name".
                if (n.IndexOf('\0') >= 0 || n.IndexOf('=', 1) >= 0)
                    throw new ArgumentException("A variable name cannot contain NUL or '=' (other than a leading '=').", nameof(names));
            }
            var missing = names.Where(n => Get(n) == null).ToList();
            if (missing.Count > 0) throw new InvalidOperationException("Required environment variables not set: " + string.Join(", ", missing) + ".");
        }

        public int GetInt(string name, int defaultValue) => GetInt(name) ?? defaultValue;
        public int? GetInt(string name) => Parse(name, s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : (int?)null, "an integer");

        public long GetLong(string name, long defaultValue) => Parse(name, s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : (long?)null, "an integer") ?? defaultValue;

        public double GetDouble(string name, double defaultValue) => GetDouble(name) ?? defaultValue;
        // "1e400" overflowed to ∞ on .NET Core but failed on .NET Framework: an error on both ("Infinity" and "∞" are allowed).
        public double? GetDouble(string name) => Parse(name, s => TryParseFloat(s, out double v)
            && !(double.IsInfinity(v) && s.IndexOf("Infinity", StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf('∞') < 0) ? v : (double?)null, "a number");

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

        public bool GetBool(string name, bool defaultValue = false) => GetBoolOrNull(name) ?? defaultValue;
        public bool? GetBoolOrNull(string name) => Parse(name, ParseBool, "a boolean (true/false, 1/0, yes/no, on/off)");

        public TimeSpan GetTimeSpan(string name, TimeSpan defaultValue) => GetTimeSpan(name) ?? defaultValue;
        public TimeSpan? GetTimeSpan(string name) => Parse(name, ParseDuration, "a duration such as 30s, 5m, 1.5h, 250ms or 00:05:00");

        public TEnum GetEnum<TEnum>(string name, TEnum defaultValue) where TEnum : struct, Enum
            => Parse(name, s => (s.IndexOf(',') < 0 || typeof(TEnum).GetCustomAttribute<FlagsAttribute>() != null)
                    && Enum.TryParse(s, true, out TEnum v) && IsValidEnum(v) ? v : (TEnum?)null,
                "one of " + string.Join(", ", Enum.GetNames(typeof(TEnum)))) ?? defaultValue;

        /// <summary>A delimited list (default ',' or ';'), items trimmed, empties dropped; empty list when unset.</summary>
        public IReadOnlyList<string> GetList(string name, params char[] separators)
        {
            string? v = Get(name);
            if (v == null) return Array.Empty<string>();
            return v.Split(separators != null && separators.Length > 0 ? separators : new[] { ',', ';' }).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        /// <summary>
        /// All variables whose names start with <paramref name="prefix"/> (process environment only, prefix matched
        /// ignoring case), prefix removed. Keys compare ignoring case on Windows, where the environment does too, and
        /// exactly elsewhere, so <c>APP_X</c> and <c>app_x</c> stay separate on Linux. Two variables that differ only in
        /// the prefix's case (<c>APP_x</c> and <c>app_x</c>, on Linux) would strip to the same key, so that throws an
        /// <see cref="InvalidOperationException"/> naming both rather than keeping one.
        /// </summary>
        public static IReadOnlyDictionary<string, string> WithPrefix(string prefix, bool stripPrefix = true)
        {
            if (prefix is null) throw new ArgumentNullException(nameof(prefix));
            return WithPrefix(Environment.GetEnvironmentVariables(), Environment.OSVersion.Platform == PlatformID.Win32NT, prefix, stripPrefix);
        }

        internal static IReadOnlyDictionary<string, string> WithPrefix(IDictionary variables, bool windows, string prefix, bool stripPrefix)
        {
            var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var result = new SortedDictionary<string, string>(comparer);
            var from = new Dictionary<string, string>(comparer);
            foreach (DictionaryEntry e in variables)
            {
                string key = (string)e.Key;
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string k = stripPrefix ? key.Substring(prefix.Length) : key;
                // One silently replaced the other, depending on the environment's order.
                if (from.TryGetValue(k, out string? other))
                    throw new InvalidOperationException($"Environment variables '{other}' and '{key}' both give the key '{k}'.");
                from[k] = key;
                result[k] = (string?)e.Value ?? "";
            }
            return result;
        }

        private T? Parse<T>(string name, Func<string, T?> parse, string expected) where T : struct
        {
            string? raw = Get(name);
            if (raw == null) return null;
            return parse(raw.Trim()) ?? throw new FormatException($"Environment variable '{name}' is '{Shown(raw)}', which is not {expected}.");
        }

        // At most 100 characters of a value in an error message, never splitting a surrogate pair: a 1,000,000-character
        // value made a 1,000,000-character exception message.
        private static string Shown(string raw)
        {
            if (raw.Length <= 100) return raw;
            int n = char.IsHighSurrogate(raw[99]) ? 99 : 100;
            return raw.Substring(0, n) + "... (" + raw.Length.ToString(CultureInfo.InvariantCulture) + " characters)";
        }

        // Defined, or (for a [Flags] type) made only of defined bits — so "Read, Write" is accepted. A comma list is refused
        // for a plain enum above: Enum.TryParse ORs the names, so "Red, Green" read as Blue (1 | 2).
        private static bool IsValidEnum<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            if (Enum.IsDefined(typeof(TEnum), value)) return true;
            if (typeof(TEnum).GetCustomAttribute<FlagsAttribute>() == null) return false;
            ulong bits = ToBits(value), allowed = 0;
            foreach (object member in Enum.GetValues(typeof(TEnum))) allowed |= ToBits(member);
            return (bits & ~allowed) == 0;
        }

        private static ulong ToBits(object enumValue)
            => unchecked(Type.GetTypeCode(Enum.GetUnderlyingType(enumValue.GetType())) switch
            {
                TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => (ulong)Convert.ToInt64(enumValue, CultureInfo.InvariantCulture),
                _ => Convert.ToUInt64(enumValue, CultureInfo.InvariantCulture),
            });

        internal static bool? ParseBool(string s)
        {
            switch (s.ToLowerInvariant())
            {
                // Exactly the documented words (and ConfigBinder's): "y" and "n" were accepted too.
                case "true": case "1": case "yes": case "on": return true;
                case "false": case "0": case "no": case "off": return false;
                default: return null;
            }
        }

        // Negative durations are refused here, as before (a timeout or interval read from the environment).
        internal static TimeSpan? ParseDuration(string s) => TryParseDurationCore(s, allowNegative: false, out TimeSpan t) ? t : (TimeSpan?)null;

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
                if (ticks > long.MaxValue * BigInteger.Pow(10, scale)) return false;
                sawTerm = true;
            }
            if (!sawTerm) return false;
            long whole = (long)(ticks / BigInteger.Pow(10, scale));
            value = TimeSpan.FromTicks(negative ? -whole : whole);
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
    }
}
