using System;
using System.Collections;
using System.Collections.Generic;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 146 (statistics, ninth pass): each failed on the code before its fix.
    public sealed class StatisticsReview146Tests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        // A list that reports a size without storing it: 0, 1, 0, 1, ...
        private sealed class Alternating : IReadOnlyList<double>
        {
            public Alternating(int count) => Count = count;
            public int Count { get; }
            public double this[int index] => index & 1;
            public IEnumerator<double> GetEnumerator() { for (int i = 0; i < Count; i++) yield return i & 1; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // ---------- FTestEqualVariances: far in a tail ----------

        // With one degree of freedom each, P(F′ ≤ F) = (2/π)·atan(√F), so p = (4/π)·r for a small ratio r of spreads.
        // F = r² underflowed (p = 0), overflowed for the reverse order (F = ∞, p = 0), or was subnormal (p 5.6e-6 off).
        public void FTest_FarTailsKeepTheirPValue()
        {
            foreach (double r in new[] { 1e-170, 1e-160, 1e-200, 1e-155 })
            {
                double expected = 4 / Math.PI * r;
                Rel(expected, HypothesisTests.FTestEqualVariances(new[] { 0, r }, new[] { 0.0, 1 }).PValue, 1e-12, $"low {r}");
                Rel(expected, HypothesisTests.FTestEqualVariances(new[] { 0.0, 1 }, new[] { 0, r }).PValue, 1e-12, $"high {r}");
            }
            // Guard: an ordinary ratio is as before.
            Rel(4 / Math.PI * Math.Atan(0.5), HypothesisTests.FTestEqualVariances(new[] { 0, 0.5 }, new[] { 0.0, 1 }).PValue, 1e-12, "0.5");
            Check.Equal(0.25, HypothesisTests.FTestEqualVariances(new[] { 0, 0.5 }, new[] { 0.0, 1 }).Statistic);
        }

        // ---------- The t and F tests: a spread past double.MaxValue ----------

        // Welch's df was NaN and threw ArgumentOutOfRangeException; the pooled t came back 0.
        public void TwoSampleT_ASpreadPastMaxValue()
        {
            var big = new[] { -1.5e308, 1.5e308 };
            var welch = HypothesisTests.TwoSampleT(new[] { 1.0, 2, 3 }, big);
            Rel(2 / 1.5e308, welch.Statistic, 1e-12, "welch t");
            Check.True(welch.DegreesOfFreedom >= 1 && welch.DegreesOfFreedom < 1 + 1e-12, $"df {welch.DegreesOfFreedom:R}");
            Rel(1, welch.PValue, 1e-12, "welch p");
            var pooled = HypothesisTests.TwoSampleT(new[] { 1.0, 2, 3 }, big, equalVariance: true);
            Rel(2 / (Math.Sqrt(1.25) * 1e308), pooled.Statistic, 1e-12, "pooled t");
            Check.Equal(3.0, pooled.DegreesOfFreedom);
            var both = HypothesisTests.TwoSampleT(big, new[] { 1e308, 1e308 });
            Rel(-1e308 / 1.5e308, both.Statistic, 1e-12, "both t");
        }

        // {−a, a, a} has t = 0.5 against 0 whatever a is; at a = 1.7e308 the spread overflowed and t was 0.
        public void OneSampleT_ASpreadPastMaxValue()
        {
            var r = HypothesisTests.OneSampleT(new[] { -1.7e308, 1.7e308, 1.7e308 }, 0);
            Rel(0.5, r.Statistic, 1e-12, "t");
            Rel(0.5, HypothesisTests.OneSampleT(new[] { -1.7, 1.7, 1.7 }, 0).Statistic, 1e-12, "guard");
        }

        // F = (s_a/s_b)² past MaxValue is ∞, but p = (4/π)·s_b/s_a was 0.
        public void FTest_ASpreadPastMaxValue()
        {
            var r = HypothesisTests.FTestEqualVariances(new[] { -1.7e308, 1.7e308 }, new[] { 0.0, 1 });
            Check.True(double.IsPositiveInfinity(r.Statistic), r.Statistic.ToString("R"));
            Rel(4 / Math.PI / 1.7e308 / 2, r.PValue, 1e-9, "p");
            var s = HypothesisTests.FTestEqualVariances(new[] { 0.0, 1 }, new[] { -1.7e308, 1.7e308 });
            Rel(4 / Math.PI / 1.7e308 / 2, s.PValue, 1e-9, "reversed p");
            Check.True(s.Statistic < 1e-300, s.Statistic.ToString("R"));
        }

        // ---------- TwoSampleT: means far smaller than the other sample's values ----------

        // The difference of means was taken about a[0] for both samples, and a[0] was lost beside b's values: t was half.
        public void TwoSampleT_SmallMeansBesideHugeValues()
        {
            Rel(2e-300, HypothesisTests.TwoSampleT(new[] { 1.0, 2, 3 }, new[] { -1e300, 1e300 }).Statistic, 1e-12, "1e300");
            Rel(-2e-300, HypothesisTests.TwoSampleT(new[] { -1e300, 1e300 }, new[] { 1.0, 2, 3 }).Statistic, 1e-12, "reversed");
            Rel(2e-20, HypothesisTests.TwoSampleT(new[] { 1.0, 2, 3 }, new[] { -1e20, 1e20 }).Statistic, 1e-12, "1e20");
            // Guard: data on an offset keep their digits.
            Rel(-1 / Math.Sqrt(2.0 / 3), HypothesisTests.TwoSampleT(new[] { 1e6 + 1, 1e6 + 2, 1e6 + 3 }, new[] { 1e6 + 2, 1e6 + 3, 1e6 + 4 }).Statistic, 1e-9, "offset");
        }

        // ---------- TwoSampleT: more than 2^31 values in all ----------

        // na + nb − 2 overflowed int: df −2,094,967,298.
        public void TwoSampleT_PooledDfPastIntMax()
        {
            var a = new Alternating(1_073_741_825);
            var r = HypothesisTests.TwoSampleT(a, a, equalVariance: true);
            Check.Equal(2_147_483_648.0, r.DegreesOfFreedom);
            Check.Equal(0.0, r.Statistic);
            Check.Equal(1.0, r.PValue);
        }

        // ---------- MannWhitneyU, PermutationTest.TwoSample: counts past Array.MaxLength in all ----------

        // na + nb overflowed in a checked context: OverflowException, not the argument's fault named.
        public void TwoSampleCounts_PastArrayMaxLengthAreArgumentErrors()
        {
            var half = new Alternating(0x40000000);
            Check.Equal("b", Check.Throws<ArgumentException>(() => HypothesisTests.MannWhitneyU(half, half)).ParamName);
            Check.Equal("b", Check.Throws<ArgumentException>(() => PermutationTest.TwoSample(half, half, new Random(1), 10)).ParamName);
        }

        // ---------- KolmogorovSmirnovOneSample: the exact one-sided tail ----------

        // The running sum of ln C(n, j) lost 5.5e-10 relative at n = 100,000 (mpmath at 40 digits gives these).
        public void KolmogorovSmirnov_ExactTailKeepsItsDigits()
        {
            foreach (var (n, d, expected) in new[]
            {
                (100000, 0.010000000000000009, 4.093278023549112e-09),
                (50000, 0.020000000000000018, 8.35587873541989e-18),
            })
            {
                var x = new double[n];
                for (int i = 0; i < n; i++) x[i] = Math.Max(0, (i + 1.0) / n - d);
                var r = HypothesisTests.KolmogorovSmirnovOneSample(x, v => Math.Min(1, Math.Max(0, v)));
                Check.Equal(d, r.Statistic);
                Rel(expected, r.PValue, 1e-12, $"n {n}");
            }
        }

        // ---------- ConfidenceInterval.ForProportion: the Wilson lower bound ----------

        // center − half cancelled: 1.5056390115603862e-11 for 1.5056390115606628e-11 (mpmath).
        public void Wilson_LowerBoundAtFewSuccesses()
        {
            var (lower, upper) = ConfidenceInterval.ForProportion(1, 1_000_000_000, 1 - 1e-15);
            Rel(1.5056390115606628e-11, lower, 1e-13, "lower");
            Check.True(upper > lower);
            // Guard: an ordinary interval, and the bounds multiply to p²/denom.
            var (l2, u2) = ConfidenceInterval.ForProportion(30, 100);
            double z = 1.959963984540054, denom = 1 + z * z / 100;
            Rel(0.09 / denom, l2 * u2, 1e-12, "product");
            Check.True(l2 > 0.21 && l2 < 0.22 && u2 > 0.39 && u2 < 0.40, $"{l2:R} {u2:R}");
        }

        // ---------- MultipleRegression: Predict where Fitted is finite ----------

        // Each term scaled to y units on its own overflowed, and their sum was ∞ − ∞ = NaN at the observed rows.
        public void Regression_PredictMatchesFittedNearMaxValue()
        {
            var x = new double[,] { { 0, 0 }, { 1, 1.000001 }, { 2, 1.999999 }, { 3, 3 }, { 4, 4.000001 }, { 5, 4.999998 } };
            var y = new[] { 1e306, -1e306, 2e306, 0, -1e306, 1e306 };
            var fit = MultipleRegression.Fit(x, y);
            for (int i = 0; i < 6; i++)
            {
                double f = fit.Fitted[i];
                Check.True(!double.IsNaN(f) && !double.IsInfinity(f), $"fitted {i}");
                Rel(f, fit.Predict(new[] { x[i, 0], x[i, 1] }), 1e-9, $"row {i}");
                var ci = fit.ConfidenceInterval(new[] { x[i, 0], x[i, 1] });
                Check.True(!double.IsNaN(ci.Lower) && !double.IsNaN(ci.Upper), $"ci {i}");
            }
        }
    }
}
