using System;
using System.Reflection;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>Round 216 review regressions (statistics, thirteenth pass).</summary>
    public sealed class StatisticsReview216Tests
    {
        private static readonly double Down = Math.Pow(2, -10);

        private static void Near(double expected, double got, string at)
        {
            if (double.IsInfinity(expected) || double.IsInfinity(got)) { Check.Equal(expected, got, at); return; }
            Check.True(Math.Abs(got - expected) <= 1e-13 * Math.Abs(expected) + 1e-300, at + ": " + got.ToString("R") + " vs " + expected.ToString("R"));
        }

        private static readonly MethodInfo UpperP = typeof(HypothesisTests).GetMethod("KolmogorovSmirnovUpperP", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo Smirnov = typeof(HypothesisTests).GetMethod("SmirnovUpper", BindingFlags.NonPublic | BindingFlags.Static)!;

        // The one-sided Smirnov tail summed over every term, with the log binomial carried as the library does.
        private static double FullSmirnovUpper(int n, double d)
        {
            double sum = 0, lnC = 0, carry = 0, nd = n * d;
            int jMax = (int)Math.Floor(n * (1 - d));
            for (int j = 0; j <= jMax; j++)
            {
                if (j > 0)
                {
                    double step = Math.Log((double)(n - j + 1) / j), next = lnC + step;
                    carry += Math.Abs(lnC) >= Math.Abs(step) ? (lnC - next) + step : (step - next) + lnC;
                    lnC = next;
                }
                double a = ((n - j) - nd) / n, b = (nd + j) / n;
                if (!(a > 0)) break;
                sum += Math.Exp((lnC + carry) + (n - j) * Math.Log(a) + (j - 1) * Math.Log(b));
            }
            return Math.Min(1, d * sum);
        }

        // Past 100,000 points the tail below 1e-4 was Stephens' asymptotic form, 6% off at p = 5e-56 (one point more than
        // 100,000 with the same D dropped p by 6%). Graded against SciPy's kstwo (and
        // mpmath at 3e6 points, where kstwo is 1e-4 off), and the stopped sum against the full one.
        public void KolmogorovSmirnov_PastHundredThousandPoints_TailIsExact()
        {
            foreach (var (n, d, p) in new[]
            {
                (100001, 0.0252980947911893, 4.968498026160456e-56), (100001, 0.009486785546695988, 3.025823276933208e-08),
                (200000, 0.01788854381999832, 5.0380743449529e-56), (1000000, 0.008, 5.107892453868561e-56),
                (3000000, 0.003, 7.050407426492295e-24),
            })
            {
                double got = (double)UpperP.Invoke(null, new object[] { n, d })!;
                Check.True(Math.Abs(got - p) <= 1e-11 * p, n + " " + d.ToString("R") + ": " + got.ToString("R") + " vs " + p.ToString("R"));
            }
            var rng = new Random(2160);
            for (int trial = 0; trial < 60; trial++)
            {
                int n = rng.Next(2, 300000);
                double d = Math.Min(0.999, (0.5 + rng.NextDouble() * 12) / Math.Sqrt(n));
                double want = FullSmirnovUpper(n, d), got = (double)Smirnov.Invoke(null, new object[] { n, d })!;
                Check.True(Math.Abs(got - want) <= 1e-14 * want, n + " " + d.ToString("R") + ": " + got.ToString("R") + " vs " + want.ToString("R"));
            }
        }

        // A spread past double.MaxValue overflowed with nothing to fall back on: half at −MaxValue and half at +MaxValue
        // gave (−∞, ∞) for the mean and (∞, ∞) for the standard deviation, where the bounds are about ±3.5e307 and
        // 1.6e308. Graded against the same data scaled down by 2^-10 (exact) with the interval scaled back.
        public void ConfidenceInterval_SpreadPastMaxValue_GivesTheFiniteBounds()
        {
            double M = double.MaxValue;
            var half = new double[100];
            for (int i = 0; i < 100; i++) half[i] = i < 50 ? -M : M;
            var (lo, hi) = ConfidenceInterval.ForMean(half);
            Check.True(lo < 0 && hi > 0 && !double.IsInfinity(lo) && !double.IsInfinity(hi), lo + " " + hi);
            var (slo, _) = ConfidenceInterval.ForStandardDeviation(new[] { -M, -M, M, M });
            Check.True(slo > 1e308 && !double.IsInfinity(slo), slo.ToString("R"));

            var rng = new Random(216);
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(2, 40);
                var x = new double[n];
                var small = new double[n];
                double centre = (rng.NextDouble() * 2 - 1) * M;
                for (int i = 0; i < n; i++)
                {
                    double v = rng.Next(3) == 0 ? (rng.Next(2) == 0 ? M : -M) : centre + (rng.NextDouble() * 2 - 1) * M * 0.6;
                    if (double.IsInfinity(v)) v = Math.Sign(v) * M;
                    x[i] = v;
                    small[i] = v * Down;
                }
                double conf = new[] { 0.5, 0.9, 0.95, 0.999 }[rng.Next(4)];
                string at = "trial " + trial + " n " + n + " conf " + conf;
                var a = ConfidenceInterval.ForMean(x, conf); var ar = ConfidenceInterval.ForMean(small, conf);
                Near(ar.Lower / Down, a.Lower, at + " ForMean lower"); Near(ar.Upper / Down, a.Upper, at + " ForMean upper");
                var t = ConfidenceInterval.ForMeanT(x, conf); var tr = ConfidenceInterval.ForMeanT(small, conf);
                Near(tr.Lower / Down, t.Lower, at + " ForMeanT lower"); Near(tr.Upper / Down, t.Upper, at + " ForMeanT upper");
                var s = ConfidenceInterval.ForStandardDeviation(x, conf); var sr = ConfidenceInterval.ForStandardDeviation(small, conf);
                Near(sr.Lower / Down, s.Lower, at + " SD lower"); Near(sr.Upper / Down, s.Upper, at + " SD upper");
            }
        }
    }
}
