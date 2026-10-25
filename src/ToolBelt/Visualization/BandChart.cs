// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;

namespace ToolBelt.Visualization
{
    /// <summary>Options for <see cref="BandChart.Render"/>.</summary>
    public sealed class BandOptions
    {
        /// <summary>Canvas background.</summary>
        public Rgba Background { get; set; } = Rgba.White;
        /// <summary>Fill color of the band between the lower and upper curves.</summary>
        public Rgba BandColor { get; set; } = new Rgba(174, 199, 232); // light steel blue
        /// <summary>Color of the optional center line.</summary>
        public Rgba CenterColor { get; set; } = new Rgba(31, 119, 180);
        /// <summary>Frame color.</summary>
        public Rgba FrameColor { get; set; } = Rgba.Black;
        /// <summary>Pixels of margin.</summary>
        public int Margin { get; set; } = 10;
        /// <summary>Whether to draw the plot frame.</summary>
        public bool DrawFrame { get; set; } = true;
        /// <summary>Explicit value-axis bounds (else derived from the band extents).</summary>
        public double? Min { get; set; }
        /// <summary>Explicit value-axis bounds (else derived from the band extents).</summary>
        public double? Max { get; set; }
    }

    /// <summary>
    /// Fills the region between a lower and an upper curve — a confidence band, min/max envelope or
    /// tolerance corridor — with an optional center line drawn on top. The x-axis is the sample index.
    /// </summary>
    public static class BandChart
    {
        /// <summary>Renders the shaded band between <paramref name="lower"/> and <paramref name="upper"/>, with an optional <paramref name="center"/> line.</summary>
        public static ImageBuffer Render(int width, int height,
            IReadOnlyList<double> lower, IReadOnlyList<double> upper, IReadOnlyList<double>? center = null, BandOptions? options = null)
        {
            if (lower is null) throw new ArgumentNullException(nameof(lower));
            if (upper is null) throw new ArgumentNullException(nameof(upper));
            if (lower.Count != upper.Count) throw new ArgumentException("lower and upper must have the same length.");
            if (lower.Count == 0) throw new ArgumentException("Curves must be non-empty.", nameof(lower));
            if (center != null && center.Count != lower.Count) throw new ArgumentException("center length must match.", nameof(center));
            options ??= new BandOptions();
            // An infinite bound made the range ∞, which put every value on the bottom row.
            if (options.Min is double mn && !IsFinite(mn) || options.Max is double mx && !IsFinite(mx))
                throw new ArgumentOutOfRangeException(nameof(options), "Min and Max must be finite.");

            var image = new ImageBuffer(width, height, options.Background);
            int m = options.Margin;
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.");
            if (options.DrawFrame)
                image.DrawRectangle(left, top, right - left + 1, bottom - top + 1, options.FrameColor);

            int n = lower.Count;
            double minV = options.Min ?? double.PositiveInfinity, maxV = options.Max ?? double.NegativeInfinity;
            if (options.Min is null || options.Max is null)
            {
                // Finite values only: Math.Min/Max propagate NaN, which collapsed every value onto the centre row.
                for (int i = 0; i < n; i++)
                    foreach (double v in new[] { lower[i], upper[i] })
                    {
                        if (!IsFinite(v)) continue;
                        if (options.Min is null && v < minV) minV = v;
                        if (options.Max is null && v > maxV) maxV = v;
                    }
            }
            double range = maxV - minV;
            double MapX(int i) => n > 1 ? left + (double)i / (n - 1) * (right - left) : (left + right) / 2;
            double MapY(double v) => range > 0 ? bottom - Fraction(v, minV, maxV) * (bottom - top) : (top + bottom) / 2;

            // Fill a vertical span at every pixel column, clipped to the plot area (values outside an explicit Min/Max
            // threw from SetPixel), with the curves interpolated between neighbouring samples: one column per sample
            // left n thin lines. Between samples with a NaN or ∞ edge only a finite sample's own column is filled.
            void Fill(int x, double a, double b)
            {
                double yA = MapY(a), yB = MapY(b);
                double lo = Math.Max(Math.Min(yA, yB), top), hi = Math.Min(Math.Max(yA, yB), bottom);
                if (lo > hi) return;
                for (int y = (int)Math.Round(lo); y <= (int)Math.Round(hi); y++) image.SetPixel(x, y, options.BandColor);
            }
            bool Finite(int i) => IsFinite(lower[i]) && IsFinite(upper[i]);
            if (n == 1)
            {
                if (Finite(0)) Fill((int)Math.Round(MapX(0)), lower[0], upper[0]);
            }
            else
            {
                for (int x = left; x <= right; x++)
                {
                    double u = (double)(x - left) / (right - left) * (n - 1);
                    int i = Math.Min(n - 2, (int)Math.Floor(u));
                    double f = u - i;
                    if (Finite(i) && Finite(i + 1))
                        Fill(x, (1 - f) * lower[i] + f * lower[i + 1], (1 - f) * upper[i] + f * upper[i + 1]);
                    else
                    {
                        int j = f < 0.5 ? i : i + 1;
                        if (Finite(j) && (int)Math.Round(MapX(j)) == x) Fill(x, lower[j], upper[j]);
                    }
                }
            }

            if (center != null)
            {
                bool havePrev = false;
                double prevX = 0, prevY = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!IsFinite(center[i])) { havePrev = false; continue; }
                    double x = MapX(i), y = MapY(center[i]);
                    if (havePrev) image.DrawLineClipped(prevX, prevY, x, y, options.CenterColor, left, top, right, bottom);
                    prevX = x; prevY = y; havePrev = true;
                }
            }
            return image;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // (v − min) / (max − min), halved when the span overflows (±1e308 data mapped everything to one row).
        private static double Fraction(double v, double min, double max)
            => double.IsInfinity(max - min) ? (v / 2 - min / 2) / (max / 2 - min / 2) : (v - min) / (max - min);
    }
}
