// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;

namespace ToolBelt.Visualization
{
    /// <summary>The five-number summary plus Tukey whiskers and outliers for one dataset.</summary>
    public sealed class BoxStats
    {
        internal BoxStats(double min, double q1, double median, double q3, double max,
            double lowerWhisker, double upperWhisker, double[] outliers)
        {
            Min = min; Q1 = q1; Median = median; Q3 = q3; Max = max;
            LowerWhisker = lowerWhisker; UpperWhisker = upperWhisker; Outliers = outliers;
        }

        /// <summary>The minimum value.</summary>
        public double Min { get; }
        /// <summary>First quartile (25th percentile).</summary>
        public double Q1 { get; }
        /// <summary>The median (50th percentile).</summary>
        public double Median { get; }
        /// <summary>Third quartile (75th percentile).</summary>
        public double Q3 { get; }
        /// <summary>The maximum value.</summary>
        public double Max { get; }
        /// <summary>Lowest value still within the lower Tukey fence, but never above <see cref="Q1"/>.</summary>
        public double LowerWhisker { get; }
        /// <summary>Highest value still within the upper Tukey fence, but never below <see cref="Q3"/>.</summary>
        public double UpperWhisker { get; }
        /// <summary>Values beyond the fences.</summary>
        public double[] Outliers { get; }

        /// <summary>Interquartile range, <c>Q3 − Q1</c>.</summary>
        public double Iqr => Q3 - Q1;
    }

    /// <summary>Options for <see cref="BoxPlot.Render"/>.</summary>
    public sealed class BoxPlotOptions
    {
        /// <summary>Canvas background.</summary>
        public Rgba Background { get; set; } = Rgba.White;
        /// <summary>Box outline and whisker color.</summary>
        public Rgba BoxColor { get; set; } = new Rgba(70, 130, 180);
        /// <summary>Median line color.</summary>
        public Rgba MedianColor { get; set; } = new Rgba(220, 50, 47);
        /// <summary>Outlier marker color.</summary>
        public Rgba OutlierColor { get; set; } = new Rgba(120, 120, 120);
        /// <summary>Frame color.</summary>
        public Rgba FrameColor { get; set; } = Rgba.Black;
        /// <summary>Pixels of margin.</summary>
        public int Margin { get; set; } = 12;
        /// <summary>Whether to draw the plot frame.</summary>
        public bool DrawFrame { get; set; } = true;
        /// <summary>Explicit value-axis minimum (else derived from the data).</summary>
        public double? Min { get; set; }
        /// <summary>Explicit value-axis maximum (else derived from the data).</summary>
        public double? Max { get; set; }
        /// <summary>Whisker reach as a multiple of the IQR (Tukey's 1.5 by default).</summary>
        public double WhiskerReach { get; set; } = 1.5;
    }

    /// <summary>
    /// Computes box-and-whisker statistics and renders one or more datasets side by side. Quartiles use
    /// linear interpolation between ranks (the R-7 / Excel convention); whiskers extend to the most extreme
    /// values still inside Q1−k·IQR … Q3+k·IQR, and anything past that is drawn as an outlier point.
    /// </summary>
    public static class BoxPlot
    {
        /// <summary>Computes the box statistics for one dataset. NaN values are ignored.</summary>
        public static BoxStats Compute(IReadOnlyList<double> data, double whiskerReach = 1.5)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count == 0) throw new ArgumentException("Dataset must be non-empty.", nameof(data));
            if (!(whiskerReach >= 0)) throw new ArgumentOutOfRangeException(nameof(whiskerReach), whiskerReach, "Whisker reach must be non-negative.");

            // NaN has no rank: Array.Sort put it first, so it became the minimum and shifted every quartile.
            var kept = new List<double>(data.Count);
            for (int i = 0; i < data.Count; i++) if (!double.IsNaN(data[i])) kept.Add(data[i]);
            if (kept.Count == 0) throw new ArgumentException("Dataset has no non-NaN values.", nameof(data));
            var sorted = kept.ToArray();
            Array.Sort(sorted);

            double q1 = Percentile(sorted, 25), median = Percentile(sorted, 50), q3 = Percentile(sorted, 75);
            double iqr = q3 - q1;
            double lowerFence = q1 - whiskerReach * iqr, upperFence = q3 + whiskerReach * iqr;

