using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class AnovaTests
    {
        public void OneWay_KnownDecomposition()
        {
            // Three groups, known sums of squares.
            // g1={1,2,3} mean 2; g2={4,5,6} mean 5; g3={7,8,9} mean 8; grand mean 5.
            var r = Anova.OneWay(
                new double[] { 1, 2, 3 },
                new double[] { 4, 5, 6 },
                new double[] { 7, 8, 9 });

            Check.Equal(2.0, r.DfBetween);
            Check.Equal(6.0, r.DfWithin);
            // SSB = 3*((2-5)^2 + (5-5)^2 + (8-5)^2) = 3*(9+0+9) = 54.
            Check.Close(54.0, r.SumSquaresBetween, 1e-9);
            // SSW = each group has {-1,0,1} residuals -> 2 per group, *3 = 6.
            Check.Close(6.0, r.SumSquaresWithin, 1e-9);
            Check.Close(27.0, r.MeanSquareBetween, 1e-9);   // 54/2
            Check.Close(1.0, r.MeanSquareWithin, 1e-9);      // 6/6
            Check.Close(27.0, r.FStatistic, 1e-9);
            Check.Close(5.0, r.GrandMean, 1e-12);
            Check.True(r.PValue > 0 && r.PValue < 0.01, $"p={r.PValue}");
        }

        public void OneWay_IdenticalGroups_NoEffect()
        {
            var r = Anova.OneWay(
                new double[] { 5, 6, 7 },
                new double[] { 5, 6, 7 },
                new double[] { 5, 6, 7 });
            Check.Close(0.0, r.FStatistic, 1e-9);
            Check.Close(1.0, r.PValue, 1e-9);
        }

        public void OneWay_ZeroWithinVariance_PerfectSeparation()
        {
            // No within-group noise but different means -> F = +inf, p = 0.
            var r = Anova.OneWay(
                new double[] { 1, 1, 1 },
                new double[] { 2, 2, 2 });
            Check.True(double.IsPositiveInfinity(r.FStatistic), "F is +inf");
            Check.Close(0.0, r.PValue, 1e-12);
        }

        public void FUpperTail_MonotoneAndBounds()
        {
            // Survival function is in [0,1], equals 1 at/below 0, and decreases in f.
            Check.Close(1.0, Anova.FUpperTailProbability(0.0, 3, 10), 1e-12);
            double p1 = Anova.FUpperTailProbability(1.0, 3, 10);
            double p5 = Anova.FUpperTailProbability(5.0, 3, 10);
            Check.True(p1 > p5, $"decreasing: p(1)={p1}, p(5)={p5}");
            Check.True(p5 > 0 && p1 < 1, "strictly inside (0,1)");
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Anova.OneWay(null!));
            Check.Throws<ArgumentException>(() => Anova.OneWay(new double[] { 1, 2, 3 })); // one group
            Check.Throws<ArgumentException>(() => Anova.OneWay(new double[] { 1 }, new double[0])); // empty group
            Check.Throws<ArgumentException>(() => Anova.OneWay(new double[] { 1 }, new double[] { 2 })); // total == groups
            Check.Throws<ArgumentOutOfRangeException>(() => Anova.FUpperTailProbability(1.0, 0, 5));
        }

        public void OneWay_DifferentialVsDirectFormula()
        {
            var rng = new DeterministicRandom(31415926);
            for (int trial = 0; trial < 400; trial++)
            {
                int k = rng.Next(2, 6);
                var groups = new double[k][];
                for (int i = 0; i < k; i++)
                {
                    int n = rng.Next(2, 8);
                    groups[i] = new double[n];
                    double center = rng.Next(0, 5);
                    for (int j = 0; j < n; j++)
                        groups[i][j] = center + (rng.NextDouble() - 0.5) * 6;
                }

                var r = Anova.OneWay(groups);
                var (f, dfB, dfW) = DirectF(groups);
                Check.Close(dfB, r.DfBetween, 1e-12, $"trial {trial} dfB");
                Check.Close(dfW, r.DfWithin, 1e-12, $"trial {trial} dfW");
                Check.Close(f, r.FStatistic, 1e-9, $"trial {trial} F");
                // p-value must match the F-distribution tail at the computed statistic.
                Check.Close(Anova.FUpperTailProbability(r.FStatistic, dfB, dfW), r.PValue, 1e-12, $"trial {trial} p");
            }
        }

        // Independent F computation from first principles.
        private static (double f, double dfB, double dfW) DirectF(double[][] groups)
        {
            int k = groups.Length, total = 0;
            double grand = 0;
            foreach (var g in groups) { total += g.Length; foreach (var v in g) grand += v; }
            grand /= total;

            double ssb = 0, ssw = 0;
            foreach (var g in groups)
            {
                double gm = 0;
                foreach (var v in g) gm += v;
                gm /= g.Length;
                ssb += g.Length * (gm - grand) * (gm - grand);
                foreach (var v in g) ssw += (v - gm) * (v - gm);
            }
            double dfB = k - 1, dfW = total - k;
            return ((ssb / dfB) / (ssw / dfW), dfB, dfW);
        }
    }
}
