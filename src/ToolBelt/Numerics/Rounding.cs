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
        /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="multiple"/> (which must be positive).</summary>
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
        /// from zero), returning the double nearest that decimal. Works across the whole double range, subnormals
        /// included; 17 or more digits returns the value unchanged (a double never needs more).
        /// </summary>
        public static double RoundToSignificantDigits(double value, int significantDigits)
        {
            if (significantDigits < 1)
                throw new ArgumentOutOfRangeException(nameof(significantDigits), significantDigits, "Significant digits must be at least 1.");
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value) || significantDigits >= 17)
                return value;

            // Scale so the requested digits sit above the decimal point: |m| in [10^(d−1), 10^d). log10 can land
            // on the wrong side of a power of ten, so the exponent is corrected against exact powers.
            double lowBound = Pow10(significantDigits - 1), highBound = Pow10(significantDigits);
            int k = significantDigits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value)));
            double m = Scale(value, k);
            if (Math.Abs(m) >= highBound) m = Scale(value, --k);
            else if (Math.Abs(m) < lowBound) m = Scale(value, ++k);

            // The result is the integer r times 10^−k; parsing that decimal gives the correctly rounded double
            // (10^k is inexact past 10^22, and multiplying by it overflowed to NaN for subnormal inputs).
            long r = (long)Math.Round(m, MidpointRounding.AwayFromZero);
            return double.Parse(r.ToString(CultureInfo.InvariantCulture) + "E" + (-k).ToString(CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // value · 10^k without overflowing an intermediate power: exact powers up to 10^22, in steps beyond.
        private static double Scale(double value, int k)
        {
            while (k > 22) { value *= 1e22; k -= 22; }
            while (k < -22) { value /= 1e22; k += 22; }
            return k >= 0 ? value * Pow10(k) : value / Pow10(-k);
        }

        private static double Pow10(int k)
        {
            double p = 1;
            for (int i = 0; i < k; i++) p *= 10; // exact for k ≤ 22
            return p;
        }
    }
}
