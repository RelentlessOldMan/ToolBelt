using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class CorrelationTests
    {
        public void PerfectPositive_IsOne()
        {
            var xs = new double[] { 1, 2, 3, 4, 5 };
            var ys = xs.Select(x => 3 * x + 2).ToArray();
            Check.Close(1, Correlation.Pearson(xs, ys), 1e-9);
        }

        public void PerfectNegative_IsMinusOne()
        {
            var xs = new double[] { 1, 2, 3, 4, 5 };
            var ys = xs.Select(x => -2 * x + 1).ToArray();
            Check.Close(-1, Correlation.Pearson(xs, ys), 1e-9);
        }

        public void ConstantVariable_IsNaN()
        {
            Check.True(double.IsNaN(Correlation.Pearson(new double[] { 1, 1, 1 }, new double[] { 1, 2, 3 })));
        }

        public void KnownValue()
        {
            // xs=1..5, ys={2,4,5,4,5}: Sxy=6, Sxx=10, Syy=6 -> r = 6/sqrt(60) = 0.7745967.
            var r = Correlation.Pearson(new double[] { 1, 2, 3, 4, 5 }, new double[] { 2, 4, 5, 4, 5 });
            Check.Close(0.7745967, r, 1e-6);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => Correlation.Pearson(new double[] { 1 }, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => Correlation.Pearson(new double[] { 1, 2 }, new double[] { 1 }));
            Check.Throws<ArgumentNullException>(() => Correlation.Pearson(null!, new double[] { 1 }));
        }

        // Properties: r in [-1,1], symmetry, self-correlation is 1, and positive-scale/shift invariance.
        public void Properties_OverRandomData()
        {
            var rng = new Random(314);
            for (int trial = 0; trial < 1000; trial++)
            {
                int n = rng.Next(2, 60);
                var xs = new double[n];
                var ys = new double[n];
                for (int i = 0; i < n; i++)
                {
                    xs[i] = rng.NextDouble() * 100 - 50;
                    ys[i] = rng.NextDouble() * 100 - 50;
                }
                if (xs.Distinct().Count() < 2 || ys.Distinct().Count() < 2) continue;

                double r = Correlation.Pearson(xs, ys);
                Check.True(r >= -1 - 1e-9 && r <= 1 + 1e-9, $"trial {trial}: {r} out of range");
                Check.Close(r, Correlation.Pearson(ys, xs), 1e-9, $"trial {trial}: symmetry");
                Check.Close(1, Correlation.Pearson(xs, xs), 1e-9, $"trial {trial}: self");

                // r is invariant under a positive affine transform of one variable.
                var scaled = xs.Select(x => 5 * x + 7).ToArray();
                Check.Close(r, Correlation.Pearson(scaled, ys), 1e-9, $"trial {trial}: affine invariance");
            }
        }
    }
}
