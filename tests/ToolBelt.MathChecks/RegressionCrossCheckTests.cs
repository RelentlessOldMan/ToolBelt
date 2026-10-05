using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.MathChecks
{
    /// <summary>Multiple regression coefficients vs Math.NET's QR regression.</summary>
    public sealed class RegressionCrossCheckTests
    {
        public void Coefficients_MatchMathNet()
        {
            var rng = new DeterministicRandom(4242);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = rng.Next(8, 60), k = rng.Next(1, 5);
                var x = new double[n, k];
                var rows = new double[n][];
                var y = new double[n];
                for (int i = 0; i < n; i++)
                {
                    rows[i] = new double[k];
                    double v = rng.NextDouble();
                    for (int j = 0; j < k; j++) { x[i, j] = rows[i][j] = rng.NextDouble() * 10 - 5; v += (j + 1) * x[i, j]; }
                    y[i] = v + rng.NextGaussian();
                }
                bool intercept = trial % 3 != 0;
                double[] theirs = MathNet.Numerics.LinearRegression.MultipleRegression.QR(rows, y, intercept);
                var ours = MultipleRegression.Fit(x, y, intercept).Coefficients;
                for (int j = 0; j < theirs.Length; j++)
                    Check.Close(theirs[j], ours[j], 1e-9 * Math.Max(1, Math.Abs(theirs[j])), $"trial {trial} coefficient {j}");
            }
        }
    }
}
