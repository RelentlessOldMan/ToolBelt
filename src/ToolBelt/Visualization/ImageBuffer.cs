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
            // width * height * 4 wrapped silently: 65536 × 16384 gave a zero-length buffer behind a "valid" image.
            if ((long)width * height * 4 > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), $"A {width} × {height} image is too large.");
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
            if ((uint)x0 >= (uint)Width || (uint)y0 >= (uint)Height || (uint)x1 >= (uint)Width || (uint)y1 >= (uint)Height)
            {
                // Cut to the canvas first: stepping a far off-canvas line pixel by pixel hung, and Math.Abs of an
                // int.MinValue difference threw.
                DrawLineClipped(x0, y0, x1, y1, color, 0, 0, Width - 1, Height - 1);
                return;
            }
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

        /// <summary>
        /// Draws a line between real-valued pixel coordinates, clipped to the rectangle [left, right] × [top, bottom]
        /// before rounding, so a far off-canvas endpoint keeps its slope and costs nothing. Nothing is drawn if
        /// a coordinate is NaN or the segment misses the rectangle.
        /// </summary>
        public void DrawLineClipped(double x0, double y0, double x1, double y1, Rgba color,
            int left, int top, int right, int bottom)
        {
            if (double.IsNaN(x0) || double.IsNaN(y0) || double.IsNaN(x1) || double.IsNaN(y1)) return;
            // Liang–Barsky against the rectangle; an infinite coordinate is handled by the same comparisons.
            double t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
            if (double.IsInfinity(x0) || double.IsInfinity(y0) || double.IsInfinity(x1) || double.IsInfinity(y1)
                || double.IsInfinity(dx) || double.IsInfinity(dy)) return;
            if (!ClipEdge(-dx, x0 - left, ref t0, ref t1) || !ClipEdge(dx, right - x0, ref t0, ref t1)
                || !ClipEdge(-dy, y0 - top, ref t0, ref t1) || !ClipEdge(dy, bottom - y0, ref t0, ref t1))
                return;
            DrawLine((int)Math.Round(x0 + t0 * dx), (int)Math.Round(y0 + t0 * dy),
                     (int)Math.Round(x0 + t1 * dx), (int)Math.Round(y0 + t1 * dy), color);
        }

        private static bool ClipEdge(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        /// <summary>Draws a rectangle (outline, or filled), clipping to the canvas.</summary>
        public void DrawRectangle(int x, int y, int width, int height, Rgba color, bool filled = false)
        {
            if (width <= 0 || height <= 0) return;
            // Walk only the part on the canvas (in long: x + width overflowed, and a huge rectangle took seconds).
            long x1 = (long)x + width - 1, y1 = (long)y + height - 1;
            int cx0 = (int)Math.Max(0, x), cy0 = (int)Math.Max(0, y);
            int cx1 = (int)Math.Min(Width - 1, x1), cy1 = (int)Math.Min(Height - 1, y1);
            if (filled)
            {
                for (int yy = cy0; yy <= cy1; yy++)
                    for (int xx = cx0; xx <= cx1; xx++)
                        Put(xx, yy, color);
                return;
            }
            for (int xx = cx0; xx <= cx1; xx++)
            {
                if (y >= 0 && y < Height) Put(xx, y, color);
                if (y1 >= 0 && y1 < Height) Put(xx, (int)y1, color);
            }
            for (int yy = cy0; yy <= cy1; yy++)
            {
                if (x >= 0 && x < Width) Put(x, yy, color);
                if (x1 >= 0 && x1 < Width) Put((int)x1, yy, color);
            }
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
