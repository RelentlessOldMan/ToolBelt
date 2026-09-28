// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Randomized sampling helpers over collections. The randomness source is always passed in as a
    /// <see cref="Random"/>, so callers control seeding and tests are deterministic.
    /// </summary>
    public static class Sampling
    {
        /// <summary>Shuffles <paramref name="list"/> in place using an unbiased Fisher–Yates shuffle.</summary>
        public static void Shuffle<T>(IList<T> list, Random random)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            if (random is null) throw new ArgumentNullException(nameof(random));

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1); // 0..i inclusive
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Chooses an index in proportion to <paramref name="weights"/>. Weights must be non-negative and
        /// sum to a positive value.
        /// </summary>
        public static int WeightedPickIndex(IReadOnlyList<double> weights, Random random)
        {
            if (weights is null) throw new ArgumentNullException(nameof(weights));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (weights.Count == 0) throw new ArgumentException("Weights must not be empty.", nameof(weights));

            double total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                double w = weights[i];
                if (w < 0 || double.IsNaN(w))
                    throw new ArgumentException($"Weight at index {i} is invalid ({w}).", nameof(weights));
                total += w;
            }
            if (total <= 0)
                throw new ArgumentException("Weights must sum to a positive value.", nameof(weights));

            double target = random.NextDouble() * total;
            double cumulative = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i];
                if (target < cumulative)
                    return i;
            }
            return weights.Count - 1; // floating-point guard: fall through to the last item
        }

        /// <summary>Picks an item in proportion to its weight. <paramref name="items"/> and <paramref name="weights"/> must align.</summary>
        public static T WeightedPick<T>(IReadOnlyList<T> items, IReadOnlyList<double> weights, Random random)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (weights is null) throw new ArgumentNullException(nameof(weights));
            if (items.Count != weights.Count)
                throw new ArgumentException("Items and weights must have the same length.", nameof(weights));
            return items[WeightedPickIndex(weights, random)];
        }
    }
}
