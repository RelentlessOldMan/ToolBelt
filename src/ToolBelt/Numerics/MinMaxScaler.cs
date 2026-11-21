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
            // t out of the normal range where the result is not: a data span of 1e-310 overflowed it to ∞ (1 of [0, 1e-310]
            // onto [0, 1e-300] for 1e10), and one of 1e300 underflowed it. Then the product is formed with its exponents apart.
            if ((double.IsInfinity(t) || Math.Abs(t) < 2.2250738585072014E-308)
                && IsFinite(value) && IsFinite(dataMin) && IsFinite(dataMax))
            {
                double s = newMin + Scaled(value, dataMin, dataMax, newMin, newMax);
                if (t > 0 && t < 1) s = Math.Min(Math.Max(s, Math.Min(newMin, newMax)), Math.Max(newMin, newMax));
                return s;
            }
            double span = newMax - newMin;
            double r = double.IsInfinity(span) ? (1 - t) * newMin + t * newMax : newMin + t * span;
            if (t >= 0 && t <= 1) r = Math.Min(Math.Max(r, Math.Min(newMin, newMax)), Math.Max(newMin, newMax));
            return r;
        }

        // (value − lo)·(outMax − outMin)/(hi − lo), all finite, with a difference that overflows halved and the
        // halving undone in the exponent.
        private static double Scaled(double value, double lo, double hi, double outMin, double outMax)
        {
            double num = value - lo, den = hi - lo, span = outMax - outMin;
            int k = 0;
            if (double.IsInfinity(num)) { num = 0.5 * value - 0.5 * lo; k++; }
            if (double.IsInfinity(den)) { den = 0.5 * hi - 0.5 * lo; k--; }
            if (double.IsInfinity(span)) { span = 0.5 * outMax - 0.5 * outMin; k++; }
            return MulDiv(num, span, den, k);
        }

        // x·y/z·2^k with the binary exponents kept apart: no step over- or underflows unless the result does.
        private static double MulDiv(double x, double y, double z, int k)
        {
            if (x == 0 || y == 0) return x * y / z;
            int ex = Exponent(x), ey = Exponent(y), ez = Exponent(z);
            return ScaleB(ScaleB(x, -ex) * ScaleB(y, -ey) / ScaleB(z, -ez), ex + ey - ez + k);
        }

        // floor(log2 |x|) for a finite x other than 0, subnormals included.
        private static int Exponent(double x)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
            return e == 0 ? Exponent(x * 18446744073709551616.0) - 64 : e - 1023;
        }

        // x·2^k, rounded at most once: every step but the last stays in the normal range.
        private static double ScaleB(double x, int k)
        {
            for (; k > 1023; k -= 1023) x *= Pow2(1023);
            for (; k < -1022; k += 960) x *= Pow2(-960);
            return x * Pow2(k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        private static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

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
