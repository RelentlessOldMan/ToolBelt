// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;

namespace ToolBelt.Visualization
{
    /// <summary>Options for <see cref="Histogram.Render"/>.</summary>
    public sealed class HistogramOptions
    {
        /// <summary>Canvas background.</summary>
        public Rgba Background { get; set; } = Rgba.White;
        /// <summary>Bar fill color.</summary>
        public Rgba BarColor { get; set; } = new Rgba(70, 130, 180); // steel blue
        /// <summary>Frame color.</summary>
        public Rgba FrameColor { get; set; } = Rgba.Black;
        /// <summary>Pixels of margin between the frame and the image edge.</summary>
        public int Margin { get; set; } = 10;
        /// <summary>Whether to draw the plot frame.</summary>
        public bool DrawFrame { get; set; } = true;
        /// <summary>Explicit lower edge of the binning range (else the data minimum).</summary>
        public double? Min { get; set; }
        /// <summary>Explicit upper edge of the binning range (else the data maximum).</summary>
        public double? Max { get; set; }
    }

    /// <summary>
    /// Bins one-dimensional data into equal-width buckets and renders the counts as a bar chart. The
    /// binning is exposed separately via <see cref="Bin"/> so the numbers can be used without drawing.
    /// </summary>
    public static class Histogram
    {
        /// <summary>
        /// Counts <paramref name="data"/> into <paramref name="bins"/> equal-width bins. Returns the bin
        /// edges (length <c>bins+1</c>) and counts (length <c>bins</c>). Values are placed in bin
        /// <c>i</c> when they fall in <c>[edge[i], edge[i+1])</c>; the maximum lands in the last bin.
        /// NaN values and values outside the range are ignored.
        /// </summary>
        public static (double[] Edges, int[] Counts) Bin(
            IReadOnlyList<double> data, int bins, double? min = null, double? max = null)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (bins < 1) throw new ArgumentOutOfRangeException(nameof(bins), bins, "Need at least one bin.");

            double lo = min ?? double.PositiveInfinity, hi = max ?? double.NegativeInfinity;
            if (min is null || max is null)
            {
                foreach (double v in data)
                {
                    if (double.IsNaN(v)) continue;
                    if (min is null && v < lo) lo = v;
                    if (max is null && v > hi) hi = v;
                }
            }
            if (double.IsInfinity(lo) || double.IsInfinity(hi)) { lo = 0; hi = 1; } // no finite data
            if (hi <= lo) { hi = lo + 0.5; lo -= 0.5; }                             // degenerate range

            double width = (hi - lo) / bins;
            var edges = new double[bins + 1];
            for (int i = 0; i <= bins; i++) edges[i] = lo + i * width;

            var counts = new int[bins];
            foreach (double v in data)
            {
                if (double.IsNaN(v) || v < lo || v > hi) continue;
                int idx = (int)((v - lo) / width);
                if (idx < 0) idx = 0;
                if (idx > bins - 1) idx = bins - 1; // v == hi
                counts[idx]++;
            }
            return (edges, counts);
        }

        /// <summary>Bins <paramref name="data"/> and renders it as a bar chart.</summary>
        public static ImageBuffer Render(int width, int height, IReadOnlyList<double> data, int bins = 10, HistogramOptions? options = null)
        {
            options ??= new HistogramOptions();
            var (_, counts) = Bin(data, bins, options.Min, options.Max);
            var image = new ImageBuffer(width, height, options.Background);

            int m = options.Margin;
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.");

            if (options.DrawFrame)
                image.DrawRectangle(left, top, right - left + 1, bottom - top + 1, options.FrameColor);

            int maxCount = 1;
            foreach (int c in counts) if (c > maxCount) maxCount = c;

            int plotWidth = right - left + 1, plotHeight = bottom - top + 1;
            for (int b = 0; b < bins; b++)
            {
                if (counts[b] <= 0) continue;
                int bx0 = left + (int)Math.Round((double)b / bins * plotWidth);
                int bx1 = left + (int)Math.Round((double)(b + 1) / bins * plotWidth);
                int bw = Math.Max(1, bx1 - bx0);
                int barH = (int)Math.Round((double)counts[b] / maxCount * plotHeight);
                if (barH > 0) image.DrawRectangle(bx0, bottom - barH + 1, bw, barH, options.BarColor, filled: true);
            }
            return image;
        }
    }
}
