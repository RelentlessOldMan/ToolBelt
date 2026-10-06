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
        /// the centre smoothly (no jump at the band edge); otherwise the value passes through unchanged.
        /// </summary>
        public static double Apply(double value, double halfWidth, double center = 0, bool continuous = true)
        {
            if (!(halfWidth >= 0) || double.IsInfinity(halfWidth)) throw new ArgumentOutOfRangeException(nameof(halfWidth), halfWidth, "Half-width must be non-negative and finite.");
            double offset = value - center;
            if (Math.Abs(offset) <= halfWidth) return center;
            return continuous ? center + offset - Math.Sign(offset) * halfWidth : value;
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
            if (HasValue && Math.Abs(input - Value) <= Threshold) return false;
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
