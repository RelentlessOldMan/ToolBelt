using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using MathNet.Numerics.Distributions;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's F-distribution tail (behind one-way ANOVA) against Math.NET's
    /// <see cref="FisherSnedecor"/>, and cross-checks the ANOVA F p-value against the same distribution at
    /// the computed statistic.
    /// </summary>
    public sealed class AnovaCrossCheckTests
    {
        private static readonly DeterministicRandom Rng = new DeterministicRandom(20262610);

        public void FUpperTail_MatchesFisherSnedecor()
        {
            for (int trial = 0; trial < 500; trial++)
            {
                double df1 = Rng.Next(1, 30);
                double df2 = Rng.Next(2, 40);
                double f = Rng.NextDouble() * 15 + 1e-3;
                double expected = 1.0 - FisherSnedecor.CDF(df1, df2, f); // survival
                Check.Close(expected, Anova.FUpperTailProbability(f, df1, df2), 1e-9, $"trial {trial} f={f}, df=({df1},{df2})");
            }
        }

        public void OneWayAnova_PValueMatchesFisherSnedecor()
        {
            for (int trial = 0; trial < 300; trial++)
            {
                int k = Rng.Next(2, 6);
                var groups = new double[k][];
                for (int i = 0; i < k; i++)
                {
                    int n = Rng.Next(2, 10);
                    groups[i] = new double[n];
                    double center = 5 + (Rng.NextDouble() - 0.5) * 4; // some real between-group effect
                    for (int j = 0; j < n; j++)
                        groups[i][j] = center + (Rng.NextDouble() - 0.5) * 10;
                }

                var r = Anova.OneWay(groups);
                double expected = 1.0 - FisherSnedecor.CDF(r.DfBetween, r.DfWithin, r.FStatistic);
                Check.Close(expected, r.PValue, 1e-9, $"trial {trial}");
            }
        }
    }
}
