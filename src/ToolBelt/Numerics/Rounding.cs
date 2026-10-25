// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Rounding helpers beyond <see cref="Math.Round(double)"/>: rounding to an arbitrary multiple and
    /// rounding to a number of significant digits.
    /// </summary>
    public static class Rounding
    {
        /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="multiple"/> (which must be
        /// positive). Rounding up past <see cref="double.MaxValue"/> (1.7e308 to a multiple of 1e308) returns infinity.</summary>
        public static double RoundToMultiple(double value, double multiple)
        {
            if (!(multiple > 0) || double.IsInfinity(multiple))
                throw new ArgumentOutOfRangeException(nameof(multiple), multiple, "Multiple must be positive and finite.");
            double q = value / multiple;
            // The quotient overflows when the value dwarfs the multiple (1e300 to 1e-10 gave ∞); every double that
            // large is already as close to a multiple as a double can be, so return it.
            if (double.IsInfinity(q) && !double.IsInfinity(value)) return value;
            return Math.Round(q, MidpointRounding.AwayFromZero) * multiple;
        }

        /// <summary>
        /// Rounds <paramref name="value"/> to <paramref name="significantDigits"/> significant figures (halves away
        /// from zero), returning the double nearest that decimal. The digits rounded are those of the shortest decimal
        /// that round-trips the value, the way it prints: 0.15 rounds to 0.2 at one digit even though the double
        /// stored for 0.15 is slightly below it. Works across the whole double range, subnormals included; rounding up
        /// past <see cref="double.MaxValue"/> returns infinity of the same sign.
        /// </summary>
        public static double RoundToSignificantDigits(double value, int significantDigits)
        {
            if (significantDigits < 1)
                throw new ArgumentOutOfRangeException(nameof(significantDigits), significantDigits, "Significant digits must be at least 1.");
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
                return value;

            // Scaling by a power of ten rounded twice (0.15·10 = 1.4999999999999998 kept 0.1; 999.96 at 3 digits gave
            // 999.9999999999999), so the rounding is done on the decimal digits themselves and parsed back once.
            Digits(Math.Abs(value), out string digits, out int exponent);
            digits = RoundDigits(digits, significantDigits, ref exponent);
            string text = (value < 0 ? "-" : "") + digits[0] + (digits.Length > 1 ? "." + digits.Substring(1) : "")
                + "E" + exponent.ToString(CultureInfo.InvariantCulture);
            try
            {
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return value < 0 ? double.NegativeInfinity : double.PositiveInfinity; // .NET Framework throws; Core returns ∞
            }
        }

        // The significant digits of the shortest round-trip decimal for a positive finite value (no leading or trailing
        // zeros) and the decimal exponent of the first one: 123.45 → "12345", 2.
        private static void Digits(double value, out string digits, out int exponent)
        {
            string s = value.ToString("R", CultureInfo.InvariantCulture);
            if (double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
                s = value.ToString("G17", CultureInfo.InvariantCulture); // .NET Framework's "R" can lose the last digit
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
    }
}
