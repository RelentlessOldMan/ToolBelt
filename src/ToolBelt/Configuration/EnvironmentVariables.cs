// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ToolBelt.Configuration
{
    /// <summary>
    /// Typed environment-variable reading with clear errors: <c>GetInt("PORT", 8080)</c>, <c>GetBool("VERBOSE")</c>,
    /// <c>GetTimeSpan("TIMEOUT")</c>, <c>Require("API_URL")</c>. A variable that is set but malformed throws a
    /// <see cref="FormatException"/> naming it (never silently falling back to the default — that hides typos in
    /// deployment config); an empty value counts as unset. Booleans accept true/false/1/0/yes/no/on/off; durations accept
    /// "30s", "5m", "1.5h", "250ms", "2d" or a TimeSpan string. The variable source is injectable for tests.
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
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.", nameof(name));
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
            var missing = names.Where(n => Get(n) == null).ToList();
            if (missing.Count > 0) throw new InvalidOperationException("Required environment variables not set: " + string.Join(", ", missing) + ".");
        }

        public int GetInt(string name, int defaultValue) => GetInt(name) ?? defaultValue;
        public int? GetInt(string name) => Parse(name, s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : (int?)null, "an integer");

        public long GetLong(string name, long defaultValue) => Parse(name, s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : (long?)null, "an integer") ?? defaultValue;

        public double GetDouble(string name, double defaultValue) => GetDouble(name) ?? defaultValue;
        public double? GetDouble(string name) => Parse(name, s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null, "a number");

        public bool GetBool(string name, bool defaultValue = false) => GetBoolOrNull(name) ?? defaultValue;
        public bool? GetBoolOrNull(string name) => Parse(name, ParseBool, "a boolean (true/false, 1/0, yes/no, on/off)");

        public TimeSpan GetTimeSpan(string name, TimeSpan defaultValue) => GetTimeSpan(name) ?? defaultValue;
        public TimeSpan? GetTimeSpan(string name) => Parse(name, ParseDuration, "a duration such as 30s, 5m, 1.5h, 250ms or 00:05:00");

        public TEnum GetEnum<TEnum>(string name, TEnum defaultValue) where TEnum : struct, Enum
            => Parse(name, s => Enum.TryParse(s, true, out TEnum v) && Enum.IsDefined(typeof(TEnum), v) ? v : (TEnum?)null,
                "one of " + string.Join(", ", Enum.GetNames(typeof(TEnum)))) ?? defaultValue;

        /// <summary>A delimited list (default ',' or ';'), items trimmed, empties dropped; empty list when unset.</summary>
        public IReadOnlyList<string> GetList(string name, params char[] separators)
        {
            string? v = Get(name);
            if (v == null) return Array.Empty<string>();
            return v.Split(separators.Length > 0 ? separators : new[] { ',', ';' }).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        }

        /// <summary>All variables whose names start with <paramref name="prefix"/> (process environment only), prefix removed.</summary>
        public static IReadOnlyDictionary<string, string> WithPrefix(string prefix, bool stripPrefix = true)
        {
            if (prefix is null) throw new ArgumentNullException(nameof(prefix));
            var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            {
                string key = (string)e.Key;
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result[stripPrefix ? key.Substring(prefix.Length) : key] = (string?)e.Value ?? "";
            }
            return result;
        }

        private T? Parse<T>(string name, Func<string, T?> parse, string expected) where T : struct
        {
            string? raw = Get(name);
            if (raw == null) return null;
            return parse(raw.Trim()) ?? throw new FormatException($"Environment variable '{name}' is '{raw}', which is not {expected}.");
        }

        internal static bool? ParseBool(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "true": case "1": case "yes": case "y": case "on": return true;
                case "false": case "0": case "no": case "n": case "off": return false;
                default: return null;
            }
        }

        internal static TimeSpan? ParseDuration(string s)
        {
            string[] units = { "ms", "s", "m", "h", "d" };
            foreach (string unit in units)
            {
                if (!s.EndsWith(unit, StringComparison.OrdinalIgnoreCase)) continue;
                string number = s.Substring(0, s.Length - unit.Length).Trim();
                if (unit == "s" && number.EndsWith("m", StringComparison.OrdinalIgnoreCase)) continue;   // "ms" handled first
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || double.IsInfinity(v)) return null;
                double ms = unit switch { "ms" => v, "s" => v * 1e3, "m" => v * 6e4, "h" => v * 3.6e6, _ => v * 8.64e7 };
                return ms > TimeSpan.MaxValue.TotalMilliseconds ? (TimeSpan?)null : TimeSpan.FromMilliseconds(ms);
            }
            return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out TimeSpan t) && t >= TimeSpan.Zero ? t : (TimeSpan?)null;
        }
    }
}
