// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Formats a value with an SI prefix (yocto through yotta) at a chosen significant-figure count, and
    /// parses the same form back. Appears in every plot axis label, report table and console readout, and
    /// is otherwise re-derived badly. Round-trips to the chosen precision.
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

            int exponent = (int)(Math.Floor(Math.Log10(Math.Abs(value)) / 3.0) * 3);
            if (exponent > MaxExponent) exponent = MaxExponent;
            if (exponent < -MaxExponent) exponent = -MaxExponent;

            double scaled = value / Math.Pow(10, exponent);
            // Rounding can push e.g. 999.95 up to 1000; renormalize one step if so.
            double rounded = RoundToSignificant(scaled, significantDigits);
            if (Math.Abs(rounded) >= 1000 && exponent < MaxExponent)
            {
                exponent += 3;
                scaled = value / Math.Pow(10, exponent);
                rounded = RoundToSignificant(scaled, significantDigits);
            }

            string number = rounded.ToString("G" + significantDigits, CultureInfo.InvariantCulture);
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
            char last = s[s.Length - 1];
            int exponent;
            if (char.IsLetter(last) || last == 'µ')
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
            value = mantissa * Math.Pow(10, exponent);
            return true;
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
                case 'µ': case 'u': exponent = -6; return true;  // accept ASCII 'u' for micro
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
