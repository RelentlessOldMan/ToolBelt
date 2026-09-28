// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (Rgba) and Visualization/Colormap.cs.
using System;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// Renders a colormap as a gradient strip — the legend that tells a heat map what its colors mean.
    /// A vertical bar runs high value at the top to low at the bottom; a horizontal bar runs low at the
    /// left to high at the right, matching the usual reading conventions.
    /// </summary>
    public static class Colorbar
    {
        /// <summary>
        /// Renders <paramref name="map"/> across the whole image. When <paramref name="vertical"/> is true
        /// the gradient runs bottom (0) to top (1); otherwise left (0) to right (1). An optional border is
        /// drawn around the strip.
        /// </summary>
        public static ImageBuffer Render(int width, int height, Colormap map, bool vertical = true, Rgba? border = null)
        {
            if (map is null) throw new ArgumentNullException(nameof(map));
            var image = new ImageBuffer(width, height);

            if (vertical)
            {
                for (int y = 0; y < height; y++)
                {
                    double t = height == 1 ? 1.0 : (double)(height - 1 - y) / (height - 1);
                    Rgba c = map.Map(t);
                    for (int x = 0; x < width; x++) image.SetPixel(x, y, c);
                }
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    double t = width == 1 ? 0.0 : (double)x / (width - 1);
                    Rgba c = map.Map(t);
                    for (int y = 0; y < height; y++) image.SetPixel(x, y, c);
                }
            }

            if (border.HasValue) image.DrawRectangle(0, 0, width, height, border.Value);
            return image;
        }
    }
}
