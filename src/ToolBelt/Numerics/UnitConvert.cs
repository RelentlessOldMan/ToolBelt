// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Table-driven unit conversion for length, mass, time, angle and data size, plus temperature (which is
    /// affine, not a simple factor, and is handled specially). Conversions go through each category's base
    /// unit, so every pair is consistent by construction. Unit names are case-sensitive (SI convention).
    /// </summary>
    public static class UnitConvert
    {
        // unit -> (category, factor-to-base). Temperature units carry a sentinel factor and route specially.
        private static readonly Dictionary<string, (string Category, double ToBase)> Units =
            new Dictionary<string, (string, double)>(StringComparer.Ordinal)
            {
                // length, base metre
                ["m"] = ("length", 1), ["km"] = ("length", 1000), ["cm"] = ("length", 0.01),
                ["mm"] = ("length", 0.001), ["um"] = ("length", 1e-6),
                ["mi"] = ("length", 1609.344), ["yd"] = ("length", 0.9144),
                ["ft"] = ("length", 0.3048), ["in"] = ("length", 0.0254), ["nmi"] = ("length", 1852),
                // mass, base kilogram
                ["kg"] = ("mass", 1), ["g"] = ("mass", 0.001), ["mg"] = ("mass", 1e-6),
                ["t"] = ("mass", 1000), ["lb"] = ("mass", 0.45359237), ["oz"] = ("mass", 0.028349523125),
                // time, base second
                ["s"] = ("time", 1), ["ms"] = ("time", 0.001), ["us"] = ("time", 1e-6), ["ns"] = ("time", 1e-9),
                ["min"] = ("time", 60), ["h"] = ("time", 3600), ["d"] = ("time", 86400), ["wk"] = ("time", 604800),
                // angle, base radian
                ["rad"] = ("angle", 1), ["deg"] = ("angle", Math.PI / 180),
                ["grad"] = ("angle", Math.PI / 200), ["turn"] = ("angle", 2 * Math.PI),
                // data size, base byte
                ["B"] = ("data", 1), ["KB"] = ("data", 1e3), ["MB"] = ("data", 1e6),
                ["GB"] = ("data", 1e9), ["TB"] = ("data", 1e12),
                ["KiB"] = ("data", 1024d), ["MiB"] = ("data", 1024d * 1024),
                ["GiB"] = ("data", 1024d * 1024 * 1024), ["TiB"] = ("data", 1024d * 1024 * 1024 * 1024),
                // temperature (affine): factor is unused, presence marks the category
                ["C"] = ("temperature", double.NaN), ["F"] = ("temperature", double.NaN), ["K"] = ("temperature", double.NaN),
            };

        public static double Convert(double value, string fromUnit, string toUnit)
        {
            if (fromUnit is null) throw new ArgumentNullException(nameof(fromUnit));
            if (toUnit is null) throw new ArgumentNullException(nameof(toUnit));
            if (!Units.TryGetValue(fromUnit, out var from)) throw new ArgumentException($"Unknown unit '{fromUnit}'.", nameof(fromUnit));
            if (!Units.TryGetValue(toUnit, out var to)) throw new ArgumentException($"Unknown unit '{toUnit}'.", nameof(toUnit));
            if (from.Category != to.Category)
                throw new ArgumentException($"Cannot convert '{fromUnit}' ({from.Category}) to '{toUnit}' ({to.Category}).");

            if (from.Category == "temperature")
                return FromKelvin(ToKelvin(value, fromUnit), toUnit);

            return value * from.ToBase / to.ToBase;
        }

        /// <summary>Parses a "value unit" string such as "5 km" or "3.2kg" into its number and unit.</summary>
        public static bool TryParse(string text, out double value, out string unit)
        {
            value = 0;
            unit = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '-' || s[i] == '+' || s[i] == 'e' || s[i] == 'E'))
                i++;

            string numberPart = s.Substring(0, i);
            if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            unit = s.Substring(i).Trim();
            return true;
        }

        private static double ToKelvin(double value, string unit)
        {
            switch (unit)
            {
                case "K": return value;
                case "C": return value + 273.15;
                case "F": return (value - 32) * 5.0 / 9.0 + 273.15;
                default: throw new ArgumentException($"Unknown temperature unit '{unit}'.");
            }
        }

        private static double FromKelvin(double kelvin, string unit)
        {
            switch (unit)
            {
                case "K": return kelvin;
                case "C": return kelvin - 273.15;
                case "F": return (kelvin - 273.15) * 9.0 / 5.0 + 32;
                default: throw new ArgumentException($"Unknown temperature unit '{unit}'.");
            }
        }
    }
}
