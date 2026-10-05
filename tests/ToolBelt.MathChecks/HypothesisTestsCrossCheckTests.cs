using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using MathNet.Numerics.Distributions;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's hypothesis-test p-values against Math.NET's distributions: the Student-t p-values
    /// against <see cref="StudentT"/> and the chi-square p-values against <see cref="ChiSquared"/>. This
    /// independently exercises the private regularized incomplete beta / gamma behind them. (KS and
    /// Mann-Whitney have no convenient Math.NET distribution and are validated by closed-form cases in the
    /// main suite.)
    /// </summary>
    public sealed class HypothesisTestsCrossCheckTests
    {
        private static readonly DeterministicRandom Rng = new DeterministicRandom(1234567);

        private static double[] Sample(int n, double center, double spread)
        {
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = center + (Rng.NextDouble() - 0.5) * spread;
            return x;
        }

        // Two-sided Student-t p-value from Math.NET for a given statistic and df.
        private static double TwoSidedTP(double t, double df)
            => 2 * (1 - StudentT.CDF(0, 1, df, Math.Abs(t)));

        public void OneSampleT_PValueMatchesMathNet()
        {
            for (int trial = 0; trial < 300; trial++)
            {
                var sample = Sample(Rng.Next(3, 30), 5, 10);
                var r = HypothesisTests.OneSampleT(sample, 4.5);
                Check.Close(TwoSidedTP(r.Statistic, r.DegreesOfFreedom), r.PValue, 1e-9, $"trial {trial}");
            }
        }

        public void TwoSampleT_Welch_PValueMatchesMathNet()
        {
            for (int trial = 0; trial < 300; trial++)
            {
                var a = Sample(Rng.Next(3, 30), 5, 10);
                var b = Sample(Rng.Next(3, 30), 6, 14);
                var r = HypothesisTests.TwoSampleT(a, b);
                Check.Close(TwoSidedTP(r.Statistic, r.DegreesOfFreedom), r.PValue, 1e-9, $"trial {trial}");
            }
        }

        public void TwoSampleT_Pooled_PValueMatchesMathNet()
        {
            for (int trial = 0; trial < 300; trial++)
            {
                var a = Sample(Rng.Next(3, 30), 5, 10);
                var b = Sample(Rng.Next(3, 30), 6, 10);
                var r = HypothesisTests.TwoSampleT(a, b, equalVariance: true);
                Check.Close(TwoSidedTP(r.Statistic, r.DegreesOfFreedom), r.PValue, 1e-9, $"trial {trial}");
            }
        }

        public void ChiSquareGoodnessOfFit_PValueMatchesMathNet()
        {
            for (int trial = 0; trial < 300; trial++)
            {
                int k = Rng.Next(2, 8);
                var observed = new double[k];
                var expected = new double[k];
                for (int i = 0; i < k; i++)
                {
                    expected[i] = Rng.NextDouble() * 50 + 5;
                    observed[i] = Math.Max(0, expected[i] + (Rng.NextDouble() - 0.5) * 20);
                }
                var r = HypothesisTests.ChiSquareGoodnessOfFit(observed, expected);
                Check.Close(1 - ChiSquared.CDF(r.DegreesOfFreedom, r.Statistic), r.PValue, 1e-9, $"trial {trial}");
            }
        }

        public void ChiSquareIndependence_PValueMatchesMathNet()
        {
            for (int trial = 0; trial < 200; trial++)
            {
                int rows = Rng.Next(2, 5), cols = Rng.Next(2, 5);
                var table = new double[rows, cols];
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < cols; j++)
                        table[i, j] = Rng.Next(1, 40); // >=1 keeps marginals positive
                var r = HypothesisTests.ChiSquareIndependence(table);
                Check.Close(1 - ChiSquared.CDF(r.DegreesOfFreedom, r.Statistic), r.PValue, 1e-9, $"trial {trial}");
            }
        }
    
        public void FTestEqualVariances_MatchesFisherSnedecor()
        {
            var rng = new ToolBelt.Numerics.DeterministicRandom(31415);
            for (int trial = 0; trial < 300; trial++)
            {
                int na = rng.Next(2, 40), nb = rng.Next(2, 40);
                var a = new double[na];
                var b = new double[nb];
                for (int i = 0; i < na; i++) a[i] = rng.NextDouble() * 10;
                for (int i = 0; i < nb; i++) b[i] = rng.NextDouble() * (1 + trial % 7);
                var r = HypothesisTests.FTestEqualVariances(a, b);
                double cdf = MathNet.Numerics.Distributions.FisherSnedecor.CDF(na - 1, nb - 1, r.Statistic);
                Check.Close(Math.Min(1, 2 * Math.Min(cdf, 1 - cdf)), r.PValue, 1e-10, $"trial {trial}");
            }
        }

        public void ForMeanT_MatchesStudentTQuantile()
        {
            var rng = new ToolBelt.Numerics.DeterministicRandom(27182);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = rng.Next(2, 60);
                var x = new double[n];
                for (int i = 0; i < n; i++) x[i] = rng.NextDouble() * 5;
                double conf = 0.5 + rng.NextDouble() * 0.49;
                var (lo, hi) = ConfidenceInterval.ForMeanT(x, conf);
                double mean = MathNet.Numerics.Statistics.Statistics.Mean(x);
                double se = MathNet.Numerics.Statistics.Statistics.StandardDeviation(x) / Math.Sqrt(n);
                double t = MathNet.Numerics.Distributions.StudentT.InvCDF(0, 1, n - 1, (1 + conf) / 2);
                Check.Close(mean - t * se, lo, 1e-8 * Math.Max(1, Math.Abs(lo)), $"lower trial {trial}");
                Check.Close(mean + t * se, hi, 1e-8 * Math.Max(1, Math.Abs(hi)), $"upper trial {trial}");
            }
        }
    }
}
