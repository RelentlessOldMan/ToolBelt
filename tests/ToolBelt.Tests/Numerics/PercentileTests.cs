using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class PercentileTests
    {
        public void KnownValues()
        {
            var data = new double[] { 1, 2, 3, 4, 5 };
            Check.Close(1, Percentile.Compute(data, 0));
            Check.Close(5, Percentile.Compute(data, 100));
            Check.Close(3, Percentile.Compute(data, 50));
            Check.Close(2, Percentile.Compute(data, 25));
            Check.Close(4, Percentile.Compute(data, 75));
        }

        public void Median_EvenCount_Interpolates()
        {
            Check.Close(2.5, Percentile.Median(new double[] { 1, 2, 3, 4 }));
        }

        public void Quartiles()
        {
            var data = new double[] { 1, 2, 3, 4, 5 };
            Check.Close(2, Percentile.Quartile(data, 1));
            Check.Close(3, Percentile.Quartile(data, 2));
            Check.Close(4, Percentile.Quartile(data, 3));
        }

        public void SingleElement()
        {
            Check.Close(42, Percentile.Compute(new double[] { 42 }, 37));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => Percentile.Compute(Array.Empty<double>(), 50));
            Check.Throws<ArgumentOutOfRangeException>(() => Percentile.Compute(new double[] { 1 }, 101));
            Check.Throws<ArgumentOutOfRangeException>(() => Percentile.Quartile(new double[] { 1 }, 4));
        }

        public void DoesNotMutateInput()
        {
            var data = new double[] { 5, 3, 1, 4, 2 };
            var copy = data.ToArray();
            Percentile.Compute(data, 50);
            Check.True(data.SequenceEqual(copy), "input array must be left untouched");
        }

        // Properties: order-independent, within [min,max], monotonic non-decreasing in p, and the median
        // equals the middle element for odd counts.
        public void Properties_OverRandomData()
        {
            var rng = new Random(88);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.Next(1, 60);
                var data = new double[n];
                for (int i = 0; i < n; i++) data[i] = rng.NextDouble() * 1000 - 500;

                double min = data.Min(), max = data.Max();
                double prev = double.NegativeInfinity;
                for (int pct = 0; pct <= 100; pct += 5)
                {
                    double v = Percentile.Compute(data, pct);
                    Check.True(v >= min - 1e-9 && v <= max + 1e-9, $"trial {trial}: {v} outside [{min},{max}]");
                    Check.True(v >= prev - 1e-9, $"trial {trial}: not monotonic at p={pct}");
                    prev = v;
                }

                // Order independence.
                var shuffled = data.OrderBy(_ => rng.Next()).ToArray();
                Check.Close(Percentile.Compute(data, 50), Percentile.Compute(shuffled, 50), 1e-9,
                    $"trial {trial}: order dependence");

                if (n % 2 == 1)
                {
                    var sorted = data.OrderBy(x => x).ToArray();
                    Check.Close(sorted[n / 2], Percentile.Median(data), 1e-9, $"trial {trial}: odd median");
                }
            }
        }
    }
}
