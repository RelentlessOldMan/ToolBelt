using System;
using MathNet.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using TbPolynomial = ToolBelt.Numerics.Polynomial;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's least-squares fits — polynomial (via normal equations) and simple linear
    /// regression — against Math.NET's <c>Fit</c> helpers.
    /// </summary>
    public sealed class FitCrossCheckTests
    {
        public void PolynomialFit_MatchesMathNet()
        {
            var rng = new Random(40);
            for (int t = 0; t < 100; t++)
            {
                int degree = rng.Next(1, 4);
                int n = rng.Next(degree + 2, 30);
                var x = new double[n];
                var y = new double[n];
                for (int i = 0; i < n; i++)
                {
                    x[i] = rng.NextDouble() * 6 - 3;
                    y[i] = 0;
                    for (int d = 0; d <= degree; d++) y[i] += (rng.NextDouble() - 0.5) * Math.Pow(x[i], d);
                    y[i] += (rng.NextDouble() - 0.5) * 0.1; // noise
                }

                var mine = TbPolynomial.Fit(x, y, degree);
                double[] theirs = Fit.Polynomial(x, y, degree); // ascending coefficients

                for (int d = 0; d <= degree; d++)
                    Check.Close(theirs[d], mine.Coefficients[d], 1e-6, $"t{t} deg{degree} c{d}");
            }
        }

        public void LinearRegression_MatchesMathNet()
        {
            var rng = new Random(41);
            for (int t = 0; t < 200; t++)
            {
                int n = rng.Next(3, 60);
                var x = new double[n];
                var y = new double[n];
                double trueSlope = rng.NextDouble() * 4 - 2, trueIntercept = rng.NextDouble() * 10 - 5;
                for (int i = 0; i < n; i++)
                {
                    x[i] = rng.NextDouble() * 20 - 10;
                    y[i] = trueSlope * x[i] + trueIntercept + (rng.NextDouble() - 0.5) * 2;
                }

                var mine = LinearRegression.Fit(x, y);
                (double intercept, double slope) = Fit.Line(x, y);

                Check.Close(slope, mine.Slope, 1e-8, $"t{t} slope");
                Check.Close(intercept, mine.Intercept, 1e-8, $"t{t} intercept");
            }
        }
    }
}
