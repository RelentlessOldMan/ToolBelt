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

            double maxWeight = 0, r = double.NaN;
            bool allFinite = true;
            for (int i = 0; i < values.Count; i++)
            {
                double w = weights[i];
                if (!(w >= 0) || double.IsInfinity(w)) // ∞ weight gave ∞/∞ = NaN
                    throw new ArgumentException($"Weight at index {i} is invalid ({w}).", nameof(weights));
                if (w == 0) continue; // 0·∞ was NaN
                maxWeight = Math.Max(maxWeight, w);
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i])) allFinite = false;
                else if (!(Math.Abs(values[i]) >= Math.Abs(r))) r = values[i]; // the value nearest 0
            }
            if (maxWeight == 0)
                throw new ArgumentException("Weights must sum to a positive value.", nameof(weights));

            // Weights are scaled by a power of two (exact) so the largest is in [0.5, 1): 1e308-sized weights no longer
            // overflow the weight sum, and no product exceeds its value. The mean is unchanged by a common weight scale.
            int weightShift = -1 - Exponent(maxWeight);
            if (allFinite)
            {
                // About the value nearest 0: r + Σ w(v − r) / Σ w is exact for equal values ({0.1, 0.1, 0.1} averaged to
                // 0.10000000000000002) and stays within the values' range; deviations that overflow fall through. About the
                // first value, a mean far below it cancelled ({1e308, 0} weighted {1e-300, 1e300} gave 0, not 1e-292).
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity, dev = 0, total = 0;
                for (int i = 0; i < values.Count; i++)
                {
                    if (weights[i] == 0) continue;
                    double v = values[i];
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                    dev += Product(v - r, weights[i], weightShift);
                    total += ScaleB(weights[i], weightShift);
                }
                double mean = r + dev / total;
                if (!double.IsNaN(mean) && !double.IsInfinity(mean)) return Math.Max(lo, Math.Min(hi, mean));
            }
            double average = Sum(values, weights, weightShift, 0);
            // Values near double.MaxValue can still overflow the sum (1e308 + 1e308): retry with values scaled by 2⁻³².
            if (double.IsInfinity(average) && allFinite)
                average = Sum(values, weights, weightShift, -32) * 4294967296.0;
            return average;
        }

        private static double Sum(IReadOnlyList<double> values, IReadOnlyList<double> weights, int weightShift, int valueShift)
        {
            double weightedSum = 0, totalWeight = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (weights[i] == 0) continue;
                weightedSum += Product(values[i], weights[i], weightShift + valueShift);
                totalWeight += ScaleB(weights[i], weightShift);
            }
            return weightedSum / totalWeight;
        }

        // a·w·2^k for a weight w > 0: scaling w alone first flushed a weight far below the largest to 0, losing a product
        // with a huge value that can still dominate the sum (1e-300 × 1e308).
        private static double Product(double a, double w, int k)
        {
            if (a == 0 || double.IsNaN(a) || double.IsInfinity(a)) return a * w;
            int ea = Exponent(Math.Abs(a)), ew = Exponent(w);
            return ScaleB(ScaleB(a, -ea) * ScaleB(w, -ew), ea + ew + k);
        }

        // floor(log2 x) for a positive finite x, subnormals included.
        private static int Exponent(double x)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
            return e == 0 ? Exponent(x * 18446744073709551616.0) - 64 : e - 1023;
        }

        // x·2^k without overflowing the factor itself (2^k alone is out of range for |k| > 1023), rounded once. Steps of
        // 2^±1022 are exact while the result is normal; one that went subnormal and was scaled again rounded twice, so a
        // result below the normal range is reached from 2^-1022 in a single multiplication.
        private static double ScaleB(double x, int k)
        {
            if (x == 0 || double.IsNaN(x) || double.IsInfinity(x)) return x;
            long kk = k;
            if (Math.Abs(x) < 2.2250738585072014E-308) { x *= 18446744073709551616.0; kk -= 64; } // subnormal: made normal, exactly
            int e = Exponent(Math.Abs(x));
            long target = e + kk; // the result's exponent
            if (target > 1023) return x > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            if (target >= -1022) return ScaleSteps(x, kk);
            x = ScaleSteps(x, -1022 - e); // exactly ±[1, 2)·2^-1022
            long rest = target + 1022;
            return rest < -1022 ? x * 0.0 : x * Pow2((int)rest);
        }

        // Exact: x and x·2^k are normal, and so is every step between them.
        private static double ScaleSteps(double x, long k)
        {
            while (k > 1023) { x *= Pow2(1023); k -= 1023; }
            while (k < -1022) { x *= Pow2(-1022); k += 1022; }
            return x * Pow2((int)k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
    }
}
