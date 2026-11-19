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
                throw new ArgumentException($"Cannot convert '{fromUnit}' ({from.Category}) to '{toUnit}' ({to.Category}).", nameof(toUnit));

            if (fromUnit == toUnit) return value; // exact: the round trip through the base unit was not

            if (from.Category == "temperature")
            {
                // Celsius ↔ Fahrenheit directly: via kelvin, 0.1 °C came back as 0.10000000000002274.
                if (fromUnit == "C" && toUnit == "F") return Ratio(value, 9.0, 5.0) + 32;
                if (fromUnit == "F" && toUnit == "C") return Ratio(value - 32, 5.0, 9.0);
                return FromKelvin(ToKelvin(value, fromUnit), toUnit);
            }

            // Ratio first: value · factor overflowed for 1e308 km. The table's factors are short decimals, so their ratio
            // is taken in decimal: in doubles 0.3048 / 0.0254 is 12.000000000000002 and 0.001 / 1e-6 is 999.9999999999999.
            // Angles carry π and stay in doubles, unless neither side is radians: then the ratio is a fraction of whole
            // numbers (degrees, gradians and turns per turn), and π/180 over π/200 made 90 deg 99.99999999999999 grad.
            if (from.Category == "angle" && fromUnit != "rad" && toUnit != "rad")
                return ExactRatio(value, PerTurn(toUnit), PerTurn(fromUnit));
            if (from.Category == "angle") return value * (from.ToBase / to.ToBase);
            // And then value·num/den rounded once, for the ratio as a fraction of whole numbers: the ratio rounded to a
            // double and multiplied rounded twice, so 9 m was 0.009000000000000001 km.
            if (WholeRatio(from.ToBase, to.ToBase, out double num, out double den))
            {
                double a = Math.Abs(value);
                if (a > 1e-250 && a < 1e250) return ExactRatio(value, num, den);
            }
            return value * (double)((decimal)from.ToBase / (decimal)to.ToBase);
        }

        // from / to as num / den in lowest terms, both whole numbers to 2^53 (so exact doubles); false if larger. The
        // factors are short decimals: F / 10^sf over T / 10^st is F·10^st / (T·10^sf).
        private static bool WholeRatio(double from, double to, out double num, out double den)
        {
            decimal n = (decimal)from, d = (decimal)to;
            int scale = ((decimal.GetBits(n)[3] >> 16) & 0xFF) + ((decimal.GetBits(d)[3] >> 16) & 0xFF);
            for (int i = 0; i < scale; i++) { n *= 10; d *= 10; }
            decimal x = n, y = d;
            while (y != 0) { decimal r = x % y; x = y; y = r; }
            n /= x;
            d /= x;
            num = den = 0;
            if (n != decimal.Truncate(n) || d != decimal.Truncate(d) || n > 9007199254740992m || d > 9007199254740992m) return false;
            num = (double)n;
            den = (double)d;
            return true;
        }

        /// <summary>Parses a "value unit" string such as "5 km" or "3.2kg" into its number and unit. Returns false
        /// unless the unit is one <see cref="Convert"/> knows (and so for a bare number or trailing junk).</summary>
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
            // A finite number only: net8.0 parses an overflowing "1e400" as infinity where .NET Framework fails, and the
            // scanner above cannot spell "Infinity", so an infinite value is always an overflow.
            if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsInfinity(value))
            { value = 0; return false; }
            // "5 bananas" and "3,5 kg" (unit ",5 kg") used to succeed and fail later in Convert.
            string rest = s.Substring(i).Trim();
            if (!Units.ContainsKey(rest)) { value = 0; return false; }
            unit = rest;
            return true;
        }

        private static double ToKelvin(double value, string unit)
        {
            switch (unit)
            {
                case "K": return value;
                case "C": return value + 273.15;
                case "F": return Ratio(value - 32, 5.0, 9.0) + 273.15;
                default: throw new ArgumentException($"Unknown temperature unit '{unit}'.", nameof(unit));
            }
        }

        private static double FromKelvin(double kelvin, string unit)
        {
            switch (unit)
            {
                case "K": return kelvin;
                case "C": return kelvin - 273.15;
                case "F": return Ratio(kelvin - 273.15, 9.0, 5.0) + 32;
                default: throw new ArgumentException($"Unknown temperature unit '{unit}'.", nameof(unit));
            }
        }

        // value·num/den, multiplying first (as before, for ordinary values) unless that overflows: 9e307 °C is 1.62e308 °F,
        // but came back as ∞ through 8.1e308.
        private static double PerTurn(string unit) => unit == "deg" ? 360 : unit == "grad" ? 400 : 1;

        // value·num/den rounded once (num and den small whole numbers): the product is kept exactly as a pair (Dekker's
        // two-product, so no FMA is needed on netstandard2.0) and the quotient corrected by its remainder. Outside the
        // range where the split is exact it falls back to Ratio.
        private static double ExactRatio(double value, double num, double den)
        {
            double a = Math.Abs(value);
            if (!(a > 1e-290 && a < 1e290)) return Ratio(value, num, den);
            var (p, pe) = TwoProduct(value, num);
            double q = p / den;
            var (t, te) = TwoProduct(q, den);
            return q + (((p - t) - te) + pe) / den;
        }

        private static (double Product, double Error) TwoProduct(double a, double b)
        {
            double p = a * b;
            double ca = 134217729.0 * a, ah = ca - (ca - a), al = a - ah;
            double cb = 134217729.0 * b, bh = cb - (cb - b), bl = b - bh;
            return (p, ((ah * bh - p) + ah * bl + al * bh) + al * bl);
        }

        private static double Ratio(double value, double num, double den)
        {
            double r = value * num / den;
            return double.IsInfinity(r) && !double.IsInfinity(value) ? value / den * num : r;
        }
    }
}
