using System;
using MathNet.Numerics.Statistics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using MnCorrelation = MathNet.Numerics.Statistics.Correlation;
using TbCorrelation = ToolBelt.Numerics.Correlation;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's Pearson correlation and percentile against Math.NET. ToolBelt's percentile uses
    /// linear interpolation between closest ranks — the R-7 / Excel (PERCENTILE.INC) definition — so it is
    /// compared against Math.NET's matching <see cref="QuantileDefinition.Excel"/>.
    /// </summary>
    public sealed class StatisticsCrossCheckTests
    {
        public void Pearson_MatchesMathNet()
        {
            var rng = new Random(30);
            for (int t = 0; t < 200; t++)
            {
                int n = rng.Next(3, 50);
                var x = new double[n];
                var y = new double[n];
                for (int i = 0; i < n; i++)
                {
                    x[i] = rng.NextDouble() * 20 - 10;
                    y[i] = 0.7 * x[i] + (rng.NextDouble() * 4 - 2); // correlated + noise
                }
                Check.Close(MnCorrelation.Pearson(x, y), TbCorrelation.Pearson(x, y), 1e-9, $"t{t} n={n}");
            }
        }

        public void Percentile_MatchesExcelDefinition()
        {
            var rng = new Random(31);
            for (int t = 0; t < 200; t++)
            {
                int n = rng.Next(2, 40);
                var data = new double[n];
                for (int i = 0; i < n; i++) data[i] = rng.NextDouble() * 100;

                foreach (double p in new[] { 0.0, 5, 10, 25, 50, 75, 90, 95, 100 })
                {
                    double theirs = data.QuantileCustom(p / 100.0, QuantileDefinition.Excel);
                    double mine = Percentile.Compute(data, p);
                    Check.Close(theirs, mine, 1e-9, $"t{t} n={n} p={p}");
                }
            }
        }
    }
}
