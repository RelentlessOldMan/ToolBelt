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
        /// <summary>Lowest value still within the lower Tukey fence.</summary>
        public double LowerWhisker { get; }
        /// <summary>Highest value still within the upper Tukey fence.</summary>
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
        /// <summary>Computes the box statistics for one dataset.</summary>
        public static BoxStats Compute(IReadOnlyList<double> data, double whiskerReach = 1.5)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Count == 0) throw new ArgumentException("Dataset must be non-empty.", nameof(data));
            if (whiskerReach < 0) throw new ArgumentOutOfRangeException(nameof(whiskerReach), whiskerReach, "Whisker reach must be non-negative.");

            var sorted = new double[data.Count];
            for (int i = 0; i < data.Count; i++) sorted[i] = data[i];
            Array.Sort(sorted);

            double q1 = Percentile(sorted, 25), median = Percentile(sorted, 50), q3 = Percentile(sorted, 75);
            double iqr = q3 - q1;
            double lowerFence = q1 - whiskerReach * iqr, upperFence = q3 + whiskerReach * iqr;

            double lowerWhisker = sorted[sorted.Length - 1], upperWhisker = sorted[0];
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
            if (outliers.Count == sorted.Length) { lowerWhisker = sorted[0]; upperWhisker = sorted[sorted.Length - 1]; }

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
                {
                    if (options.Min is null) minV = Math.Min(minV, s.Min);
                    if (options.Max is null) maxV = Math.Max(maxV, s.Max);
                }
            }
            double range = maxV - minV;
            int plotHeight = bottom - top;
            int MapY(double v) => range > 0 ? bottom - (int)Math.Round((v - minV) / range * plotHeight) : (top + bottom) / 2;

            int plotWidth = right - left;
            double slot = (double)plotWidth / datasets.Count;
            int halfBox = Math.Max(2, (int)(slot * 0.3));

            for (int i = 0; i < stats.Length; i++)
            {
                var s = stats[i];
                int cx = left + (int)Math.Round((i + 0.5) * slot);
                int q1y = MapY(s.Q1), q3y = MapY(s.Q3), medY = MapY(s.Median);
                int loY = MapY(s.LowerWhisker), hiY = MapY(s.UpperWhisker);

                // Box (Q1..Q3).
                int boxTop = Math.Min(q1y, q3y);
                image.DrawRectangle(cx - halfBox, boxTop, halfBox * 2 + 1, Math.Abs(q1y - q3y) + 1, options.BoxColor);
                // Median line.
                image.DrawLine(cx - halfBox, medY, cx + halfBox, medY, options.MedianColor);
                // Whiskers + caps.
                image.DrawLine(cx, q3y, cx, hiY, options.BoxColor);
                image.DrawLine(cx, q1y, cx, loY, options.BoxColor);
                image.DrawLine(cx - halfBox / 2, hiY, cx + halfBox / 2, hiY, options.BoxColor);
                image.DrawLine(cx - halfBox / 2, loY, cx + halfBox / 2, loY, options.BoxColor);
                // Outliers.
                foreach (double o in s.Outliers)
                {
                    int oy = MapY(o);
                    image.DrawRectangle(cx - 1, oy - 1, 3, 3, options.OutlierColor, filled: true);
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
            if (lo == hi) return sorted[lo];
            return sorted[lo] + (rank - lo) * (sorted[hi] - sorted[lo]);
        }
    }
}
