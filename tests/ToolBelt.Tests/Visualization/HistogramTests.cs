using System;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class HistogramTests
    {
        public void Bin_ExactCountsAndEdges()
        {
            var (edges, counts) = Histogram.Bin(new double[] { 1, 2, 2, 3, 3, 3 }, 3);
            Check.Equal(4, edges.Length);
            Check.Close(1.0, edges[0], 1e-12);
            Check.Close(3.0, edges[3], 1e-12);
            Check.Equal(3, counts.Length);
            Check.Equal(1, counts[0]); // the single 1
            Check.Equal(2, counts[1]); // the two 2s
            Check.Equal(3, counts[2]); // the three 3s (max lands in last bin)
        }

        public void Bin_ExplicitRange_UniformFill()
        {
            var (_, counts) = Histogram.Bin(new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 5, min: 0, max: 10);
            foreach (int c in counts) Check.Equal(2, c);
        }

        public void Bin_IgnoresNanAndOutOfRange()
        {
            var (_, counts) = Histogram.Bin(new double[] { 0, 5, double.NaN, 20, 9 }, 2, min: 0, max: 10);
            // 0 and 5 -> bin 0/1; 9 -> bin1; NaN and 20 ignored.
            Check.Equal(1, counts[0]); // 0
            Check.Equal(2, counts[1]); // 5 and 9
        }

        public void Bin_DegenerateRangeDoesNotThrow()
        {
            var (edges, counts) = Histogram.Bin(new double[] { 7, 7, 7 }, 4);
            Check.True(edges[edges.Length - 1] > edges[0], "range expanded around the constant value");
            int total = 0;
            foreach (int c in counts) total += c;
            Check.Equal(3, total);
        }

        public void Render_TallBinReachesBottom()
        {
            var img = Histogram.Render(50, 50, new double[] { 1, 1, 1, 1 }, bins: 1,
                new HistogramOptions { DrawFrame = false, Margin = 5, BarColor = Rgba.Red });
            // Single full-height bar spanning the plot area; the bottom-center pixel is the bar.
            Check.Equal(Rgba.Red, img.GetPixel(25, 44)); // bottom = 50-1-5 = 44
        }

        public void Render_EmptyBinStaysBackground()
        {
            var img = Histogram.Render(60, 40, new double[] { 0, 0, 0 }, bins: 3,
                new HistogramOptions { DrawFrame = false, Margin = 5, Background = Rgba.White, Min = 0, Max = 3 });
            // Data all in bin 0; the far-right bin is empty -> background near the top-right.
            Check.Equal(Rgba.White, img.GetPixel(52, 10));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Histogram.Bin(null!, 3));
            Check.Throws<ArgumentOutOfRangeException>(() => Histogram.Bin(new double[] { 1 }, 0));
            Check.Throws<ArgumentException>(() => Histogram.Render(10, 10, new double[] { 1 }, 5,
                new HistogramOptions { Margin = 20 }));
        }
    }
}
