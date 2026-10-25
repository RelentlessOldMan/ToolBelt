// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Computes a weighted arithmetic mean: <c>sum(value·weight) / sum(weight)</c>. Weights must be
    /// non-negative, finite, and sum to a positive value, and align 1:1 with the values. A value with weight 0 is
    /// left out entirely (so it may be NaN or infinite). Weights and values of any finite magnitude are handled
    /// without overflow (1e308-sized weights, or values near <see cref="double.MaxValue"/>).
    /// </summary>
    public static class WeightedAverage
    {
        public static double Compute(IReadOnlyList<double> values, IReadOnlyList<double> weights)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (weights is null) throw new ArgumentNullException(nameof(weights));
            if (values.Count != weights.Count)
                throw new ArgumentException("Values and weights must have the same length.", nameof(weights));
            if (values.Count == 0)
                throw new ArgumentException("At least one value is required.", nameof(values));

            double maxWeight = 0;
            bool allFinite = true;
            for (int i = 0; i < values.Count; i++)
            {
                double w = weights[i];
                if (!(w >= 0) || double.IsInfinity(w)) // ∞ weight gave ∞/∞ = NaN
                    throw new ArgumentException($"Weight at index {i} is invalid ({w}).", nameof(weights));
                if (w == 0) continue; // 0·∞ was NaN
                maxWeight = Math.Max(maxWeight, w);
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i])) allFinite = false;
            }
            if (maxWeight == 0)
                throw new ArgumentException("Weights must sum to a positive value.", nameof(weights));

            // Weights are scaled by a power of two (exact) so the largest is in [0.5, 1): 1e308-sized weights no longer
            // overflow the weight sum, and no product exceeds its value. The mean is unchanged by a common weight scale.
            int weightShift = -1 - Exponent(maxWeight);
            double average = Sum(values, weights, weightShift, 1.0);
            // Values near double.MaxValue can still overflow the sum (1e308 + 1e308): retry with values scaled by 2⁻³².
            if (double.IsInfinity(average) && allFinite)
                average = Sum(values, weights, weightShift, 1.0 / 4294967296.0) * 4294967296.0;
            return average;
        }

        private static double Sum(IReadOnlyList<double> values, IReadOnlyList<double> weights, int weightShift, double valueScale)
        {
            double weightedSum = 0, totalWeight = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (weights[i] == 0) continue;
                double w = ScaleB(weights[i], weightShift);
                weightedSum += values[i] * valueScale * w;
                totalWeight += w;
            }
            return weightedSum / totalWeight;
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
