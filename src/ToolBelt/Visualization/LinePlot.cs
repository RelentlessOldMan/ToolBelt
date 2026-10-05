// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba) and PlotFrame.cs.
using System;
using System.Collections.Generic;

namespace ToolBelt.Visualization
{
    /// <summary>How a series is drawn.</summary>
    public enum PlotStyle
    {
        Line,
        Scatter,
        Bar,
    }

    /// <summary>One data series to plot.</summary>
    public sealed class Series
    {
        public Series(IReadOnlyList<double> y, Rgba color, PlotStyle style = PlotStyle.Line, IReadOnlyList<double>? x = null, string? name = null)
        {
            Y = y ?? throw new ArgumentNullException(nameof(y));
            if (x != null && x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            X = x;
            Color = color;
            Style = style;
            Name = name;
        }

        public IReadOnlyList<double> Y { get; }
        /// <summary>X coordinates; if null, the point index 0..n-1 is used.</summary>
        public IReadOnlyList<double>? X { get; }
        public Rgba Color { get; }
        public PlotStyle Style { get; }
        /// <summary>Optional display name (shown in a legend by renderers that draw one).</summary>
        public string? Name { get; }
    }

    /// <summary>Options for <see cref="LinePlot.Render"/>.</summary>
    public sealed class PlotOptions
    {
        public Rgba Background { get; set; } = Rgba.White;
        public Rgba FrameColor { get; set; } = Rgba.Black;
        public int Margin { get; set; } = 10;
        public bool DrawFrame { get; set; } = true;
        public double? MinX { get; set; }
        public double? MaxX { get; set; }
        public double? MinY { get; set; }
        public double? MaxY { get; set; }
    }

    /// <summary>
    /// Renders one or more data series to an <see cref="ImageBuffer"/> as lines, scatter points or bars, with
    /// an optional frame — for a quick, portable picture of the data's shape. This renderer draws no text
    /// labels (there is no font engine here); use it for at-a-glance figures, and layer labels elsewhere if
    /// needed.
    /// </summary>
    public static class LinePlot
    {
        public static ImageBuffer Render(int width, int height, IReadOnlyList<Series> series, PlotOptions? options = null)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            options ??= new PlotOptions();
            var image = new ImageBuffer(width, height, options.Background);

            int m = options.Margin;
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.");

            if (options.DrawFrame)
                image.DrawRectangle(left, top, right - left + 1, bottom - top + 1, options.FrameColor);

            Bounds(series, options, out double minX, out double maxX, out double minY, out double maxY);
            double rangeX = maxX - minX, rangeY = maxY - minY;

            int MapX(double x) => rangeX > 0 ? left + (int)Math.Round((x - minX) / rangeX * (right - left)) : (left + right) / 2;
            int MapY(double y) => rangeY > 0 ? bottom - (int)Math.Round((y - minY) / rangeY * (bottom - top)) : (top + bottom) / 2;

            foreach (Series s in series)
            {
                int? prevX = null, prevY = null;
                for (int i = 0; i < s.Y.Count; i++)
                {
                    double xv = s.X?[i] ?? i;
                    int px = MapX(xv), py = MapY(s.Y[i]);
                    switch (s.Style)
                    {
                        case PlotStyle.Line:
                            if (prevX.HasValue) image.DrawLine(prevX.Value, prevY!.Value, px, py, s.Color);
                            break;
                        case PlotStyle.Scatter:
                            image.DrawRectangle(px - 1, py - 1, 3, 3, s.Color, filled: true);
                            break;
                        case PlotStyle.Bar:
                            image.DrawRectangle(px, Math.Min(py, MapY(0)), 1, Math.Abs(py - MapY(0)) + 1, s.Color, filled: true);
                            break;
                    }
                    prevX = px; prevY = py;
                }
            }
            return image;
        }

        /// <summary>
        /// The data-to-pixel mapping <see cref="Render"/> uses for the same arguments, so overlays such as
        /// <c>Annotations.Draw</c> line up with the plotted series (round the mapped coordinates to pixels). With
        /// no plottable data the range defaults to [0, 1].
        /// </summary>
        public static PlotFrame Frame(int width, int height, IReadOnlyList<Series> series, PlotOptions? options = null)
        {
            if (series is null) throw new ArgumentNullException(nameof(series));
            options ??= new PlotOptions();
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            int m = options.Margin;
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.");
            Bounds(series, options, out double minX, out double maxX, out double minY, out double maxY);
            if (double.IsInfinity(minX) || double.IsInfinity(maxX)) { minX = 0; maxX = 1; }
            if (double.IsInfinity(minY) || double.IsInfinity(maxY)) { minY = 0; maxY = 1; }
            return new PlotFrame(left, top, right - left, bottom - top, minX, maxX, minY, maxY);
        }

        // Data bounds across all series, or the explicit option values.
        private static void Bounds(IReadOnlyList<Series> series, PlotOptions options,
            out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = options.MinX ?? double.PositiveInfinity; maxX = options.MaxX ?? double.NegativeInfinity;
            minY = options.MinY ?? double.PositiveInfinity; maxY = options.MaxY ?? double.NegativeInfinity;
            if (options.MinX is null || options.MaxX is null || options.MinY is null || options.MaxY is null)
            {
                foreach (Series s in series)
                    for (int i = 0; i < s.Y.Count; i++)
                    {
                        double x = s.X?[i] ?? i;
                        double y = s.Y[i];
                        if (options.MinX is null && x < minX) minX = x;
                        if (options.MaxX is null && x > maxX) maxX = x;
                        if (options.MinY is null && y < minY) minY = y;
                        if (options.MaxY is null && y > maxY) maxY = y;
                    }
            }
        }
    }
}
