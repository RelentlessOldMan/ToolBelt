// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Numerics
{
    /// <summary>The outcome of a hypothesis test: the test statistic, its p-value, and degrees of freedom where applicable.</summary>
    public readonly struct TestResult
    {
        public TestResult(double statistic, double pValue, double degreesOfFreedom)
            : this(statistic, pValue, degreesOfFreedom, double.NaN)
        {
        }

        /// <summary>For tests with two degrees-of-freedom parameters (the F-test: numerator, then denominator).</summary>
        public TestResult(double statistic, double pValue, double degreesOfFreedom, double degreesOfFreedom2)
        {
            Statistic = statistic;
            PValue = pValue;
            DegreesOfFreedom = degreesOfFreedom;
            DegreesOfFreedom2 = degreesOfFreedom2;
        }

        /// <summary>The test statistic (t, chi-square, D, or U depending on the test).</summary>
        public double Statistic { get; }

        /// <summary>The p-value. Two-sided for the t and Mann-Whitney tests; upper-tail for chi-square; the KS tail probability for KS.</summary>
        public double PValue { get; }

        /// <summary>Degrees of freedom where the test defines them (NaN for KS and Mann-Whitney); the numerator df for F.</summary>
        public double DegreesOfFreedom { get; }

        /// <summary>The second degrees-of-freedom parameter (denominator df for F); NaN for single-df tests.</summary>
        public double DegreesOfFreedom2 { get; }

        /// <summary>
        /// The decision at significance level <paramref name="alpha"/>: true when p &lt; α, i.e. the null hypothesis is
        /// rejected. Choose α before looking at the data.
        /// </summary>
        public bool IsSignificant(double alpha = 0.05)
        {
            if (!(alpha > 0 && alpha < 1)) throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "Alpha must be in (0, 1).");
            return PValue < alpha;
        }

        public override string ToString() => double.IsNaN(DegreesOfFreedom2)
            ? string.Format(CultureInfo.InvariantCulture, "statistic={0:0.######}, p={1:0.######}, df={2:0.###}", Statistic, PValue, DegreesOfFreedom)
            : string.Format(CultureInfo.InvariantCulture, "statistic={0:0.######}, p={1:0.######}, df=({2:0.###}, {3:0.###})",
                Statistic, PValue, DegreesOfFreedom, DegreesOfFreedom2);
    }

    /// <summary>
    /// Classical significance tests built on <see cref="SpecialFunctions"/>-style primitives (duplicated
    /// privately here so the file stands alone): Student's t (one-sample, paired, and two-sample with Welch
    /// or pooled variance), Pearson's chi-square (goodness-of-fit and independence), Kolmogorov-Smirnov
    /// (one- and two-sample), the Mann-Whitney U rank-sum test, and the F-test for equal variances. Each returns a
    /// <see cref="TestResult"/> with the statistic, degrees of freedom, p-value and an <c>IsSignificant(α)</c> decision.
    /// p-values are exact up to the usual numerical tolerances for the t and chi-square tests; the KS and
    /// Mann-Whitney p-values use the standard asymptotic approximations (document-worthy for very small n).
    /// </summary>
    public static class HypothesisTests
    {
        // ---------- Student's t ----------

        /// <summary>One-sample t-test of the sample mean against <paramref name="populationMean"/> (two-sided).</summary>
        public static TestResult OneSampleT(IReadOnlyList<double> sample, double populationMean)
        {
            RequireCount(sample, 2, nameof(sample));
            var (mean, variance, n) = MeanVariance(sample);
            double df = n - 1;
            if (variance == 0)
                return mean == populationMean
                    ? new TestResult(0, 1, df)
                    : new TestResult(mean > populationMean ? double.PositiveInfinity : double.NegativeInfinity, 0, df);
            double t = (mean - populationMean) / Math.Sqrt(variance / n);
            return new TestResult(t, TwoSidedTP(t, df), df);
        }

        /// <summary>Paired t-test on the per-element differences of two equal-length samples (two-sided).</summary>
        public static TestResult PairedT(IReadOnlyList<double> first, IReadOnlyList<double> second)
        {
            RequireCount(first, 2, nameof(first));
            if (second is null) throw new ArgumentNullException(nameof(second));
            if (first.Count != second.Count)
                throw new ArgumentException("Paired samples must have the same length.", nameof(second));
            var diffs = new double[first.Count];
            for (int i = 0; i < diffs.Length; i++) diffs[i] = first[i] - second[i];
            return OneSampleT(diffs, 0);
        }

        /// <summary>
        /// Two-sample t-test of equal means (two-sided). Defaults to Welch's unequal-variance test; pass
        /// <paramref name="equalVariance"/> = true for the pooled-variance (Student) form.
        /// </summary>
        public static TestResult TwoSampleT(IReadOnlyList<double> a, IReadOnlyList<double> b, bool equalVariance = false)
        {
            RequireCount(a, 2, nameof(a));
            RequireCount(b, 2, nameof(b));
            var (ma, va, na) = MeanVariance(a);
            var (mb, vb, nb) = MeanVariance(b);

            double t, df;
            if (equalVariance)
            {
                double pooled = ((na - 1) * va + (nb - 1) * vb) / (na + nb - 2);
                double se = Math.Sqrt(pooled * (1.0 / na + 1.0 / nb));
                df = na + nb - 2;
                t = se == 0 ? SignedInfinity(ma - mb) : (ma - mb) / se;
            }
            else
            {
                double sa = va / na, sb = vb / nb;
                double se = Math.Sqrt(sa + sb);
                // Welch-Satterthwaite degrees of freedom.
                df = (sa + sb) * (sa + sb) / (sa * sa / (na - 1) + sb * sb / (nb - 1));
                t = se == 0 ? SignedInfinity(ma - mb) : (ma - mb) / se;
            }

            if (double.IsInfinity(t)) return new TestResult(t, 0, df);
            if (double.IsNaN(t)) return new TestResult(0, 1, df); // both variances zero and means equal
            return new TestResult(t, TwoSidedTP(t, df), df);
        }

        // ---------- F-test for equal variances ----------

        /// <summary>
        /// Two-sided F-test that two normal populations have equal variances: F = s²(a)/s²(b) with (n_a − 1, n_b − 1)
        /// degrees of freedom; p = 2·min(P(F′ ≤ F), P(F′ ≥ F)). Very sensitive to non-normality — for skewed or
        /// heavy-tailed data a significant result may reflect the shape rather than the spread.
        /// </summary>
        public static TestResult FTestEqualVariances(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            RequireCount(a, 2, nameof(a));
            RequireCount(b, 2, nameof(b));
            var (_, va, na) = MeanVariance(a);
            var (_, vb, nb) = MeanVariance(b);
            double d1 = na - 1, d2 = nb - 1;
            if (vb == 0)
                return va == 0 ? new TestResult(double.NaN, 1, d1, d2) : new TestResult(double.PositiveInfinity, 0, d1, d2);
            double f = va / vb;
            if (f == 0) return new TestResult(0, 0, d1, d2);

            // Each tail from its own incomplete-beta argument, so tiny p-values keep their precision.
            double lower = RegularizedBetaI(d1 * f / (d1 * f + d2), d1 / 2, d2 / 2);
            double upper = RegularizedBetaI(d2 / (d1 * f + d2), d2 / 2, d1 / 2);
            double p = Math.Min(1, 2 * Math.Min(lower, upper));
            return new TestResult(f, p, d1, d2);
        }

        // ---------- Pearson's chi-square ----------

        /// <summary>Chi-square goodness-of-fit test (upper-tail p). df = categories - 1.</summary>
        public static TestResult ChiSquareGoodnessOfFit(IReadOnlyList<double> observed, IReadOnlyList<double> expected)
        {
            if (observed is null) throw new ArgumentNullException(nameof(observed));
            if (expected is null) throw new ArgumentNullException(nameof(expected));
            if (observed.Count != expected.Count)
                throw new ArgumentException("Observed and expected must have the same length.", nameof(expected));
            if (observed.Count < 2)
                throw new ArgumentException("Need at least two categories.", nameof(observed));

            double chi2 = 0;
            for (int i = 0; i < observed.Count; i++)
            {
                if (expected[i] <= 0)
                    throw new ArgumentException($"Expected count at index {i} must be positive.", nameof(expected));
                double d = observed[i] - expected[i];
                chi2 += d * d / expected[i];
            }
            double df = observed.Count - 1;
            return new TestResult(chi2, ChiSquareUpperP(chi2, df), df);
        }

        /// <summary>Chi-square test of independence over an r×c contingency table (upper-tail p). df = (r-1)(c-1).</summary>
        public static TestResult ChiSquareIndependence(double[,] contingency)
        {
            if (contingency is null) throw new ArgumentNullException(nameof(contingency));
            int r = contingency.GetLength(0), c = contingency.GetLength(1);
            if (r < 2 || c < 2) throw new ArgumentException("Contingency table must be at least 2×2.", nameof(contingency));

            var rowSum = new double[r];
            var colSum = new double[c];
            double total = 0;
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                {
                    double v = contingency[i, j];
                    if (v < 0) throw new ArgumentException("Counts must be non-negative.", nameof(contingency));
                    rowSum[i] += v; colSum[j] += v; total += v;
                }
            if (total <= 0) throw new ArgumentException("Contingency table must contain positive counts.", nameof(contingency));
            for (int i = 0; i < r; i++)
                if (rowSum[i] == 0) throw new ArgumentException($"Row {i} is all zero (zero marginal).", nameof(contingency));
            for (int j = 0; j < c; j++)
                if (colSum[j] == 0) throw new ArgumentException($"Column {j} is all zero (zero marginal).", nameof(contingency));

            double chi2 = 0;
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                {
                    double e = rowSum[i] * colSum[j] / total;
                    double d = contingency[i, j] - e;
                    chi2 += d * d / e;
                }
            double df = (r - 1) * (c - 1);
            return new TestResult(chi2, ChiSquareUpperP(chi2, df), df);
        }

        // ---------- Kolmogorov-Smirnov ----------

        /// <summary>One-sample KS test comparing a sample to a reference CDF. Statistic is the KS distance D.</summary>
        public static TestResult KolmogorovSmirnovOneSample(IReadOnlyList<double> sample, Func<double, double> cdf)
        {
            RequireCount(sample, 1, nameof(sample));
            if (cdf is null) throw new ArgumentNullException(nameof(cdf));

            var sorted = new double[sample.Count];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = sample[i];
            Array.Sort(sorted);

            int n = sorted.Length;
            double d = 0;
            for (int i = 0; i < n; i++)
            {
                double f = cdf(sorted[i]);
                double dPlus = (i + 1.0) / n - f;
                double dMinus = f - (double)i / n;
                if (dPlus > d) d = dPlus;
                if (dMinus > d) d = dMinus;
            }
            double en = Math.Sqrt(n);
            return new TestResult(d, KolmogorovQ((en + 0.12 + 0.11 / en) * d), double.NaN);
        }

        /// <summary>Two-sample KS test comparing the empirical CDFs of two samples. Statistic is the KS distance D.</summary>
        public static TestResult KolmogorovSmirnovTwoSample(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            RequireCount(a, 1, nameof(a));
            RequireCount(b, 1, nameof(b));

            var sa = ToSortedArray(a);
            var sb = ToSortedArray(b);
            int na = sa.Length, nb = sb.Length;
            int i = 0, j = 0;
            double d = 0;
            // Advance both empirical CDFs past every point equal to the current value before measuring the
            // gap — otherwise cross-sample ties create spurious transient gaps (identical samples would
            // report D > 0).
            while (i < na && j < nb)
            {
                double x = Math.Min(sa[i], sb[j]);
                while (i < na && sa[i] == x) i++;
                while (j < nb && sb[j] == x) j++;
                double gap = Math.Abs((double)i / na - (double)j / nb);
                if (gap > d) d = gap;
            }
            double en = Math.Sqrt((double)na * nb / (na + nb));
            return new TestResult(d, KolmogorovQ((en + 0.12 + 0.11 / en) * d), double.NaN);
        }

        // ---------- Mann-Whitney U ----------

        /// <summary>
        /// Mann-Whitney U rank-sum test for a difference in distribution (two-sided). The statistic is the
        /// smaller of the two U values; the p-value uses the normal approximation with a tie correction and
        /// a continuity correction (best for moderate-to-large samples).
        /// </summary>
        public static TestResult MannWhitneyU(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            RequireCount(a, 1, nameof(a));
            RequireCount(b, 1, nameof(b));
            int na = a.Count, nb = b.Count, total = na + nb;

            // Merge, sort by value, assign average (mid) ranks to ties.
            var values = new double[total];
            var fromA = new bool[total];
            for (int k = 0; k < na; k++) { values[k] = a[k]; fromA[k] = true; }
            for (int k = 0; k < nb; k++) { values[na + k] = b[k]; fromA[na + k] = false; }
            int[] order = new int[total];
            for (int k = 0; k < total; k++) order[k] = k;
            Array.Sort(order, (x, y) => values[x].CompareTo(values[y]));

            var ranks = new double[total];
            double tieTerm = 0;
            int p = 0;
            while (p < total)
            {
                int q = p;
                while (q + 1 < total && values[order[q + 1]] == values[order[p]]) q++;
                double avgRank = (p + q) / 2.0 + 1; // 1-based average of positions p..q
                for (int k = p; k <= q; k++) ranks[order[k]] = avgRank;
                long t = q - p + 1;
                tieTerm += (double)t * t * t - t;
                p = q + 1;
            }

            double rankSumA = 0;
            for (int k = 0; k < total; k++) if (fromA[k]) rankSumA += ranks[k];

            double u1 = rankSumA - (double)na * (na + 1) / 2.0;
            double u2 = (double)na * nb - u1;
            double u = Math.Min(u1, u2);

            double meanU = (double)na * nb / 2.0;
            double sd = Math.Sqrt((double)na * nb / 12.0 * ((total + 1) - tieTerm / ((double)total * (total - 1))));
            if (sd == 0) return new TestResult(u, 1, double.NaN);

            double z = (Math.Abs(u1 - meanU) - 0.5) / sd; // continuity-corrected
            if (z < 0) z = 0;
            double pValue = 2 * (1 - NormalCdf(z));
            if (pValue > 1) pValue = 1;
            if (pValue < 0) pValue = 0;
            return new TestResult(u, pValue, double.NaN);
        }

        // ---------- shared helpers ----------

        private static (double mean, double variance, int n) MeanVariance(IReadOnlyList<double> x)
        {
            int n = x.Count;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += x[i];
            mean /= n;
            double ss = 0;
            for (int i = 0; i < n; i++) { double d = x[i] - mean; ss += d * d; }
            return (mean, ss / (n - 1), n);
        }

        private static double[] ToSortedArray(IReadOnlyList<double> x)
        {
            var arr = new double[x.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = x[i];
            Array.Sort(arr);
            return arr;
        }

        private static double SignedInfinity(double diff)
            => diff == 0 ? double.NaN : (diff > 0 ? double.PositiveInfinity : double.NegativeInfinity);

        private static void RequireCount(IReadOnlyList<double> x, int min, string name)
        {
            if (x is null) throw new ArgumentNullException(name);
            if (x.Count < min) throw new ArgumentException($"Need at least {min} value(s).", name);
        }

        // Two-sided Student-t p-value: P(|T| > |t|) = I_{df/(df+t^2)}(df/2, 1/2).
        private static double TwoSidedTP(double t, double df)
            => RegularizedBetaI(df / (df + t * t), df / 2.0, 0.5);

        // Chi-square upper-tail p-value: Q(df/2, x/2).
        private static double ChiSquareUpperP(double x, double df)
            => x <= 0 ? 1.0 : 1.0 - RegularizedGammaP(df / 2.0, x / 2.0);

        // Standard normal CDF via the regularized incomplete gamma (erf), accurate to ~1e-12.
        private static double NormalCdf(double z)
        {
            double p = RegularizedGammaP(0.5, z * z / 2.0);
            return z >= 0 ? 0.5 * (1 + p) : 0.5 * (1 - p);
        }

        // Kolmogorov distribution tail Q_KS(λ) = 2 Σ (-1)^{j-1} e^{-2 j² λ²}.
        private static double KolmogorovQ(double lambda)
        {
            if (lambda <= 0) return 1.0;
            double sum = 0, sign = 1;
            for (int j = 1; j <= 100; j++)
            {
                double term = Math.Exp(-2.0 * j * j * lambda * lambda);
                sum += sign * term;
                sign = -sign;
                if (term < 1e-14) break;
            }
            double q = 2 * sum;
            return q < 0 ? 0 : (q > 1 ? 1 : q);
        }

        // ---- special functions, duplicated privately (no leaf-to-leaf dependency; see SpecialFunctions.cs) ----

        private const double Tiny = 1e-300;
        private const double Eps = 1e-15;
        private const int MaxIter = 300;

        private static readonly double[] LanczosG =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };

        private static double LnGamma(double x)
        {
            double a = LanczosG[0];
            double t = x + 6.5;
            double xm1 = x - 1;
            for (int i = 1; i < LanczosG.Length; i++) a += LanczosG[i] / (xm1 + i);
            return 0.5 * Math.Log(2 * Math.PI) + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }

        private static double RegularizedGammaP(double a, double x)
        {
            if (x <= 0) return 0.0;
            if (x < a + 1.0)
            {
                double ap = a, sum = 1.0 / a, del = 1.0 / a;
                for (int n = 0; n < MaxIter; n++)
                {
                    ap += 1.0; del *= x / ap; sum += del;
                    if (Math.Abs(del) < Math.Abs(sum) * Eps) break;
                }
                return sum * Math.Exp(-x + a * Math.Log(x) - LnGamma(a));
            }
            double b = x + 1.0 - a, c = 1.0 / Tiny, d = 1.0 / b, h = d;
            for (int i = 1; i < MaxIter; i++)
            {
                double an = -i * (i - a);
                b += 2.0;
                d = an * d + b; if (Math.Abs(d) < Tiny) d = Tiny;
                c = b + an / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1.0 / d;
                double del = d * c; h *= del;
                if (Math.Abs(del - 1.0) < Eps) break;
            }
            return 1.0 - Math.Exp(-x + a * Math.Log(x) - LnGamma(a)) * h;
        }

        private static double RegularizedBetaI(double x, double a, double b)
        {
            if (x <= 0) return 0.0;
            if (x >= 1) return 1.0;
            double front = Math.Exp(LnGamma(a + b) - LnGamma(a) - LnGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
            if (x < (a + 1.0) / (a + b + 2.0))
                return front * BetaCf(x, a, b) / a;
            return 1.0 - front * BetaCf(1 - x, b, a) / b;
        }

        private static double BetaCf(double x, double a, double b)
        {
            double qab = a + b, qap = a + 1.0, qam = a - 1.0;
            double c = 1.0, d = 1.0 - qab * x / qap;
            if (Math.Abs(d) < Tiny) d = Tiny;
            d = 1.0 / d;
            double h = d;
            for (int m = 1; m <= MaxIter; m++)
            {
                double m2 = 2.0 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1.0 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1.0 / d; h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1.0 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1.0 / d;
                double del = d * c; h *= del;
                if (Math.Abs(del - 1.0) < Eps) break;
            }
            return h;
        }
    }
}
