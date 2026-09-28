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
                if (options.MinX is null) minX = Math.Min(minX, x[i]);
                if (options.MaxX is null) maxX = Math.Max(maxX, x[i]);
                if (options.MinY is null) minY = Math.Min(minY, y[i] - Math.Abs(error[i]));
                if (options.MaxY is null) maxY = Math.Max(maxY, y[i] + Math.Abs(error[i]));
            }
            double rangeX = maxX - minX, rangeY = maxY - minY;
            int MapX(double v) => rangeX > 0 ? left + (int)Math.Round((v - minX) / rangeX * (right - left)) : (left + right) / 2;
            int MapY(double v) => rangeY > 0 ? bottom - (int)Math.Round((v - minY) / rangeY * (bottom - top)) : (top + bottom) / 2;

            int cap = Math.Max(0, options.CapHalfWidth);
            for (int i = 0; i < x.Count; i++)
            {
                int px = MapX(x[i]);
                int py = MapY(y[i]);
                int hiY = MapY(y[i] + Math.Abs(error[i]));
                int loY = MapY(y[i] - Math.Abs(error[i]));
                image.DrawLine(px, hiY, px, loY, options.Color);            // whisker
                image.DrawLine(px - cap, hiY, px + cap, hiY, options.Color); // upper cap
                image.DrawLine(px - cap, loY, px + cap, loY, options.Color); // lower cap
                image.DrawRectangle(px - 1, py - 1, 3, 3, options.Color, filled: true); // marker
            }
            return image;
        }
    }
}
