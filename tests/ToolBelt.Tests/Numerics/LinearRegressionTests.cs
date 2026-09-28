using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class LinearRegressionTests
    {
        public void PerfectLine_RecoversSlopeInterceptAndR2()
        {
            var xs = new double[] { 0, 1, 2, 3, 4 };
            var ys = xs.Select(x => 2 * x + 3).ToArray();
            var fit = LinearRegression.Fit(xs, ys);
            Check.Close(2, fit.Slope, 1e-9);
            Check.Close(3, fit.Intercept, 1e-9);
            Check.Close(1, fit.RSquared, 1e-9);
            Check.Close(13, fit.Predict(5), 1e-9);
        }

        public void ConstantY_SlopeZero_PerfectFit()
        {
            var fit = LinearRegression.Fit(new double[] { 1, 2, 3 }, new double[] { 5, 5, 5 });
            Check.Close(0, fit.Slope, 1e-9);
            Check.Close(5, fit.Intercept, 1e-9);
            Check.Close(1, fit.RSquared, 1e-9);
        }

        public void KnownDataset()
        {
            // Classic example: xs=1..4, ys=2,4,5,4 -> slope 0.7, intercept 2.
            var fit = LinearRegression.Fit(new double[] { 1, 2, 3, 4 }, new double[] { 2, 4, 5, 4 });
            Check.Close(0.7, fit.Slope, 1e-9);
            Check.Close(2.0, fit.Intercept, 1e-9);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => LinearRegression.Fit(new double[] { 1 }, new double[] { 1 }));       // n < 2
            Check.Throws<ArgumentException>(() => LinearRegression.Fit(new double[] { 1, 2 }, new double[] { 1 }));    // length mismatch
            Check.Throws<ArgumentException>(() => LinearRegression.Fit(new double[] { 3, 3, 3 }, new double[] { 1, 2, 3 })); // constant x
            Check.Throws<ArgumentNullException>(() => LinearRegression.Fit(null!, new double[] { 1 }));
        }

        // Property: fitting a line built as y = a*x + b recovers a and b, over random parameters.
        public void Property_RecoversRandomLines()
        {
            var rng = new Random(2718);
            for (int trial = 0; trial < 1000; trial++)
            {
                double a = rng.NextDouble() * 20 - 10;
                double b = rng.NextDouble() * 20 - 10;
                int n = rng.Next(2, 50);
                var xs = new double[n];
                var ys = new double[n];
                for (int i = 0; i < n; i++)
                {
                    xs[i] = rng.NextDouble() * 100 - 50;
                    ys[i] = a * xs[i] + b;
                }
                // Skip degenerate draws where all x happened to coincide.
                if (xs.Distinct().Count() < 2) continue;

                var fit = LinearRegression.Fit(xs, ys);
                Check.Close(a, fit.Slope, 1e-6, $"trial {trial}: slope");
                Check.Close(b, fit.Intercept, 1e-6, $"trial {trial}: intercept");
                Check.Close(1, fit.RSquared, 1e-6, $"trial {trial}: R^2");
            }
        }
    }
}
