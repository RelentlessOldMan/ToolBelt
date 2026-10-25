// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Computes a weighted arithmetic mean: <c>sum(value·weight) / sum(weight)</c>. Weights must be
    /// non-negative, finite, and sum to a positive value, and align 1:1 with the values. A value with weight 0 is
    /// left out entirely (so it may be NaN or infinite).
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

            double weightedSum = 0;
            double totalWeight = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double w = weights[i];
                if (!(w >= 0) || double.IsInfinity(w)) // ∞ weight gave ∞/∞ = NaN
                    throw new ArgumentException($"Weight at index {i} is invalid ({w}).", nameof(weights));
                if (w == 0) continue; // 0·∞ was NaN
                weightedSum += values[i] * w;
                totalWeight += w;
            }

            if (totalWeight <= 0)
                throw new ArgumentException("Weights must sum to a positive value.", nameof(weights));

            return weightedSum / totalWeight;
        }
    }
}
