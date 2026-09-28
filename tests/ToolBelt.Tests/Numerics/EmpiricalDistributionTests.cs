using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class EmpiricalDistributionTests
    {
        public void Cdf()
        {
            var ecdf = new EmpiricalDistribution(Enumerable.Range(1, 10).Select(i => (double)i));
            Check.Close(0.5, ecdf.Cdf(5), 1e-9);   // 5 of 10 are <= 5
            Check.Close(1.0, ecdf.Cdf(10), 1e-9);
            Check.Close(0.0, ecdf.Cdf(0), 1e-9);
            Check.Close(1.0, ecdf.Cdf(100), 1e-9);
            Check.Close(0.3, ecdf.Cdf(3), 1e-9);
        }

        public void Quantile()
        {
            var ecdf = new EmpiricalDistribution(Enumerable.Range(1, 10).Select(i => (double)i));
            Check.Close(5.5, ecdf.Quantile(0.5), 1e-9);  // median of 1..10
            Check.Close(1, ecdf.Quantile(0), 1e-9);
            Check.Close(10, ecdf.Quantile(1), 1e-9);
        }

        public void MinMaxAndCount()
        {
            var ecdf = new EmpiricalDistribution(new double[] { 3, 1, 4, 1, 5 });
            Check.Equal(5, ecdf.Count);
            Check.Equal(1.0, ecdf.Min);
            Check.Equal(5.0, ecdf.Max);
        }

        public void UnsortedInputHandled()
        {
            var ecdf = new EmpiricalDistribution(new double[] { 9, 2, 7, 1 });
            Check.Close(0.5, ecdf.Cdf(2), 1e-9); // 1 and 2 are <= 2
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new EmpiricalDistribution(Array.Empty<double>()));
            Check.Throws<ArgumentNullException>(() => new EmpiricalDistribution(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => new EmpiricalDistribution(new double[] { 1 }).Quantile(1.5));
        }

        // Cdf is monotonic non-decreasing across the sample range.
        public void Property_CdfMonotonic()
        {
            var rng = new Random(44);
            var samples = Enumerable.Range(0, 200).Select(_ => rng.NextDouble() * 100).ToArray();
            var ecdf = new EmpiricalDistribution(samples);
            double prev = -1;
            for (double v = -10; v <= 110; v += 0.5)
            {
                double c = ecdf.Cdf(v);
                Check.True(c >= prev - 1e-12 && c >= 0 && c <= 1, $"cdf not monotonic/in-range at {v}");
                prev = c;
            }
        }
    }
}
