// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Standard-score (z-score) helpers: expresses a value as the number of standard deviations it lies
    /// from the mean. When the standard deviation is zero (all values equal), every score is defined as
    /// zero rather than NaN.
    /// </summary>
    public static class ZScore
    {
        /// <summary>The z-score of a single value given a mean and standard deviation (which must not be negative or NaN).</summary>
        public static double Standardize(double value, double mean, double stdDev)
        {
            if (!(stdDev >= 0))
                throw new ArgumentOutOfRangeException(nameof(stdDev), stdDev, "Standard deviation must not be negative or NaN.");
            if (stdDev == 0) return 0.0;
            double diff = value - mean;
            // Finite operands of opposite sign can overflow the difference (1e308 − (−1e308)): halve them first.
            if (double.IsInfinity(diff) && !double.IsInfinity(value) && !double.IsInfinity(mean))
                return (0.5 * value - 0.5 * mean) / stdDev * 2;
            return diff / stdDev;
        }

        /// <summary>
        /// Standardizes a whole data set: computes the mean and standard deviation (population by default,
        /// sample when <paramref name="sample"/> is true) and returns each value's z-score.
        /// </summary>
        public static double[] Standardize(IReadOnlyList<double> data, bool sample = false)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count == 0) throw new ArgumentException("Data must not be empty.", nameof(data));
            if (sample && data.Count < 2)
                throw new ArgumentException("Sample standardization needs at least two values.", nameof(data));

            // z-scores do not depend on the data's scale, so finite data is first scaled by a power of two (exact) to put
            // the largest magnitude in [1, 2): the sums and squares below then neither overflow (±1e308) nor underflow
            // (±1e-200, whose squares were 0, so every score came out 0).
            double maxAbs = 0;
            bool finite = true;
            for (int i = 0; i < data.Count; i++)
            {
                double v = data[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) finite = false;
                else maxAbs = Math.Max(maxAbs, Math.Abs(v));
            }
            int shift = finite && maxAbs > 0 ? -Exponent(maxAbs) : 0;
            var x = new double[data.Count];
            for (int i = 0; i < x.Length; i++) x[i] = ScaleB(data[i], shift);

            // Mean about the first value: all-equal data gives exactly that mean and a zero spread (summing
            // first left 0.1 ×3 an ulp off, so every score came out −1 instead of 0).
            double shiftedSum = 0;
            for (int i = 0; i < x.Length; i++)
                shiftedSum += x[i] - x[0];
            double mean = x[0] + shiftedSum / x.Length;

            double sumSq = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double d = x[i] - mean;
                sumSq += d * d;
            }
            double variance = sumSq / (sample ? x.Length - 1 : x.Length);
            double stdDev = Math.Sqrt(variance);

            for (int i = 0; i < x.Length; i++)
                x[i] = stdDev == 0 ? 0.0 : (x[i] - mean) / stdDev;
            return x;
        }

        // floor(log2 x) for a positive finite x, subnormals included.
        private static int Exponent(double x)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
            return e == 0 ? Exponent(x * 18446744073709551616.0) - 64 : e - 1023;
        }

        // x·2^k without overflowing the factor itself (2^k alone is out of range for |k| > 1023).
        private static double ScaleB(double x, int k)
        {
            while (k > 1023) { x *= Pow2(1023); k -= 1023; }
            while (k < -1022) { x *= Pow2(-1022); k += 1022; }
            return x * Pow2(k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
    }
}
