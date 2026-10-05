// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>Which departure from the null hypothesis a test looks for.</summary>
    public enum Alternative
    {
        /// <summary>Either direction: |statistic| at least as large as observed.</summary>
        TwoSided,
        /// <summary>Statistic at least as large as observed.</summary>
        Greater,
        /// <summary>Statistic at most as large as observed.</summary>
        Less,
    }

    /// <summary>The outcome of a permutation test.</summary>
    public sealed class PermutationResult
    {
        internal PermutationResult(double observed, double pValue, int permutations, bool exact, double[] nullDistribution)
        {
            Observed = observed;
            PValue = pValue;
            Permutations = permutations;
            IsExact = exact;
            NullDistribution = nullDistribution;
        }

        /// <summary>The statistic on the data as observed.</summary>
        public double Observed { get; }

        public double PValue { get; }

        /// <summary>Rearrangements evaluated (all of them when <see cref="IsExact"/>).</summary>
        public int Permutations { get; }

        /// <summary>True when every rearrangement was enumerated, so the p-value is exact.</summary>
        public bool IsExact { get; }

        /// <summary>The statistic under each rearrangement — the null distribution, for plotting or further use.</summary>
        public IReadOnlyList<double> NullDistribution { get; }

        public bool IsSignificant(double alpha = 0.05)
        {
            if (!(alpha > 0 && alpha < 1)) throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "Alpha must be in (0, 1).");
            return PValue < alpha;
        }
    }

    /// <summary>
    /// Permutation (randomisation) tests: distribution-free significance tests for any statistic. Under the null
    /// hypothesis the group labels (two-sample) or the signs of the differences (paired) are exchangeable, so the
    /// observed statistic is compared with its distribution over rearrangements. When the number of rearrangements is
    /// at most the requested budget they are all enumerated and the p-value is exact; otherwise a seeded Monte Carlo
    /// sample is drawn and the p-value is (b + 1)/(m + 1), which is never 0 and keeps the test valid. Pair with a seeded
    /// generator for reproducible results.
    /// </summary>
    public static class PermutationTest
    {
        /// <summary>
        /// Two-sample test. The statistic defaults to mean(a) − mean(b); pass any function of the two groups (difference in
        /// medians, ratio of variances, ...).
        /// </summary>
        public static PermutationResult TwoSample(
            IReadOnlyList<double> a, IReadOnlyList<double> b, Random random, int permutations = 10000,
            Func<IReadOnlyList<double>, IReadOnlyList<double>, double>? statistic = null, Alternative alternative = Alternative.TwoSided)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (a.Count == 0 || b.Count == 0) throw new ArgumentException("Both samples must be non-empty.");
            if (permutations < 1) throw new ArgumentOutOfRangeException(nameof(permutations), permutations, "Must be positive.");
            statistic ??= MeanDifference;

            int na = a.Count, n = na + b.Count;
            var pooled = new double[n];
            for (int i = 0; i < na; i++) pooled[i] = a[i];
            for (int i = 0; i < b.Count; i++) pooled[na + i] = b[i];
            double observed = statistic(a, b);

            var ga = new double[na];
            var gb = new double[n - na];
            var nulls = new List<double>();
            double total = Binomial(n, na);
            bool exact = total <= permutations;
            if (exact)
            {
                // Enumerate every split of the pooled sample into groups of the original sizes.
                var idx = new int[na];
                for (int i = 0; i < na; i++) idx[i] = i;
                while (true)
                {
                    Split(pooled, idx, ga, gb);
                    nulls.Add(statistic(ga, gb));
                    if (!NextCombination(idx, n)) break;
                }
            }
            else
            {
                var work = (double[])pooled.Clone();
                for (int r = 0; r < permutations; r++)
                {
                    // Partial Fisher–Yates: the first na positions become a random group A.
                    for (int i = 0; i < na; i++)
                    {
                        int j = i + random.Next(n - i);
                        (work[i], work[j]) = (work[j], work[i]);
                    }
                    Array.Copy(work, 0, ga, 0, na);
                    Array.Copy(work, na, gb, 0, n - na);
                    nulls.Add(statistic(ga, gb));
                }
            }
            return Result(observed, nulls, exact, alternative);
        }

        /// <summary>
        /// Paired test on the differences first − second: under the null each difference is equally likely to have either
        /// sign. The statistic is the mean difference.
        /// </summary>
        public static PermutationResult Paired(
            IReadOnlyList<double> first, IReadOnlyList<double> second, Random random, int permutations = 10000,
            Alternative alternative = Alternative.TwoSided)
        {
            if (first is null) throw new ArgumentNullException(nameof(first));
            if (second is null) throw new ArgumentNullException(nameof(second));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (first.Count != second.Count) throw new ArgumentException("Paired samples must have the same length.");
            if (first.Count == 0) throw new ArgumentException("Samples must be non-empty.");
            if (permutations < 1) throw new ArgumentOutOfRangeException(nameof(permutations), permutations, "Must be positive.");

            int n = first.Count;
            var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = first[i] - second[i];
            double observed = Mean(d);
            var nulls = new List<double>();
            bool exact = n < 31 && (1L << n) <= permutations;
            if (exact)
            {
                for (long mask = 0; mask < (1L << n); mask++)
                {
                    double sum = 0;
                    for (int i = 0; i < n; i++) sum += ((mask >> i) & 1) == 0 ? d[i] : -d[i];
                    nulls.Add(sum / n);
                }
            }
            else
            {
                for (int r = 0; r < permutations; r++)
                {
                    double sum = 0;
                    for (int i = 0; i < n; i++) sum += random.Next(2) == 0 ? d[i] : -d[i];
                    nulls.Add(sum / n);
                }
            }
            return Result(observed, nulls, exact, alternative);
        }

        /// <summary>mean(a) − mean(b), the default two-sample statistic.</summary>
        public static double MeanDifference(IReadOnlyList<double> a, IReadOnlyList<double> b) => Mean(a) - Mean(b);

        private static PermutationResult Result(double observed, List<double> nulls, bool exact, Alternative alternative)
        {
            // A small tolerance so rearrangements equal to the observed value up to rounding count as "as extreme".
            double tol = 1e-12 * Math.Max(1, Math.Abs(observed));
            int extreme = 0;
            foreach (double s in nulls)
            {
                bool hit = alternative switch
                {
                    Alternative.Greater => s >= observed - tol,
                    Alternative.Less => s <= observed + tol,
                    _ => Math.Abs(s) >= Math.Abs(observed) - tol,
                };
                if (hit) extreme++;
            }
            double p = exact ? (double)extreme / nulls.Count : (extreme + 1.0) / (nulls.Count + 1.0);
            return new PermutationResult(observed, Math.Min(1, p), nulls.Count, exact, nulls.ToArray());
        }

        private static void Split(double[] pooled, int[] idx, double[] ga, double[] gb)
        {
            int ai = 0, bi = 0, next = 0;
            for (int i = 0; i < pooled.Length; i++)
            {
                if (next < idx.Length && idx[next] == i) { ga[ai++] = pooled[i]; next++; }
                else gb[bi++] = pooled[i];
            }
        }

        // Lexicographic next k-combination of {0..n−1}; false after the last.
        private static bool NextCombination(int[] idx, int n)
        {
            int k = idx.Length, i = k - 1;
            while (i >= 0 && idx[i] == n - k + i) i--;
            if (i < 0) return false;
            idx[i]++;
            for (int j = i + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
            return true;
        }

        private static double Binomial(int n, int k)
        {
            double r = 1;
            for (int i = 1; i <= k; i++)
            {
                r = r * (n - k + i) / i;
                if (r > 1e12) return r;
            }
            return r;
        }

        private static double Mean(IReadOnlyList<double> x)
        {
            double s = 0;
            for (int i = 0; i < x.Count; i++) s += x[i];
            return s / x.Count;
        }
    }
}
