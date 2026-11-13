// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Numerics;

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
            // The cap is the runtime's largest byte array: just under int.MaxValue threw OutOfMemoryException.
            if ((long)width * height * 4 > 0x7FFFFFC7)
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
            if (x0 < 0 || x0 >= Width || y0 < 0 || y0 >= Height || x1 < 0 || x1 >= Width || y1 < 0 || y1 >= Height)
            {
                // Visit only the columns (or rows) that cross the canvas: stepping a far off-canvas line pixel by
                // pixel hung, and Math.Abs of an int.MinValue difference threw. Cutting the segment to the canvas and
                // redrawing between rounded ends lost or shifted pixels, so plot the very line drawn below instead.
                long ldx = (long)x1 - x0, ldy = (long)y1 - y0;
                if (Math.Abs(ldx) >= Math.Abs(ldy)) DrawMajor(x0, y0, ldx, ldy, Width, color, xMajor: true);
                else DrawMajor(y0, x0, ldy, ldx, Height, color, xMajor: false);
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

        // The loop below lands, at step k along the major axis, on minor offset floor((2k·|minor| + |major|) / (2·|major|)).
        // Decimal holds 2k·|minor| for any int endpoints, where long overflowed.
        private void DrawMajor(long a0, long b0, long da, long db, int size, Rgba color, bool xMajor)
        {
            long lo = Math.Max(0, Math.Min(a0, a0 + da)), hi = Math.Min(size - 1, Math.Max(a0, a0 + da));
            long major = Math.Abs(da), minor = Math.Abs(db);
            for (long a = lo; a <= hi; a++)
            {
                long k = Math.Abs(a - a0);
                long off = major == 0 ? 0 : (long)decimal.Floor((2m * k * minor + major) / (2m * major));
                long b = db < 0 ? b0 - off : b0 + off;
                if (xMajor) { if (b >= 0 && b < Height) Put((int)a, (int)b, color); }
                else if (b >= 0 && b < Width) Put((int)b, (int)a, color);
            }
        }

        /// <summary>
        /// Draws a line between real-valued pixel coordinates, clipped to the rectangle [left, right] × [top, bottom]
        /// before rounding, so a far off-canvas endpoint (anywhere in the double range) keeps its slope and costs
        /// nothing. Nothing is drawn if a coordinate is NaN or infinite, or the segment misses the rectangle.
        /// </summary>
        public void DrawLineClipped(double x0, double y0, double x1, double y1, Rgba color,
            int left, int top, int right, int bottom)
        {
            if (!IsFinite(x0) || !IsFinite(y0) || !IsFinite(x1) || !IsFinite(y1)) return;
            // Clip along the major axis, reading each end off the line where it is cut. Liang–Barsky's parameter t lost
            // the far case: for endpoints at ±1e20 every t rounds to 0.5, so it picked the wrong edge and drew a corner-
            // to-corner diagonal, and x0 + t·dx cancelled catastrophically (pixels off at 1e17, the whole canvas at 1e200).
            // Halved deltas only choose the axis: x1 - x0 overflows to ∞ for endpoints near ±1e308.
            double hx = x1 * 0.5 - x0 * 0.5, hy = y1 * 0.5 - y0 * 0.5;
            if (hx == 0 && hy == 0)
            {
                if (x0 >= left && x0 <= right && y0 >= top && y0 <= bottom)
                    DrawLine(ToEdge(x0, left, right), ToEdge(y0, top, bottom), ToEdge(x0, left, right), ToEdge(y0, top, bottom), color);
                return;
            }
            if (Math.Abs(hx) >= Math.Abs(hy))
            {
                if (!ClipMajor(x0, y0, x1, y1, left, right, top, bottom, out var a, out var b)) return;
                DrawLine(ToEdge(a.U, left, right), ToEdge(a.V, top, bottom), ToEdge(b.U, left, right), ToEdge(b.V, top, bottom), color);
            }
            else
            {
                if (!ClipMajor(y0, x0, y1, x1, top, bottom, left, right, out var a, out var b)) return;
                DrawLine(ToEdge(a.V, left, right), ToEdge(a.U, top, bottom), ToEdge(b.V, left, right), ToEdge(b.U, top, bottom), color);
            }
        }

        // The part of the segment (u0, v0)–(u1, v1), u0 ≠ u1, inside [uLo, uHi] × [vLo, vHi], as its two ends in the
        // segment's own direction (DrawLine breaks ties by direction). False if it misses. Each end is bounded by an
        // endpoint, a u edge or a v edge, which fixes one of its coordinates exactly; the other is read off the line and
        // rounded exactly (rounding a correctly rounded quotient again turned 31.4999... into 32).
        private static bool ClipMajor(double u0, double v0, double u1, double v1, double uLo, double uHi, double vLo, double vHi,
            out (double U, double V) a, out (double U, double V) b)
        {
            // Each bound: its u (for comparing), its v, and whether it was cut by a v edge (then V is that edge).
            var lo = u0 <= u1 ? (U: u0, V: v0, VEdge: false) : (U: u1, V: v1, VEdge: false);
            var hi = u0 <= u1 ? (U: u1, V: v1, VEdge: false) : (U: u0, V: v0, VEdge: false);
            a = b = default;
            if (uLo > lo.U) lo = (uLo, RoundedAlong(uLo, u0, v0, u1, v1), false);
            if (uHi < hi.U) hi = (uHi, RoundedAlong(uHi, u0, v0, u1, v1), false);
            if (v0 == v1)
            {
                if (v0 < vLo || v0 > vHi) return false;
            }
            else
            {
                // The line is monotone in v, so the u where it crosses vLo and vHi bound the part between them.
                double ca = Along(vLo, v0, u0, v1, u1), cb = Along(vHi, v0, u0, v1, u1);
                var (cLo, eLo, cHi, eHi) = ca <= cb ? (ca, vLo, cb, vHi) : (cb, vHi, ca, vLo);
                if (cLo > lo.U) lo = (cLo, eLo, true);
                if (cHi < hi.U) hi = (cHi, eHi, true);
            }
            if (lo.U > hi.U) return false;
            // An end cut by a v edge lies on that edge; its u is the exact crossing, rounded once.
            (double U, double V) End((double U, double V, bool VEdge) e)
                => e.VEdge ? (RoundedAlong(e.V, v0, u0, v1, u1), e.V) : (e.U, e.V);
            (a, b) = u0 <= u1 ? (End(lo), End(hi)) : (End(hi), End(lo));
            return true;
        }

        // The v coordinate at u on the line through (u0, v0) and (u1, v1), u0 ≠ u1. Up to 2^40 doubles are within 2^-11 of
        // a pixel; past that v0 + (u - u0)·slope cancels huge terms, so it is computed exactly on the doubles' binary
        // expansions (each double is an integer times 2^e) and rounded once at the end.
        private static double Along(double u, double u0, double v0, double u1, double v1)
        {
            const double Small = 1099511627776.0;                               // 2^40
            if (Math.Abs(u0) <= Small && Math.Abs(v0) <= Small && Math.Abs(u1) <= Small && Math.Abs(v1) <= Small)
                return v0 + (u - u0) * (v1 - v0) / (u1 - u0);                   // a tiny u1 - u0 overflows to ±∞, not NaN
            Exact(u, u0, v0, u1, v1, out BigInteger num, out BigInteger den);
            BigInteger whole = BigInteger.DivRem(num, den, out BigInteger rem);
            int shift = Math.Max(0, den.ToByteArray().Length * 8 - 64);         // keep the fraction's parts in double range
            return (double)whole + (double)(rem >> shift) / (double)(den >> shift);
        }

        // Along, rounded half-to-even to an integer from the exact value rather than from its nearest double. Up to 2^40
        // the double is within about 2^-10, which settles the rounding unless it lies near a half (a slope of -1/2 through
        // (-6998.66, 3499.33) crosses x = -39 a hair off 19.5); past 2^40, or near a half, it is computed exactly.
        private static double RoundedAlong(double u, double u0, double v0, double u1, double v1)
        {
            const double Small = 1099511627776.0;                               // 2^40
            if (Math.Abs(u0) <= Small && Math.Abs(v0) <= Small && Math.Abs(u1) <= Small && Math.Abs(v1) <= Small)
            {
                double f = Along(u, u0, v0, u1, v1);
                if (Math.Abs(f - Math.Floor(f) - 0.5) > 1.0 / 64) return Math.Round(f);   // NaN (∞ - ∞) falls through
            }
            Exact(u, u0, v0, u1, v1, out BigInteger num, out BigInteger den);
            BigInteger q = BigInteger.DivRem(num, den, out BigInteger r);
            if (r.Sign < 0) { q -= 1; r += den; }                               // floor
            int c = (r << 1).CompareTo(den);
            if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
            return (double)q;
        }

        // v = num / den exactly, den > 0, from the doubles' binary expansions (each an integer times 2^e).
        private static void Exact(double u, double u0, double v0, double u1, double v1, out BigInteger num, out BigInteger den)
        {
            int e = Math.Min(0, Math.Min(Math.Min(LowBit(u), LowBit(u0)), Math.Min(LowBit(v0), Math.Min(LowBit(u1), LowBit(v1)))));
            BigInteger bu = Scaled(u, e), bu0 = Scaled(u0, e), bv0 = Scaled(v0, e), bu1 = Scaled(u1, e), bv1 = Scaled(v1, e);
            num = bv0 * (bu1 - bu0) + (bu - bu0) * (bv1 - bv0);
            den = bu1 - bu0;
            if (den.Sign < 0) { num = -num; den = -den; }
            den <<= -e;
        }

        // The exponent of a double's lowest mantissa bit (zero has none), and the double as an integer multiple of 2^e.
        private static int LowBit(double v)
        {
            if (v == 0) return int.MaxValue;
            int biased = (int)((BitConverter.DoubleToInt64Bits(v) >> 52) & 0x7FF);
            return Math.Max(1, biased) - 1075;
        }

        private static BigInteger Scaled(double v, int e)
        {
            if (v == 0) return BigInteger.Zero;
            long bits = BitConverter.DoubleToInt64Bits(v);
            int biased = (int)((bits >> 52) & 0x7FF);
            long mantissa = (bits & 0xFFFFFFFFFFFFFL) | (biased == 0 ? 0 : 1L << 52);
            var m = new BigInteger(mantissa) << (Math.Max(1, biased) - 1075 - e);
            return bits < 0 ? -m : m;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static int ToEdge(double v, int lo, int hi) => (int)Math.Max(lo, Math.Min(hi, Math.Round(v)));

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
            // Blitting an image onto itself with an offset read pixels it had already overwritten (smearing).
            if (ReferenceEquals(source, this))
            {
                source = new ImageBuffer(Width, Height);
                Buffer.BlockCopy(_pixels, 0, source._pixels, 0, _pixels.Length);
            }
            // Only the overlap is walked, in long: destX + sx overflowed for an offset near int.MaxValue.
            long sx0 = Math.Max(0, -(long)destX), sx1 = Math.Min(source.Width, (long)Width - destX);
            long sy0 = Math.Max(0, -(long)destY), sy1 = Math.Min(source.Height, (long)Height - destY);
            for (long sy = sy0; sy < sy1; sy++)
                for (long sx = sx0; sx < sx1; sx++)
                    Put((int)(destX + sx), (int)(destY + sy), source.GetPixel((int)sx, (int)sy));
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
            if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x), x, $"x must be in [0, {Width}).");
            if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y), y, $"y must be in [0, {Height}).");
        }
    }
}
