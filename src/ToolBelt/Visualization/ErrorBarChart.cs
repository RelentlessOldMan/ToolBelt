// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;

namespace ToolBelt.Visualization
{
    /// <summary>Options for <see cref="ErrorBarChart.Render"/>.</summary>
    public sealed class ErrorBarOptions
    {
        /// <summary>Canvas background.</summary>
        public Rgba Background { get; set; } = Rgba.White;
        /// <summary>Marker and error-bar color.</summary>
        public Rgba Color { get; set; } = new Rgba(70, 130, 180);
        /// <summary>Frame color.</summary>
        public Rgba FrameColor { get; set; } = Rgba.Black;
        /// <summary>Pixels of margin.</summary>
        public int Margin { get; set; } = 12;
        /// <summary>Whether to draw the plot frame.</summary>
        public bool DrawFrame { get; set; } = true;
        /// <summary>Half-width of the error-bar caps, in pixels.</summary>
        public int CapHalfWidth { get; set; } = 3;
        /// <summary>Explicit axis bounds (else derived from the data, including the error extents).</summary>
        public double? MinX { get; set; }
        /// <summary>Explicit axis bounds (else derived from the data).</summary>
        public double? MaxX { get; set; }
        /// <summary>Explicit axis bounds (else derived from the data).</summary>
        public double? MinY { get; set; }
        /// <summary>Explicit axis bounds (else derived from the data).</summary>
        public double? MaxY { get; set; }
    }

    /// <summary>
    /// Plots points with symmetric vertical error bars (value ± error) — the standard way to show a mean
    /// with its uncertainty. The y-axis auto-scales to include the whole error range so no cap is clipped.
    /// </summary>
    public static class ErrorBarChart
    {
        /// <summary>Renders <paramref name="y"/> against <paramref name="x"/> with error bars of ±<paramref name="error"/>.</summary>
        public static ImageBuffer Render(int width, int height,
            IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double> error, ErrorBarOptions? options = null)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (error is null) throw new ArgumentNullException(nameof(error));
            if (x.Count != y.Count || x.Count != error.Count)
                throw new ArgumentException("x, y and error must have the same length.");
            options ??= new ErrorBarOptions();
            // An infinite bound made the mapping NaN: the markers vanished, or (int)NaN threw under checked arithmetic.
            foreach (double? bound in new[] { options.MinX, options.MaxX, options.MinY, options.MaxY })
                if (bound is double b && !IsFinite(b))
                    throw new ArgumentOutOfRangeException(nameof(options), "MinX, MaxX, MinY and MaxY must be finite.");

            var image = new ImageBuffer(width, height, options.Background);
            int m = options.Margin;
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.");
            if (options.DrawFrame)
                image.DrawRectangle(left, top, right - left + 1, bottom - top + 1, options.FrameColor);

            double minX = options.MinX ?? double.PositiveInfinity, maxX = options.MaxX ?? double.NegativeInfinity;
            double minY = options.MinY ?? double.PositiveInfinity, maxY = options.MaxY ?? double.NegativeInfinity;
            for (int i = 0; i < x.Count; i++)
            {
                // A point with a NaN or ∞ coordinate is not plotted and does not set the range (a NaN made the
                // range NaN, which put every point on the centre row); a non-finite error draws no bar.
                if (!IsFinite(x[i]) || !IsFinite(y[i])) continue;
                double e = IsFinite(error[i]) ? Math.Abs(error[i]) : 0;
                if (options.MinX is null) minX = Math.Min(minX, x[i]);
                if (options.MaxX is null) maxX = Math.Max(maxX, x[i]);
                if (options.MinY is null) minY = Math.Min(minY, y[i] - e);
                if (options.MaxY is null) maxY = Math.Max(maxY, y[i] + e);
            }
            double rangeX = maxX - minX, rangeY = maxY - minY;
            double MapX(double v) => rangeX > 0 ? left + Fraction(v, minX, maxX) * (right - left) : (left + right) / 2;
            double MapY(double v) => rangeY > 0 ? bottom - Fraction(v, minY, maxY) * (bottom - top) : (top + bottom) / 2;

            int cap = Math.Max(0, options.CapHalfWidth);
            for (int i = 0; i < x.Count; i++)
            {
                if (!IsFinite(x[i]) || !IsFinite(y[i])) continue;
                double px = MapX(x[i]);
                if (px < left || px > right) continue; // outside an explicit x range
                double py = MapY(y[i]);
                int ix = (int)Math.Round(px);
                if (IsFinite(error[i]))
                {
                    double e = Math.Abs(error[i]);
                    double hiY = MapY(y[i] + e), loY = MapY(y[i] - e);
                    // Clipped as doubles: a huge error against an explicit range overflowed the int casts.
                    image.DrawLineClipped(px, hiY, px, loY, options.Color, left, top, right, bottom);              // whisker
                    image.DrawLineClipped(px - cap, hiY, px + cap, hiY, options.Color, left, top, right, bottom);  // upper cap
                    image.DrawLineClipped(px - cap, loY, px + cap, loY, options.Color, left, top, right, bottom);  // lower cap
                }
                if (py >= top && py <= bottom)
                    image.DrawRectangle(ix - 1, (int)Math.Round(py) - 1, 3, 3, options.Color, filled: true); // marker
            }
            return image;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // (v − min) / (max − min), halved when the span overflows (±1e308 data mapped everything to one edge).
        private static double Fraction(double v, double min, double max)
            => double.IsInfinity(max - min) ? (v / 2 - min / 2) / (max / 2 - min / 2) : (v - min) / (max - min);
    }
}
