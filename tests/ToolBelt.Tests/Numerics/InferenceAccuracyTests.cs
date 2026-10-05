using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// The t / chi-square intervals and the F-test, graded three ways: worked closed-form examples (short table values
    /// only), a differential check between independent implementations, and simulation — data drawn from a known
    /// normal distribution must give ~95% coverage for a 95% interval and ~α false positives for a level-α test.
    /// </summary>
    public sealed class InferenceAccuracyTests
    {
        private static readonly double[] Five = { 1, 2, 3, 4, 5 };   // mean 3, s² = 2.5

        private static double[] Normal(Random rng, int n, double mean = 0, double sd = 1)
        {
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = rng.NextGaussian(mean, sd);
            return x;
        }

        // ---------- intervals: worked examples ----------

        public void ForMeanT_WorkedExample()
        {
            var (lo, hi) = ConfidenceInterval.ForMeanT(Five);
            double margin = 2.776445 * Math.Sqrt(2.5 / 5);                  // t(0.975, 4) from tables
            Check.Close(3 - margin, lo, 1e-5);
            Check.Close(3 + margin, hi, 1e-5);
            var (zlo, zhi) = ConfidenceInterval.ForMean(Five);
            Check.True(lo < zlo && hi > zhi, "the t interval is wider than the z interval");
        }

        public void ForMeanT_ConvergesToZForLargeSamples()
        {
            var rng = new DeterministicRandom(5);
            double[] big = Normal(rng, 20000);
            var (tlo, thi) = ConfidenceInterval.ForMeanT(big);
            var (zlo, zhi) = ConfidenceInterval.ForMean(big);
            Check.Close(thi - tlo, zhi - zlo, (zhi - zlo) * 1e-4);
        }

        public void ForVariance_WorkedExample()
        {
            var (lo, hi) = ConfidenceInterval.ForVariance(Five);
            Check.Close(10 / 11.143287, lo, 1e-5);                         // (n−1)s² / χ²(0.975, 4)
            Check.Close(10 / 0.484419, hi, 1e-4);                          // (n−1)s² / χ²(0.025, 4)
            var (slo, shi) = ConfidenceInterval.ForStandardDeviation(Five);
            Check.Close(Math.Sqrt(lo), slo, 1e-15);
            Check.Close(Math.Sqrt(hi), shi, 1e-14);
        }

        // ---------- intervals: simulated coverage ----------

        public void Coverage_TIntervalIsNominal_ZIntervalUndercoversAtSmallN()
        {
            var rng = new DeterministicRandom(20261005);
            const int trials = 4000;
            int tHits = 0, zHits = 0, vHits = 0;
            for (int i = 0; i < trials; i++)
            {
                double[] x = Normal(rng, 5, mean: 10, sd: 2);
                var (tl, th) = ConfidenceInterval.ForMeanT(x);
                var (zl, zh) = ConfidenceInterval.ForMean(x);
                var (vl, vh) = ConfidenceInterval.ForVariance(x);
                if (tl <= 10 && 10 <= th) tHits++;
                if (zl <= 10 && 10 <= zh) zHits++;
                if (vl <= 4 && 4 <= vh) vHits++;
            }
            double t = (double)tHits / trials, z = (double)zHits / trials, v = (double)vHits / trials;
            Check.True(t > 0.938 && t < 0.962, $"t coverage {t}");
            Check.True(v > 0.938 && v < 0.962, $"variance coverage {v}");
            // The z interval's true coverage at n = 5 is 2·F_t4(1.96) − 1 ≈ 0.879, as its documentation says.
            double expectedZ = 2 * Distributions.StudentTCdf(Distributions.NormalQuantile(0.975), 4) - 1;
            Check.Close(0.879, expectedZ, 1e-3);
            Check.True(Math.Abs(z - expectedZ) < 0.015, $"z coverage {z} vs theoretical {expectedZ}");
        }

        // ---------- F-test ----------

        public void FTest_EqualSpreadGivesFOneAndPOne()
        {
            var r = HypothesisTests.FTestEqualVariances(new double[] { 1, 2, 3 }, new double[] { 10, 11, 12 });
            Check.Close(1, r.Statistic, 1e-15);
            Check.Close(1, r.PValue, 1e-12);
            Check.Equal(2.0, r.DegreesOfFreedom);
            Check.Equal(2.0, r.DegreesOfFreedom2);
        }

        public void FTest_AgreesWithTheIndependentFDistribution()
        {
            var rng = new DeterministicRandom(77);
            for (int trial = 0; trial < 300; trial++)
            {
                double[] a = Normal(rng, rng.Next(2, 30), sd: 1 + rng.NextDouble() * 3);
                double[] b = Normal(rng, rng.Next(2, 30), sd: 1 + rng.NextDouble() * 3);
                var r = HypothesisTests.FTestEqualVariances(a, b);
                double cdf = Distributions.FCdf(r.Statistic, r.DegreesOfFreedom, r.DegreesOfFreedom2);
                double expected = Math.Min(1, 2 * Math.Min(cdf, 1 - cdf));
                Check.Close(expected, r.PValue, 1e-12, $"trial {trial}");

                // Swapping the samples inverts F and leaves the two-sided p unchanged.
                var swapped = HypothesisTests.FTestEqualVariances(b, a);
                Check.Close(1 / r.Statistic, swapped.Statistic, 1e-12 * Math.Max(1, 1 / r.Statistic));
                Check.Close(r.PValue, swapped.PValue, 1e-12);
            }
        }

        public void FTest_DegenerateSpreads()
        {
            var flatB = HypothesisTests.FTestEqualVariances(new double[] { 1, 2, 4 }, new double[] { 5, 5, 5 });
            Check.True(double.IsPositiveInfinity(flatB.Statistic));
            Check.Equal(0.0, flatB.PValue);
            var bothFlat = HypothesisTests.FTestEqualVariances(new double[] { 3, 3 }, new double[] { 5, 5, 5 });
            Check.True(double.IsNaN(bothFlat.Statistic));
            Check.Equal(1.0, bothFlat.PValue);
            Check.Throws<ArgumentException>(() => HypothesisTests.FTestEqualVariances(new double[] { 1 }, new double[] { 1, 2 }));
        }

        // ---------- decisions and simulated false-positive rates ----------

        public void IsSignificant_AndReporting()
        {
            var r = new TestResult(2.5, 0.03, 10);
            Check.True(r.IsSignificant());
            Check.False(r.IsSignificant(0.01));
            Check.True(double.IsNaN(r.DegreesOfFreedom2));
            Check.Throws<ArgumentOutOfRangeException>(() => r.IsSignificant(0));
            Check.Throws<ArgumentOutOfRangeException>(() => r.IsSignificant(1));
            Check.Equal("statistic=2.5, p=0.03, df=10", r.ToString());
            Check.Equal("statistic=1.5, p=0.2, df=(3, 7)", new TestResult(1.5, 0.2, 3, 7).ToString());
        }

        public void FalsePositiveRates_MatchAlphaUnderTheNull()
        {
            var rng = new DeterministicRandom(424242);
            const int trials = 4000;
            int fRejects = 0, tRejects = 0;
            for (int i = 0; i < trials; i++)
            {
                double[] a = Normal(rng, 8, mean: 3, sd: 1.5);
                double[] b = Normal(rng, 12, mean: 3, sd: 1.5);
                if (HypothesisTests.FTestEqualVariances(a, b).IsSignificant(0.05)) fRejects++;
                if (HypothesisTests.TwoSampleT(a, b).IsSignificant(0.05)) tRejects++;
            }
            double f = (double)fRejects / trials, t = (double)tRejects / trials;
            Check.True(f > 0.038 && f < 0.062, $"F-test false-positive rate {f}");
            Check.True(t > 0.038 && t < 0.062, $"Welch t-test false-positive rate {t}");
        }

        public void Power_FTestDetectsARealDifference()
        {
            var rng = new DeterministicRandom(99);
            int rejects = 0;
            for (int i = 0; i < 500; i++)
                if (HypothesisTests.FTestEqualVariances(Normal(rng, 25, sd: 1), Normal(rng, 25, sd: 2.5)).IsSignificant())
                    rejects++;
            Check.True(rejects > 450, $"only {rejects}/500 detected a 2.5x spread difference");
        }
    }
}
