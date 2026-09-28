// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Sampling and distribution helpers over any <see cref="Random"/> (pair with
    /// <see cref="DeterministicRandom"/> for reproducible results): ranged doubles, in-place Fisher-Yates
    /// shuffle, sample-k-without-replacement, weighted choice, and Gaussian/exponential draws. For a
    /// cryptographically secure source use a dedicated crypto generator, not this.
    /// </summary>
    public static class RandomUtils
    {
        /// <summary>A double in [min, max).</summary>
        public static double NextDouble(this Random random, double min, double max)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (min > max) throw new ArgumentException("min must not exceed max.");
            return min + random.NextDouble() * (max - min);
        }

        /// <summary>Shuffles <paramref name="list"/> in place using the Fisher-Yates algorithm.</summary>
        public static void Shuffle<T>(this Random random, IList<T> list)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (list is null) throw new ArgumentNullException(nameof(list));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Returns <paramref name="count"/> distinct elements chosen uniformly without replacement.</summary>
        public static T[] SampleWithoutReplacement<T>(this Random random, IReadOnlyList<T> source, int count)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (count < 0 || count > source.Count)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be in [0, source length].");

            var pool = new T[source.Count];
            for (int i = 0; i < source.Count; i++) pool[i] = source[i];
            // Partial Fisher-Yates: the first `count` slots become the sample.
            for (int i = 0; i < count; i++)
            {
                int j = i + random.Next(source.Count - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var result = new T[count];
            Array.Copy(pool, result, count);
            return result;
        }

        /// <summary>Chooses an item with probability proportional to its weight.</summary>
        public static T WeightedChoice<T>(this Random random, IReadOnlyList<T> items, IReadOnlyList<double> weights)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (weights is null) throw new ArgumentNullException(nameof(weights));
            if (items.Count == 0) throw new ArgumentException("At least one item is required.", nameof(items));
            if (items.Count != weights.Count) throw new ArgumentException("items and weights must have the same length.");

            double total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] < 0) throw new ArgumentException("Weights must be non-negative.", nameof(weights));
                total += weights[i];
            }
            if (total <= 0) throw new ArgumentException("Weights must sum to a positive value.", nameof(weights));

            double target = random.NextDouble() * total;
            double cumulative = 0;
            for (int i = 0; i < items.Count; i++)
            {
                cumulative += weights[i];
                if (target < cumulative) return items[i];
            }
            return items[items.Count - 1]; // floating-point guard
        }

        /// <summary>A normally distributed draw via the Box-Muller transform.</summary>
        public static double NextGaussian(this Random random, double mean = 0, double standardDeviation = 1)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            double u1 = 1.0 - random.NextDouble(); // in (0, 1] to keep the log finite
            double u2 = random.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + standardDeviation * z;
        }

        /// <summary>An exponentially distributed draw with the given rate (lambda).</summary>
        public static double NextExponential(this Random random, double rate = 1.0)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate), rate, "Rate must be positive.");
            return -Math.Log(1.0 - random.NextDouble()) / rate;
        }
    }
}
