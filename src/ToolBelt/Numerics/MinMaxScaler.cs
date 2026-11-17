// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Min-max feature scaling: linearly rescales values so the data's minimum maps to a target low and
    /// its maximum to a target high. Constant data (min == max) maps entirely to the target low, avoiding
    /// a divide-by-zero. Unlike <see cref="Interpolation.Remap"/>, the input range is derived from the
    /// data itself. Data and the target bounds must be finite; ranges as wide as ±<see cref="double.MaxValue"/> scale without overflow.
    /// </summary>
    public static class MinMaxScaler
    {
        /// <summary>Scales <paramref name="data"/> into [0, 1].</summary>
        public static double[] ToUnit(IReadOnlyList<double> data) => ToRange(data, 0.0, 1.0);

        /// <summary>Scales <paramref name="data"/> into [<paramref name="newMin"/>, <paramref name="newMax"/>].</summary>
        public static double[] ToRange(IReadOnlyList<double> data, double newMin, double newMax)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count == 0) throw new ArgumentException("Data must not be empty.", nameof(data));
            CheckBounds(newMin, newMax);

            double dataMin = data[0], dataMax = data[0];
            for (int i = 0; i < data.Count; i++)
            {
                // NaN never compared, so it slipped past min/max and came out as NaN (or set the range if first).
                if (double.IsNaN(data[i]) || double.IsInfinity(data[i]))
                    throw new ArgumentException($"Value [{i}] is not finite.", nameof(data));
                if (data[i] < dataMin) dataMin = data[i];
                if (data[i] > dataMax) dataMax = data[i];
            }

            var result = new double[data.Count];
            for (int i = 0; i < data.Count; i++)
                result[i] = Scale(data[i], dataMin, dataMax, newMin, newMax);
            return result;
        }

        /// <summary>Scales one value from [<paramref name="dataMin"/>, <paramref name="dataMax"/>] to the target range.</summary>
        public static double Scale(double value, double dataMin, double dataMax, double newMin, double newMax)
        {
            CheckBounds(newMin, newMax);
            // A NaN data bound returned NaN for every value, where a NaN target bound throws.
            if (double.IsNaN(dataMin)) throw new ArgumentOutOfRangeException(nameof(dataMin), dataMin, "Data bound is NaN.");
            if (double.IsNaN(dataMax)) throw new ArgumentOutOfRangeException(nameof(dataMax), dataMax, "Data bound is NaN.");
            if (dataMax == dataMin)
                return newMin; // constant input: no spread to preserve
            double num = value - dataMin, den = dataMax - dataMin;
            // −1e308..1e308 overflows to ∞/∞ = NaN: halve then. Only then, since halving a subnormal range ([0, ε]) loses it.
            double t = double.IsInfinity(num) || double.IsInfinity(den)
                ? (0.5 * value - 0.5 * dataMin) / (0.5 * dataMax - 0.5 * dataMin)
                : num / den;
            // The ends land exactly on the bounds, and values inside the data range inside the target range: newMin + 1·span
            // does not round back to newMax ([0.6, 1.61] mapped the maximum to 1.6100000000000003).
            if (value == dataMax) return newMax;
            if (value == dataMin) return newMin;
            double span = newMax - newMin;
            double r = double.IsInfinity(span) ? (1 - t) * newMin + t * newMax : newMin + t * span;
            if (t >= 0 && t <= 1) r = Math.Min(Math.Max(r, Math.Min(newMin, newMax)), Math.Max(newMin, newMax));
            return r;
        }

        // An infinite or NaN bound gave NaN for the end it should hit exactly (0 to ∞ mapped the minimum to NaN).
        private static void CheckBounds(double newMin, double newMax)
        {
            if (double.IsNaN(newMin) || double.IsInfinity(newMin))
                throw new ArgumentOutOfRangeException(nameof(newMin), newMin, "Target bound must be finite.");
            if (double.IsNaN(newMax) || double.IsInfinity(newMax))
                throw new ArgumentOutOfRangeException(nameof(newMax), newMax, "Target bound must be finite.");
        }
    }
}
