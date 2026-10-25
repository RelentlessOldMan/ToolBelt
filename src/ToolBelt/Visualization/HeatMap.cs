// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs and Visualization/Colormap.cs.
using System;

namespace ToolBelt.Visualization
{
    /// <summary>Options for <see cref="HeatMap.Render"/>.</summary>
    public sealed class HeatMapOptions
    {
        /// <summary>Pixels per cell (square). Default 1.</summary>
        public int CellSize { get; set; } = 1;

        /// <summary>Value mapped to the low end of the colormap. Null auto-detects the data minimum.</summary>
        public double? Min { get; set; }

        /// <summary>Value mapped to the high end. Null auto-detects the data maximum.</summary>
        public double? Max { get; set; }

        /// <summary>Color for NaN cells (default fully transparent).</summary>
        public Rgba NanColor { get; set; } = Rgba.Transparent;
    }

    /// <summary>
    /// Renders a 2-D grid of values to an <see cref="ImageBuffer"/> through a <see cref="Colormap"/> — the
    /// core "capture this matrix as a picture" primitive. Row 0 is drawn at the top. Values are normalized to
    /// the data range (or an explicit range); NaN cells get a configurable color.
    /// </summary>
    public static class HeatMap
    {
        public static ImageBuffer Render(double[,] data, Colormap colormap, HeatMapOptions? options = null)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (colormap is null) throw new ArgumentNullException(nameof(colormap));
            options ??= new HeatMapOptions();
            if (options.CellSize < 1) throw new ArgumentOutOfRangeException(nameof(options), options.CellSize, "CellSize must be at least 1.");

            int rows = data.GetLength(0), cols = data.GetLength(1);
            if (rows == 0 || cols == 0) throw new ArgumentException("Data must be non-empty.", nameof(data));

            double min = options.Min ?? double.PositiveInfinity;
            double max = options.Max ?? double.NegativeInfinity;
            if (options.Min is null || options.Max is null)
            {
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        double v = data[r, c];
                        if (double.IsNaN(v) || double.IsInfinity(v)) continue; // an ∞ made the range ∞, so every cell NaN
                        if (options.Min is null && v < min) min = v;
                        if (options.Max is null && v > max) max = v;
                    }
            }
            if (min > max && options.Min.HasValue && options.Max.HasValue)
                throw new ArgumentException("Min must not exceed Max.", nameof(options));
            // Halved when the span overflows (±1e308): clamping it to double.MaxValue skewed the mapping.
            bool halve = double.IsInfinity(max - min);
            double Fraction(double v)
            {
                // An empty range (Min = Max, or one explicit limit past all the data) put every cell at mid-colour.
                if (!(max > min)) return v < min ? 0 : v > max ? 1 : 0.5;
                return halve ? (v / 2 - min / 2) / (max / 2 - min / 2) : (v - min) / (max - min);
            }

            int cell = options.CellSize;
            if ((long)cols * cell > int.MaxValue || (long)rows * cell > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(options), "CellSize makes the image too large."); // the int product wrapped
            var image = new ImageBuffer(cols * cell, rows * cell);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double v = data[r, c];
                    Rgba color = double.IsNaN(v)
                        ? options.NanColor
                        : double.IsInfinity(v) ? colormap.Map(v > 0 ? 1 : 0) // ±∞ takes the end colour
                        : colormap.Map(Fraction(v));
                    image.DrawRectangle(c * cell, r * cell, cell, cell, color, filled: true);
                }
            return image;
        }
    }
}
