// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Visualization
{
    /// <summary>A 32-bit RGBA color (bytes, no premultiplication).</summary>
    public readonly struct Rgba : IEquatable<Rgba>
    {
        public Rgba(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public byte A { get; }

        public static readonly Rgba Transparent = new Rgba(0, 0, 0, 0);
        public static readonly Rgba Black = new Rgba(0, 0, 0);
        public static readonly Rgba White = new Rgba(255, 255, 255);
        public static readonly Rgba Red = new Rgba(255, 0, 0);
        public static readonly Rgba Green = new Rgba(0, 255, 0);
        public static readonly Rgba Blue = new Rgba(0, 0, 255);

        public bool Equals(Rgba other) => R == other.R && G == other.G && B == other.B && A == other.A;
        public override bool Equals(object? obj) => obj is Rgba c && Equals(c);
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
        public static bool operator ==(Rgba a, Rgba b) => a.Equals(b);
        public static bool operator !=(Rgba a, Rgba b) => !a.Equals(b);
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    }

    /// <summary>
    /// A minimal RGBA raster canvas — width, height, a row-major byte buffer, get/set pixel, fill, blit and
    /// simple line/rectangle drawing — so the pure-core renderers have something to draw on. This is a raster
    /// target, not a drawing framework: drawing operations overwrite (no alpha blending) and clip silently.
    /// </summary>
    public sealed class ImageBuffer
    {
        private readonly byte[] _pixels;

        public ImageBuffer(int width, int height, Rgba? fill = null)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            Width = width;
            Height = height;
            _pixels = new byte[width * height * 4];
            if (fill.HasValue) Fill(fill.Value);
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>The backing RGBA buffer (row-major, 4 bytes per pixel). Mutating it mutates the image.</summary>
        public byte[] Pixels => _pixels;

        public Rgba GetPixel(int x, int y)
        {
            CheckBounds(x, y);
            int i = (y * Width + x) * 4;
            return new Rgba(_pixels[i], _pixels[i + 1], _pixels[i + 2], _pixels[i + 3]);
        }

        public void SetPixel(int x, int y, Rgba color)
        {
            CheckBounds(x, y);
            Put(x, y, color);
        }

        public void Fill(Rgba color)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    Put(x, y, color);
        }

        /// <summary>Draws a line with Bresenham's algorithm, clipping to the canvas.</summary>
        public void DrawLine(int x0, int y0, int x1, int y1, Rgba color)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                PutClipped(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Draws a rectangle (outline, or filled), clipping to the canvas.</summary>
        public void DrawRectangle(int x, int y, int width, int height, Rgba color, bool filled = false)
        {
            if (width <= 0 || height <= 0) return;
            if (filled)
            {
                for (int yy = y; yy < y + height; yy++)
                    for (int xx = x; xx < x + width; xx++)
                        PutClipped(xx, yy, color);
                return;
            }
            for (int xx = x; xx < x + width; xx++) { PutClipped(xx, y, color); PutClipped(xx, y + height - 1, color); }
            for (int yy = y; yy < y + height; yy++) { PutClipped(x, yy, color); PutClipped(x + width - 1, yy, color); }
        }

        /// <summary>Copies <paramref name="source"/> onto this image at (destX, destY), clipping to the canvas.</summary>
        public void Blit(ImageBuffer source, int destX, int destY)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            for (int sy = 0; sy < source.Height; sy++)
                for (int sx = 0; sx < source.Width; sx++)
                    PutClipped(destX + sx, destY + sy, source.GetPixel(sx, sy));
        }

        private void Put(int x, int y, Rgba color)
        {
            int i = (y * Width + x) * 4;
            _pixels[i] = color.R; _pixels[i + 1] = color.G; _pixels[i + 2] = color.B; _pixels[i + 3] = color.A;
        }

        private void PutClipped(int x, int y, Rgba color)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height) Put(x, y, color);
        }

        private void CheckBounds(int x, int y)
        {
            if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x), x, $"x must be in [0, {Width}).");
            if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y), y, $"y must be in [0, {Height}).");
        }
    }
}
