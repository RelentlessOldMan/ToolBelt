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
        /// Chooses an index in proportion to <paramref name="weights"/>. Weights must be finite, non-negative
        /// and not all zero. A zero-weight index is never chosen.
        /// </summary>
        public static int WeightedPickIndex(IReadOnlyList<double> weights, Random random)
        {
            if (weights is null) throw new ArgumentNullException(nameof(weights));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (weights.Count == 0) throw new ArgumentException("Weights must not be empty.", nameof(weights));

            double max = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                double w = weights[i];
                if (!(w >= 0) || double.IsInfinity(w))
                    throw new ArgumentException($"Weight at index {i} is invalid ({w}).", nameof(weights));
                if (w > max) max = w;
            }
            if (max == 0)
                throw new ArgumentException("Weights must sum to a positive value.", nameof(weights));

            // Scale by the largest weight so the total (at most Count) cannot overflow to infinity.
            double total = 0;
            int last = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                total += weights[i] / max;
                if (weights[i] > 0) last = i;
            }

            double target = random.NextDouble() * total;
            double cumulative = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                cumulative += weights[i] / max;
                if (target < cumulative && weights[i] > 0)
                    return i;
            }
            return last; // floating-point guard: fall through to the last item with a positive weight
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
