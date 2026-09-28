using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class HistogramTests
    {
        public void BasicBinning()
        {
            var h = new Histogram(0, 10, 5); // bins of width 2: [0,2)[2,4)[4,6)[6,8)[8,10)
            h.AddRange(new double[] { 0, 1, 2, 3, 9 });
            Check.True(h.Bins.SequenceEqual(new long[] { 2, 2, 0, 0, 1 }));
            Check.Equal(5L, h.Total);
        }

        public void UnderflowAndOverflow()
        {
            var h = new Histogram(0, 10, 5);
            h.Add(-1);   // underflow
            h.Add(10);   // overflow (max is exclusive)
            h.Add(100);  // overflow
            Check.Equal(1L, h.Underflow);
            Check.Equal(2L, h.Overflow);
            Check.True(h.Bins.All(b => b == 0));
        }

        public void BinBounds()
        {
            var h = new Histogram(0, 128, 8);
            Check.Close(16, h.BinWidth);
            Check.Close(0, h.BinLowerBound(0));
            Check.Close(112, h.BinLowerBound(7));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new Histogram(10, 10, 5));   // min == max
            Check.Throws<ArgumentException>(() => new Histogram(10, 0, 5));    // min > max
            Check.Throws<ArgumentOutOfRangeException>(() => new Histogram(0, 1, 0));
        }

        // Differential: bin counts must match a reference that finds each value's bin by linear scan.
        // Uses an exact binWidth (128/8 = 16) so the two methods agree at boundaries too.
        public void Differential_MatchesLinearScanReference()
        {
            var rng = new Random(606);
            for (int trial = 0; trial < 500; trial++)
            {
                const double min = 0, max = 128;
                const int binCount = 8;
                double binWidth = (max - min) / binCount;

                var h = new Histogram(min, max, binCount);
                var expected = new long[binCount];
                long under = 0, over = 0;

                int n = rng.Next(0, 300);
                for (int i = 0; i < n; i++)
                {
                    double value = rng.NextDouble() * 160 - 16; // spans under/in/over
                    h.Add(value);

                    if (value < min) { under++; continue; }
                    if (value >= max) { over++; continue; }
                    int bin = 0;
                    while (bin < binCount - 1 && value >= min + (bin + 1) * binWidth)
                        bin++;
                    expected[bin]++;
                }

                Check.True(expected.SequenceEqual(h.Bins), $"trial {trial}: bins");
                Check.Equal(under, h.Underflow, $"trial {trial}: underflow");
                Check.Equal(over, h.Overflow, $"trial {trial}: overflow");
            }
        }
    }
}
