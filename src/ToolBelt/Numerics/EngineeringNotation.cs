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
            if (significantDigits > 17) significantDigits = 17; // all a double holds

            // Rounded on the decimal digits, not by scaling with powers of ten: scaling rounded twice, so 999.9999999999999
            // at 17 digits printed "1 k" and full-precision output did not round-trip.
            Digits(Math.Abs(value), out string digits, out int decimalExponent);
            digits = RoundDigits(digits, significantDigits, ref decimalExponent);
            int exponent = FloorDiv(decimalExponent, 3) * 3;
            // Clamping to yocto/yotta printed "1E-06 y" and "1230000 Y"; out of range, plain E notation reads better.
            if (exponent > MaxExponent || exponent < -MaxExponent)
                return value.ToString("G" + significantDigits, CultureInfo.InvariantCulture);

            // 1 to 3 digits before the point; fixed point always ("G" turned 120 at 2 digits into "1.2E+02").
            int integerDigits = decimalExponent - exponent + 1;
            string number = digits.Length <= integerDigits
                ? digits + new string('0', integerDigits - digits.Length)
                : digits.Substring(0, integerDigits) + "." + digits.Substring(integerDigits);
            if (value < 0) number = "-" + number;
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
            // A plain number first, so Format's "NaN" / "Infinity" round-trip (the 'y' of "Infinity" is yocto). A finite
            // text that overflows ("1e400") is not a value; .NET Core parses it as infinity.
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                if (!double.IsInfinity(value) || s.IndexOf("Infinity", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                value = 0; // a failed parse reports 0, not the ∞ it overflowed to
                return false;
            }
            char last = s[s.Length - 1];
            int exponent;
            if (char.IsLetter(last) || last == 'µ' || last == 'μ')
            {
                if (!TryPrefixExponent(last, out exponent)) return false;
                s = s.Substring(0, s.Length - 1).Trim();
            }
            else
            {
                return false; // not a plain number and no prefix
            }

            // The prefix joins the mantissa's own exponent and the decimal is parsed once, correctly rounded: multiplying
            // by 10^k rounded twice, and 1e306 k overflowed to infinity instead of failing.
            long total = exponent;
            int ePos = s.IndexOfAny(new[] { 'E', 'e' });
            if (ePos >= 0)
            {
                if (!long.TryParse(s.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long own))
                    return false;
                total += Math.Max(-100000, Math.Min(100000, own));
                s = s.Substring(0, ePos);
            }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double mantissa)
                || double.IsNaN(mantissa) || double.IsInfinity(mantissa))
                return false;
            string text2 = s.Trim() + "E" + total.ToString(CultureInfo.InvariantCulture);
            double parsed;
            try
            {
                if (!double.TryParse(text2, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            }
            catch (OverflowException) { return false; }
            if (double.IsInfinity(parsed)) return false;
            value = parsed;
            return true;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        // The significant digits of the shortest round-trip decimal for a positive finite value (no leading or trailing
        // zeros) and the decimal exponent of the first one: 123.45 → "12345", 2.
        private static void Digits(double value, out string digits, out int exponent)
        {
            string s = value.ToString("R", CultureInfo.InvariantCulture);
            if (double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
                for (int p = 16; p <= 17; p++) // .NET Framework's "R" can lose the last digit; G17 alone was not the shortest
                {
                    s = value.ToString("G" + p, CultureInfo.InvariantCulture);
                    if (double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) == value) break;
                }
            int exp = 0, ePos = s.IndexOfAny(new[] { 'E', 'e' });
            if (ePos >= 0)
            {
                exp = int.Parse(s.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                s = s.Substring(0, ePos);
            }
            int dot = s.IndexOf('.');
            string whole = dot < 0 ? s : s.Substring(0, dot);
            string all = whole + (dot < 0 ? "" : s.Substring(dot + 1));
            int lead = 0;
            while (lead < all.Length && all[lead] == '0') lead++;
            digits = all.Substring(lead).TrimEnd('0');
            exponent = whole.Length + exp - lead - 1;
        }

        // Keeps the first `count` digits, rounding half away from zero; a carry out of all nines becomes "1" one decade up.
        private static string RoundDigits(string digits, int count, ref int exponent)
        {
            if (digits.Length <= count) return digits;
            char[] kept = digits.Substring(0, count).ToCharArray();
            if (digits[count] >= '5')
            {
                int i = count - 1;
                while (i >= 0 && kept[i] == '9') kept[i--] = '0';
                if (i < 0) { exponent++; return "1"; }
                kept[i]++;
            }
            return new string(kept).TrimEnd('0');
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
    }
}
