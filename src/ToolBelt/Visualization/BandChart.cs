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
                for (int i = 0; i < n; i++)
                {
                    if (options.Min is null) minV = Math.Min(minV, Math.Min(lower[i], upper[i]));
                    if (options.Max is null) maxV = Math.Max(maxV, Math.Max(lower[i], upper[i]));
                }
            }
            double range = maxV - minV;
            int MapX(int i) => n > 1 ? left + (int)Math.Round((double)i / (n - 1) * (right - left)) : (left + right) / 2;
            int MapY(double v) => range > 0 ? bottom - (int)Math.Round((v - minV) / range * (bottom - top)) : (top + bottom) / 2;

            // Fill a vertical span at each column between the two curves.
            for (int i = 0; i < n; i++)
            {
                int x = MapX(i);
                int yA = MapY(lower[i]), yB = MapY(upper[i]);
                int y0 = Math.Min(yA, yB), y1 = Math.Max(yA, yB);
                for (int y = y0; y <= y1; y++) image.SetPixel(x, y, options.BandColor);
            }

            if (center != null)
            {
                int? prevX = null, prevY = null;
                for (int i = 0; i < n; i++)
                {
                    int x = MapX(i), y = MapY(center[i]);
                    if (prevX.HasValue) image.DrawLine(prevX.Value, prevY!.Value, x, y, options.CenterColor);
                    prevX = x; prevY = y;
                }
            }
            return image;
        }
    }
}