            double lowerWhisker = double.PositiveInfinity, upperWhisker = double.NegativeInfinity;
            var outliers = new List<double>();
            foreach (double v in sorted)
            {
                if (v < lowerFence || v > upperFence) outliers.Add(v);
                else
                {
                    if (v < lowerWhisker) lowerWhisker = v;
                    if (v > upperWhisker) upperWhisker = v;
                }
            }
            // As matplotlib does, a whisker never ends inside the box: with no value between the box and the
            // fence (or every value an outlier) it stops at the quartile.
            lowerWhisker = Math.Min(lowerWhisker, q1);
            upperWhisker = Math.Max(upperWhisker, q3);

            return new BoxStats(sorted[0], q1, median, q3, sorted[sorted.Length - 1],
                lowerWhisker, upperWhisker, outliers.ToArray());
        }

        /// <summary>Renders side-by-side box plots for the given datasets.</summary>
        public static ImageBuffer Render(int width, int height, IReadOnlyList<IReadOnlyList<double>> datasets, BoxPlotOptions? options = null)
        {
            if (datasets is null) throw new ArgumentNullException(nameof(datasets));
            if (datasets.Count == 0) throw new ArgumentException("Provide at least one dataset.", nameof(datasets));
            options ??= new BoxPlotOptions();

            var stats = new BoxStats[datasets.Count];
            for (int i = 0; i < datasets.Count; i++) stats[i] = Compute(datasets[i], options.WhiskerReach);

            var image = new ImageBuffer(width, height, options.Background);
            int m = options.Margin;
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.");
            if (options.DrawFrame)
                image.DrawRectangle(left, top, right - left + 1, bottom - top + 1, options.FrameColor);

            double minV = options.Min ?? double.PositiveInfinity, maxV = options.Max ?? double.NegativeInfinity;
            if (options.Min is null || options.Max is null)
            {
                foreach (var s in stats)
                    foreach (double v in new[] { s.Min, s.LowerWhisker, s.Q1, s.Q3, s.UpperWhisker, s.Max })
                    {
                        if (double.IsInfinity(v)) continue; // an ∞ outlier cannot set the axis
                        if (options.Min is null && v < minV) minV = v;
                        if (options.Max is null && v > maxV) maxV = v;
                    }
            }
            double range = maxV - minV;
            int plotHeight = bottom - top;
            // Doubles, clipped when drawn: a value outside an explicit Min/Max overflowed the int cast.
            double MapY(double v) => range > 0 ? bottom - (v - minV) / range * plotHeight : (top + bottom) / 2;

            int plotWidth = right - left;
            double slot = (double)plotWidth / datasets.Count;
            int halfBox = Math.Max(2, (int)(slot * 0.3));

            void Line(double x0, double y0, double x1, double y1, Rgba c)
                => image.DrawLineClipped(x0, y0, x1, y1, c, left, top, right, bottom);

            for (int i = 0; i < stats.Length; i++)
            {
                var s = stats[i];
                int cx = left + (int)Math.Round((i + 0.5) * slot);
                double q1y = MapY(s.Q1), q3y = MapY(s.Q3), medY = MapY(s.Median);
                double loY = MapY(s.LowerWhisker), hiY = MapY(s.UpperWhisker);

                // Box (Q1..Q3).
                Line(cx - halfBox, q1y, cx + halfBox, q1y, options.BoxColor);
                Line(cx - halfBox, q3y, cx + halfBox, q3y, options.BoxColor);
                Line(cx - halfBox, q1y, cx - halfBox, q3y, options.BoxColor);
                Line(cx + halfBox, q1y, cx + halfBox, q3y, options.BoxColor);
                // Median line.
                Line(cx - halfBox, medY, cx + halfBox, medY, options.MedianColor);
                // Whiskers + caps.
                Line(cx, q3y, cx, hiY, options.BoxColor);
                Line(cx, q1y, cx, loY, options.BoxColor);
                Line(cx - halfBox / 2, hiY, cx + halfBox / 2, hiY, options.BoxColor);
                Line(cx - halfBox / 2, loY, cx + halfBox / 2, loY, options.BoxColor);
                // Outliers.
                foreach (double o in s.Outliers)
                {
                    double oy = MapY(o);
                    if (oy >= top && oy <= bottom)
                        image.DrawRectangle(cx - 1, (int)Math.Round(oy) - 1, 3, 3, options.OutlierColor, filled: true);
                }
            }
            return image;
        }

        // R-7 / Excel linear-interpolation percentile on already-sorted data.
        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 1) return sorted[0];
            double rank = p / 100.0 * (sorted.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            double a = sorted[lo], b = sorted[hi], f = rank - lo;
            // Weighted, not a + f·(b − a): the difference was ∞ − ∞ = NaN between infinities and overflowed for ±1e308.
            if (lo == hi || a == b) return a;
            return (1 - f) * a + f * b;
        }
    }
}
