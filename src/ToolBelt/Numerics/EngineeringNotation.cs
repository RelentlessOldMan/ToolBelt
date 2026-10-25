// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Formats a value with an SI prefix (yocto through yotta) at a chosen significant-figure count, and
    /// parses the same form back. Appears in every plot axis label, report table and console readout, and
    /// is otherwise re-derived badly. Round-trips to the chosen precision (NaN and ±infinity format as
    /// "NaN" / "Infinity" / "-Infinity" and parse back); values beyond the yocto–yotta range use E notation with
    /// no prefix. More than 17 significant digits are treated as 17 (all a double holds). Parsing accepts the
    /// micro sign (U+00B5), Greek mu (U+03BC) and ASCII 'u' for micro.
    /// </summary>
    public static class EngineeringNotation
    {
        // Index 0 == exponent 0; positive indices step up by 3, negative down by 3.
        private static readonly string[] PositivePrefixes = { "", "k", "M", "G", "T", "P", "E", "Z", "Y" };
        private static readonly string[] NegativePrefixes = { "", "m", "µ", "n", "p", "f", "a", "z", "y" };
        private const int MaxExponent = 24;

        /// <summary>Formats <paramref name="value"/> as a number with an SI prefix, e.g. 1500 -> "1.5 k".</summary>
        public static string Format(double value, int significantDigits = 3)
        {
            if (significantDigits < 1) throw new ArgumentOutOfRangeException(nameof(significantDigits), significantDigits, "Must be at least 1.");
            if (double.IsNaN(value) || double.IsInfinity(value))
                return value.ToString(CultureInfo.InvariantCulture);
            if (value == 0) return "0";
            if (significantDigits > 17) significantDigits = 17; // 10^400 overflowed RoundToSignificant to NaN

            int exponent = (int)(Math.Floor(Math.Log10(Math.Abs(value)) / 3.0) * 3);
            // Clamping to yocto/yotta printed "1E-06 y" and "1230000 Y"; out of range, plain E notation reads better.
            if (exponent > MaxExponent || exponent < -MaxExponent)
                return value.ToString("G" + significantDigits, CultureInfo.InvariantCulture);

            double scaled = ScaleDown(value, exponent);
            // Rounding can push e.g. 999.95 up to 1000; renormalize one step if so.
            double rounded = RoundToSignificant(scaled, significantDigits);
            if (Math.Abs(rounded) >= 1000)
            {
                if (exponent == MaxExponent) return RoundToSignificant(value, significantDigits).ToString("G" + significantDigits, CultureInfo.InvariantCulture);
                exponent += 3;
                scaled = ScaleDown(value, exponent);
                rounded = RoundToSignificant(scaled, significantDigits);
            }

            string number;
            if (Math.Abs(rounded) >= 1 && Math.Abs(rounded) < 1e15)
            {
                // Fixed point: "G" switched to E notation whenever the integer digits exceeded the requested
                // digits (120 at 2 digits became "1.2E+02"). Trailing zeros are trimmed as "G" did.
                int integerDigits = 1;
                for (double t = Math.Abs(rounded); t >= 10; t /= 10) integerDigits++;
                number = rounded.ToString("F" + Math.Max(0, significantDigits - integerDigits), CultureInfo.InvariantCulture);
                if (number.IndexOf('.') >= 0) number = number.TrimEnd('0').TrimEnd('.');
            }
            else
            {
                number = rounded.ToString("G" + significantDigits, CultureInfo.InvariantCulture); // far beyond yocto/yotta
            }
            string prefix = PrefixFor(exponent);
            return prefix.Length == 0 ? number : number + " " + prefix;
        }

        public static double Parse(string text)
        {
            if (!TryParse(text, out double value))
                throw new FormatException($"'{text}' is not a valid engineering-notation value.");
            return value;
        }

        public static bool TryParse(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();
            // A plain number first, so Format's "NaN" / "Infinity" round-trip (the 'y' of "Infinity" is yocto).
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            char last = s[s.Length - 1];
            int exponent;
            if (char.IsLetter(last) || last == 'µ' || last == 'μ')
            {
                if (!TryPrefixExponent(last, out exponent)) return false;
                s = s.Substring(0, s.Length - 1).Trim();
            }
            else
            {
                exponent = 0;
            }

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double mantissa))
                return false;
            // Divide for negative prefixes: 10^9 is exact and 10^-9 is not, so 3 n parsed as 3.0000000000000004e-9.
            value = exponent >= 0 ? mantissa * Pow10(exponent) : mantissa / Pow10(-exponent);
            return true;
        }

        private static double ScaleDown(double value, int exponent)
            => exponent >= 0 ? value / Pow10(exponent) : value * Pow10(-exponent);

        private static double Pow10(int k)
        {
            double p = 1;
            for (int i = 0; i < k; i++) p *= 10; // exact for k ≤ 22
            return p;
        }

        private static string PrefixFor(int exponent)
        {
            int index = exponent / 3;
            return index >= 0 ? PositivePrefixes[index] : NegativePrefixes[-index];
        }

        private static bool TryPrefixExponent(char symbol, out int exponent)
        {
            switch (symbol)
            {
                case 'k': case 'K': exponent = 3; return true;   // accept uppercase K leniently
                case 'M': exponent = 6; return true;
                case 'G': exponent = 9; return true;
                case 'T': exponent = 12; return true;
                case 'P': exponent = 15; return true;
                case 'E': exponent = 18; return true;
                case 'Z': exponent = 21; return true;
                case 'Y': exponent = 24; return true;
                case 'm': exponent = -3; return true;
                case 'µ': case 'μ': case 'u': exponent = -6; return true;  // micro sign, Greek mu, ASCII 'u'
                case 'n': exponent = -9; return true;
                case 'p': exponent = -12; return true;
                case 'f': exponent = -15; return true;
                case 'a': exponent = -18; return true;
                case 'z': exponent = -21; return true;
                case 'y': exponent = -24; return true;
                default: exponent = 0; return false;
            }
        }

        private static double RoundToSignificant(double value, int significantDigits)
        {
            if (value == 0) return 0;
            double magnitude = Math.Ceiling(Math.Log10(Math.Abs(value)));
            double scale = Math.Pow(10, significantDigits - (int)magnitude);
            return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
        }
    }
}
