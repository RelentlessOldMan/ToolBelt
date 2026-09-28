// ToolBelt drop-in — fully self-contained (BCL only).
using System;

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
            if (double.IsNaN(multiple) || multiple <= 0)
                throw new ArgumentOutOfRangeException(nameof(multiple), multiple, "Multiple must be positive.");
            return Math.Round(value / multiple, MidpointRounding.AwayFromZero) * multiple;
        }

        /// <summary>Rounds <paramref name="value"/> to <paramref name="significantDigits"/> significant figures.</summary>
        public static double RoundToSignificantDigits(double value, int significantDigits)
        {
            if (significantDigits < 1)
                throw new ArgumentOutOfRangeException(nameof(significantDigits), significantDigits, "Significant digits must be at least 1.");
            if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
                return value;

            // Scale so the requested number of significant digits sits above the decimal point, round, unscale.
            double scale = Math.Pow(10, significantDigits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value))));
            return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
        }
    }
}
