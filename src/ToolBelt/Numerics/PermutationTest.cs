// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>Which departure from the null hypothesis a test looks for.</summary>
    public enum Alternative
    {
        /// <summary>Either direction: twice the smaller one-sided p-value (works for statistics not centred on zero).</summary>
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
            NullDistribution = Array.AsReadOnly(nullDistribution);
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
            if (a.Count == 0 || b.Count == 0) throw new ArgumentException("Both samples must be non-empty.", a.Count == 0 ? nameof(a) : nameof(b));
            // Capped as Bootstrap caps resamples: the null distribution is one array.
            if (permutations < 1 || permutations > 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(permutations), permutations, "Must be 1 to Array.MaxLength (0x7FFFFFC7).");
            RequireAlternative(alternative);
            // The pooled values are one array: past Array.MaxLength in all, na + nb overflowed int (OverflowException).
            if ((long)a.Count + b.Count > 0x7FFFFFC7)
                throw new ArgumentException("The two samples hold more than Array.MaxLength (0x7FFFFFC7) values together.", nameof(b));
            RequireFinite(a, nameof(a));
            RequireFinite(b, nameof(b));

            int na = a.Count, n = na + b.Count;
            var pooled = new double[n];
            for (int i = 0; i < na; i++) pooled[i] = a[i];
            for (int i = 0; i < b.Count; i++) pooled[na + i] = b[i];
            double observed;
            if (statistic is null)
            {
                // The default statistic on data centred once, with compensated sums. Summed raw, its rounding error was
                // eps times the data's offset (1000.7 and 1000.3 swapped between groups came out 3e-14 apart, past the
                // tie tolerance), so rearrangements holding exactly the observed values were not counted and an exact
                // p-value of 1 read 0.7. The mean difference is unchanged by the shift.
                double c = Centre(pooled);
                for (int i = 0; i < n; i++) pooled[i] -= c;
                statistic = (x, y) => AccurateMean(x) - AccurateMean(y);
                var ca = new double[na];
                var cb = new double[n - na];
                Array.Copy(pooled, 0, ca, 0, na);
                Array.Copy(pooled, na, cb, 0, n - na);
                observed = statistic(ca, cb);
            }
            else observed = statistic(a, b);

            var ga = new double[na];
            var gb = new double[n - na];
            double total = Binomial(n, na);
            bool exact = total <= permutations;
            // Sized up front (total is exact below 1e12): grown as a list and then copied, it was held twice.
            var nulls = new double[exact ? (int)total : permutations];
            int at = 0;
            if (exact)
            {
                // Enumerate every split of the pooled sample into groups of the original sizes.
                var idx = new int[na];
                for (int i = 0; i < na; i++) idx[i] = i;
                while (true)
                {
                    Split(pooled, idx, ga, gb);
                    nulls[at++] = statistic(ga, gb);
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
                    nulls[at++] = statistic(ga, gb);
                }
            }
            return Result(observed, nulls, exact, alternative);
        }

        /// <summary>
        /// Paired test on the differences first − second: under the null each difference is equally likely to have either
        /// sign. The statistic is the mean difference. A sign pattern counts as being as extreme as the observed one by
        /// the exact sums of the differences as doubles (not to a relative tolerance), so the enumerated p-value is exact
        /// even when the differences cancel; differences that tie only in decimal (0.1 + 0.2 against 0.3) do not tie.
        /// </summary>
        public static PermutationResult Paired(
            IReadOnlyList<double> first, IReadOnlyList<double> second, Random random, int permutations = 10000,
            Alternative alternative = Alternative.TwoSided)
        {
            if (first is null) throw new ArgumentNullException(nameof(first));
            if (second is null) throw new ArgumentNullException(nameof(second));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (first.Count != second.Count) throw new ArgumentException("Paired samples must have the same length.", nameof(second));
            if (first.Count == 0) throw new ArgumentException("Samples must be non-empty.", nameof(first));
            RequireAlternative(alternative);
            // Capped as Bootstrap caps resamples: the null distribution is one array.
            if (permutations < 1 || permutations > 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(permutations), permutations, "Must be 1 to Array.MaxLength (0x7FFFFFC7).");
            RequireFinite(first, nameof(first));
            RequireFinite(second, nameof(second));

            int n = first.Count;
            var d = new double[n];
            for (int i = 0; i < n; i++)
            {
                d[i] = first[i] - second[i];
                // Finite values of opposite sign near 1e308 have an infinite difference, and every null sum became NaN.
                if (double.IsInfinity(d[i])) throw new ArgumentException($"The difference at [{i}] overflows.", nameof(second));
            }
            // Compensated means, and each sign pattern compared with the observed one by its sum as a double-double (the
            // error of a Neumaier sum's pair is at most n²ε²·Σ|d|), not by the means at a 1e-12 relative tolerance: summed
            // plainly, differences that cancel ({1e17, 3, −1e17}, mean 1) read as mean 0, and patterns whose means differ
            // by less than an ulp of a large one ({2, 5, −1, 2, 2e16, 6, 1}) read as ties, p = 0.5 for 3/128.
            double observed = AccurateMean(d);
            // Scaled by a power of two (exact) so the sums cannot overflow; the tolerance is in the scaled units.
            double big = 0;
            foreach (double v in d) big = Math.Max(big, Math.Abs(v));
            double unit = big > 1e300 / n ? Math.Pow(2, -64) : 1, absSum = 0;
            var ds = new double[n];
            for (int i = 0; i < n; i++) { ds[i] = d[i] * unit; absSum += Math.Abs(ds[i]); }
            double tol = 4.0 * n * n * (Eps * Eps) * absSum;
            PairSum(ds, out double obsHi, out double obsLo);
            var signed = new double[n];
            bool exact = n < 31 && (1L << n) <= permutations;
            var nulls = new double[exact ? 1 << n : permutations];
            int at = 0, greater = 0, less = 0;
            void Count()
            {
                PairSum(signed, out double hi, out double lo);
                double s = hi - obsHi, bb = s - hi, e = (hi - (s - bb)) + (-obsHi - bb);   // TwoSum: hi − obsHi = s + e exactly
                double diff = s + (e + (lo - obsLo));
                if (diff >= -tol) greater++;
                if (diff <= tol) less++;
                for (int i = 0; i < n; i++) signed[i] /= unit;
                nulls[at++] = AccurateMean(signed);
            }
            if (exact)
            {
                for (long mask = 0; mask < (1L << n); mask++)
                {
                    for (int i = 0; i < n; i++) signed[i] = ((mask >> i) & 1) == 0 ? ds[i] : -ds[i];
                    Count();
                }
            }
            else
            {
                for (int r = 0; r < permutations; r++)
                {
                    for (int i = 0; i < n; i++) signed[i] = random.Next(2) == 0 ? ds[i] : -ds[i];
                    Count();
                }
            }
            for (int i = 0; i < nulls.Length; i++)
                if (double.IsNaN(nulls[i])) throw new InvalidOperationException("The statistic returned NaN on a rearrangement.");
            return Result(observed, nulls, exact, alternative, greater, less);
        }

        private const double Eps = 1.1102230246251565e-16;   // 2^−53

        // The Neumaier sum as an unevaluated pair hi + lo.
        private static void PairSum(double[] x, out double hi, out double lo)
        {
            double s = 0, comp = 0;
            foreach (double v in x)
            {
                double t = s + v;
                comp += Math.Abs(s) >= Math.Abs(v) ? (s - t) + v : (v - t) + s;
                s = t;
            }
            hi = s + comp;
            lo = comp - (hi - s);
        }

        /// <summary>mean(a) − mean(b), the default two-sample statistic.</summary>
        public static double MeanDifference(IReadOnlyList<double> a, IReadOnlyList<double> b) => Mean(a) - Mean(b);

        // An undefined value was silently run as TwoSided.
        private static void RequireAlternative(Alternative alternative)
        {
            if (alternative != Alternative.TwoSided && alternative != Alternative.Greater && alternative != Alternative.Less)
                throw new ArgumentOutOfRangeException(nameof(alternative), alternative, "Not a defined Alternative.");
        }

        private static PermutationResult Result(double observed, double[] nulls, bool exact, Alternative alternative)
        {
            Count(observed, nulls, out int greater, out int less);
            return Result(observed, nulls, exact, alternative, greater, less);
        }

        private static void Count(double observed, double[] nulls, out int greater, out int less)
        {
            // A tolerance relative to the statistic's own scale, so rearrangements equal to the observed value up to rounding
            // count as "as extreme" — whatever the units (an absolute 1e-12 would call everything a tie for data near 1e-13).
            // The scale is per comparison: the larger of the two values and the median finite |statistic| (the typical size,
            // for an observed value that is rounding noise about zero). Scaled by the largest statistic of all, one huge
            // rearrangement (a variance ratio near 1e12) made every value a tie (p = 1 for a true 9/21). Infinite values take
            // no part in the scale; a NaN would fail every comparison (p = 0), so it is refused.
            if (double.IsNaN(observed)) throw new InvalidOperationException("The statistic returned NaN on the observed data.");
            var finite = new List<double>(nulls.Length);
            foreach (double s in nulls)
            {
                if (double.IsNaN(s)) throw new InvalidOperationException("The statistic returned NaN on a rearrangement.");
                if (!double.IsInfinity(s)) finite.Add(Math.Abs(s));
            }
            finite.Sort();
            double typical = finite.Count == 0 ? 0 : finite[finite.Count / 2];
            double obsScale = double.IsInfinity(observed) ? 0 : Math.Abs(observed);
            greater = less = 0;
            foreach (double s in nulls)
            {
                double tol = double.IsInfinity(s) ? 0 : 1e-12 * Math.Max(typical, Math.Max(obsScale, Math.Abs(s)));
                if (s >= observed - tol) greater++;
                if (s <= observed + tol) less++;
            }
        }

        private static PermutationResult Result(double observed, double[] nulls, bool exact, Alternative alternative, int greater, int less)
        {
            double Tail(int count) => exact ? (double)count / nulls.Length : (count + 1.0) / (nulls.Length + 1.0);
            // Two-sided: twice the smaller tail. Equals the usual |S| ≥ |observed| rule for a null symmetric about zero (mean
            // difference) but stays correct for statistics centred elsewhere (a variance ratio is centred on 1).
            double p = alternative switch
            {
                Alternative.Greater => Tail(greater),
                Alternative.Less => Tail(less),
                _ => 2 * Math.Min(Tail(greater), Tail(less)),
            };
            return new PermutationResult(observed, Math.Min(1, p), nulls.Length, exact, nulls);
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

        private static void RequireFinite(IReadOnlyList<double> x, string name)
        {
            for (int i = 0; i < x.Count; i++)
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i])) throw new ArgumentException($"Value [{i}] is not finite.", name);
        }

        // The pooled mean, or 0 when some deviation from it would overflow (values of both signs near 1e308).
        private static double Centre(double[] v)
        {
            double s = 0;
            foreach (double e in v) s += e / v.Length;
            foreach (double e in v) if (double.IsInfinity(e - s)) return 0;
            return s;
        }

        // Neumaier-compensated mean: reordering the same values changes it by rounding of the result alone, not by n·eps
        // of the terms (a Monte Carlo null on 10⁴ values sits far below the terms' own size).
        private static double AccurateMean(IReadOnlyList<double> x)
        {
            double s = 0, comp = 0;
            for (int i = 0; i < x.Count; i++)
            {
                double v = x[i], t = s + v;
                comp += Math.Abs(s) >= Math.Abs(v) ? (s - t) + v : (v - t) + s;
                s = t;
            }
            double total = s + comp;
            if (!double.IsInfinity(total) && !double.IsInfinity(s)) return total / x.Count;
            return Mean(x);
        }

        private static double Mean(IReadOnlyList<double> x)
        {
            double s = 0;
            for (int i = 0; i < x.Count; i++) s += x[i];
            if (!double.IsInfinity(s)) return s / x.Count;
            // The sum of finite values overflowed (two of 1e308): average term by term instead, so it is not ∞ − ∞ = NaN.
            s = 0;
            for (int i = 0; i < x.Count; i++) s += x[i] / x.Count;
            return s;
        }
    }
}
