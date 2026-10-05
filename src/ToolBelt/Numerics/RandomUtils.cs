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

        /// <summary>
        /// A Poisson-distributed count with mean <paramref name="lambda"/>: Knuth's multiplication method below 30, and
        /// Hörmann's transformed rejection with squeeze (PTRS) above — exact in distribution and O(1) for any mean.
        /// </summary>
        public static int NextPoisson(this Random random, double lambda)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (!(lambda >= 0) || double.IsInfinity(lambda)) throw new ArgumentOutOfRangeException(nameof(lambda), lambda, "Mean must be non-negative and finite.");
            if (lambda == 0) return 0;
            if (lambda < 30)
            {
                double limit = Math.Exp(-lambda), product = random.NextDouble();
                int k = 0;
                while (product > limit) { k++; product *= random.NextDouble(); }
                return k;
            }
            // PTRS (Hörmann 1993).
            double slam = Math.Sqrt(lambda), logLam = Math.Log(lambda);
            double b = 0.931 + 2.53 * slam, a = -0.059 + 0.02483 * b;
            double invAlpha = 1.1239 + 1.1328 / (b - 3.4), vr = 0.9277 - 3.6224 / (b - 2);
            while (true)
            {
                double u = random.NextDouble() - 0.5, v = random.NextDouble();
                double us = 0.5 - Math.Abs(u);
                double kd = Math.Floor((2 * a / us + b) * u + lambda + 0.43);
                if (us >= 0.07 && v <= vr) return (int)kd;
                if (kd < 0 || (us < 0.013 && v > us)) continue;
                if (Math.Log(v) + Math.Log(invAlpha) - Math.Log(a / (us * us) + b) <= -lambda + kd * logLam - LnGamma(kd + 1))
                    return (int)kd;
            }
        }

        /// <summary>
        /// A gamma-distributed draw with the given <paramref name="shape"/> (k) and <paramref name="scale"/> (θ), mean kθ —
        /// Marsaglia &amp; Tsang's method (with the standard boost for shape &lt; 1).
        /// </summary>
        public static double NextGamma(this Random random, double shape, double scale = 1.0)
        {
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (!(shape > 0) || double.IsInfinity(shape)) throw new ArgumentOutOfRangeException(nameof(shape), shape, "Shape must be positive.");
            if (!(scale > 0) || double.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be positive.");
            if (shape < 1)
            {
                // Gamma(k) = Gamma(k + 1) · U^(1/k)
                double u = 1.0 - random.NextDouble();
                return random.NextGamma(shape + 1, scale) * Math.Pow(u, 1 / shape);
            }
            double d = shape - 1.0 / 3, c = 1 / Math.Sqrt(9 * d);
            while (true)
            {
                double x, v;
                do { x = random.NextGaussian(); v = 1 + c * x; } while (v <= 0);
                v = v * v * v;
                double u = 1.0 - random.NextDouble();
                if (u < 1 - 0.0331 * x * x * x * x) return d * v * scale;
                if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v))) return d * v * scale;
            }
        }

        private static double LnGamma(double x)
        {
            // Lanczos (g = 7, n = 9); x >= 1 here.
            double[] g = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
                           12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            double a = g[0], t = x + 6.5, xm1 = x - 1;
            for (int i = 1; i < g.Length; i++) a += g[i] / (xm1 + i);
            return 0.91893853320467274178 + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }
    }
}
