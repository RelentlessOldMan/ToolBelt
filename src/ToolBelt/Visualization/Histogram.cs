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
        /// NaN values and values outside the range are ignored; infinities never set an automatic range. Explicit
        /// bounds must be finite with <paramref name="min"/> ≤ <paramref name="max"/>.
        /// </summary>
        public static (double[] Edges, int[] Counts) Bin(
            IReadOnlyList<double> data, int bins, double? min = null, double? max = null)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (bins < 1 || bins == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(bins), bins, "Need between 1 and int.MaxValue - 1 bins.");

            if (min.HasValue && (double.IsNaN(min.Value) || double.IsInfinity(min.Value)))
                throw new ArgumentOutOfRangeException(nameof(min), min, "Min must be finite.");
            if (max.HasValue && (double.IsNaN(max.Value) || double.IsInfinity(max.Value)))
                throw new ArgumentOutOfRangeException(nameof(max), max, "Max must be finite.");
            if (min > max) throw new ArgumentException("Min must not exceed Max.", nameof(min));

            double lo = min ?? double.PositiveInfinity, hi = max ?? double.NegativeInfinity;
            if (min is null || max is null)
            {
                foreach (double v in data)
                {
                    // ±∞ cannot set a range: one ∞ used to discard every finite value for [0, 1].
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    if (min is null && v < lo) lo = v;
                    if (max is null && v > hi) hi = v;
                }
            }
            if (double.IsInfinity(lo) || double.IsInfinity(hi)) // no finite data on an auto side
            {
                if (double.IsInfinity(lo) && double.IsInfinity(hi)) { lo = 0; hi = 1; }
                else if (double.IsInfinity(lo)) lo = hi - 1;
                else hi = lo + 1;
            }
            if (hi <= lo)
            {
                // One explicit bound at or past all the data stays, and the other side opens beyond it: centring moved
                // min = 5 to 4.5 and counted 4.7 and 4.8, both below it.
                if (min.HasValue && !max.HasValue) hi = Math.Min(double.MaxValue, lo + Math.Max(1, Math.Abs(lo) * 0.1));
                else if (max.HasValue && !min.HasValue) lo = Math.Max(-double.MaxValue, hi - Math.Max(1, Math.Abs(hi) * 0.1));
                else { hi = lo + 0.5; lo -= 0.5; }                                    // degenerate range
            }

            // Scaled by bins first so ±1e308 ranges do not overflow.
            double width = hi / bins - lo / bins;
            var edges = new double[bins + 1];
            edges[0] = lo; // lo + 0·width was NaN for one bin over ±1e308 (width ∞)
            // lo + span·i/bins puts decimal edges where expected (lo + i·width made the 0.3 edge 0.30000000000000004, so
            // 0.3 fell in the bin below); i·width only when span·bins overflows (ranges near ±double.MaxValue).
            double span = hi - lo;
            bool exact = !double.IsInfinity(span * bins);
            for (int i = 1; i < bins; i++)
                edges[i] = exact ? lo + span * i / bins : lo + i * width;
            edges[bins] = hi;

            var counts = new int[bins];
            foreach (double v in data)
            {
                if (double.IsNaN(v) || v < lo || v > hi) continue;
                // A NaN quotient (width 0) cast to int.MinValue and indexed below the array; start at 0 and settle.
                double q = Math.Floor(v / width - lo / width);
                int idx = double.IsNaN(q) ? 0 : (int)Math.Min(bins - 1, Math.Max(0, q));
                // Settle against the returned edges, so a value equal to edge[i] is always in bin i (the
                // quotient alone disagreed with the edges for values on them).
                while (idx > 0 && v < edges[idx]) idx--;
                while (idx < bins - 1 && v >= edges[idx + 1]) idx++;
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
            // A negative margin put the plot area outside the image (SetPixel threw "x", or int.MinValue overflowed).
            if (m < 0) throw new ArgumentOutOfRangeException(nameof(options), m, "Margin must not be negative.");
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
