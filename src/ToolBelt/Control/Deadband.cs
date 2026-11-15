// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Control
{
    /// <summary>
    /// Deadband shaping: treat small deviations from a centre as exactly zero, so a controller or joystick doesn't react
    /// to noise around its rest point.
    /// </summary>
    public static class Deadband
    {
        /// <summary>
        /// Returns <paramref name="center"/> when |value − center| ≤ <paramref name="halfWidth"/>. Outside the band a
        /// <paramref name="continuous"/> deadband moves the value toward the centre by the half-width, so the output leaves
        /// the centre smoothly (no jump at the band edge); otherwise the value passes through unchanged. NaN gives NaN.
        /// </summary>
        public static double Apply(double value, double halfWidth, double center = 0, bool continuous = true)
        {
            if (double.IsNaN(center) || double.IsInfinity(center)) throw new ArgumentOutOfRangeException(nameof(center), center, "Centre must be finite.");
            if (!(halfWidth >= 0) || double.IsInfinity(halfWidth)) throw new ArgumentOutOfRangeException(nameof(halfWidth), halfWidth, "Half-width must be non-negative and finite.");
            double offset = value - center;
            if (double.IsNaN(offset)) return double.NaN;
            if (Within(value, center, offset, halfWidth)) return center;
            if (!continuous) return value;
            // Shift the value itself: center + offset overflowed when offset did (1.5e308 about −1.5e308) and cancelled
            // when the centre dwarfed the value (3 about 1e17 gave 1, not 4). Rounding can't carry it past the centre.
            return offset > 0 ? Math.Max(center, value - halfWidth) : Math.Min(center, value + halfWidth);
        }

        // |value − center| ≤ limit for the exact difference. The rounded one keeps the order, so only a tie with the limit
        // needs the rounding error (TwoSum) to tell which side the true value is on: −1 about 1e17 rounded to a distance of
        // exactly 1e17, inside a half-width of 1e17.
        internal static bool Within(double value, double center, double offset, double limit)
        {
            double size = Math.Abs(offset);
            if (size != limit) return size < limit;
            if (double.IsInfinity(offset)) return false;
            double error = TwoSumError(value, -center, offset);
            return offset > 0 ? error <= 0 : error >= 0;
        }

        // The rounding error of s = a + b (Knuth's TwoSum): a + b = s + error exactly.
        private static double TwoSumError(double a, double b, double s)
        {
            double bv = s - a, av = s - bv;
            return (a - av) + (b - bv);
        }
    }

    /// <summary>
    /// Report-by-exception filter (the SCADA/historian "deadband"): the output holds the last reported value until the
    /// input moves more than <see cref="Threshold"/> away from it, then jumps to the input. Cuts the update rate of a
    /// slowly varying reading without hiding real changes. Not thread-safe.
    /// </summary>
    public sealed class DeadbandFilter
    {
        public DeadbandFilter(double threshold)
        {
            if (!(threshold >= 0) || double.IsInfinity(threshold)) throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Threshold must be non-negative and finite.");
            Threshold = threshold;
        }

        public double Threshold { get; }

        /// <summary>The last reported value.</summary>
        public double Value { get; private set; }

        public bool HasValue { get; private set; }

        /// <summary>Feeds a reading; returns true (and updates <see cref="Value"/>) when it is reported. The first reading always is; NaN never is.</summary>
        public bool Update(double input)
        {
            if (double.IsNaN(input)) return false;
            // == : ∞ − ∞ is NaN. Within: a change of 1e17 + 1 rounded to exactly the threshold 1e17 and was missed.
            if (HasValue && (input == Value || Deadband.Within(input, Value, input - Value, Threshold))) return false;
            Value = input;
            HasValue = true;
            return true;
        }

        public void Reset()
        {
            Value = 0;
            HasValue = false;
        }
    }
}
