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
    }
}
