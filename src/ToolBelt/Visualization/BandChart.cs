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
            if (lower.Count != upper.Count) throw new ArgumentException("lower and upper must have the same length.", nameof(upper));
            if (lower.Count == 0) throw new ArgumentException("Curves must be non-empty.", nameof(lower));
            if (center != null && center.Count != lower.Count) throw new ArgumentException("center length must match.", nameof(center));
            options ??= new BandOptions();
            // An infinite bound made the range ∞, which put every value on the bottom row.
            if (options.Min is double mn && !IsFinite(mn) || options.Max is double mx && !IsFinite(mx))
                throw new ArgumentOutOfRangeException(nameof(options), "Min and Max must be finite.");
            // Min ≥ Max drew the band on the centre row as if it were in range.
            if (options.Min >= options.Max) throw new ArgumentOutOfRangeException(nameof(options), "Min must be below Max.");

            var image = new ImageBuffer(width, height, options.Background);
            int m = options.Margin;
            // A negative margin put the plot area outside the image (SetPixel threw "x", or int.MinValue overflowed).
            if (m < 0) throw new ArgumentOutOfRangeException(nameof(options), m, "Margin must not be negative.");
            int left = m, right = width - 1 - m, top = m, bottom = height - 1 - m;
            if (right <= left || bottom <= top) throw new ArgumentException("Image is too small for the margin.", nameof(options));
            if (options.DrawFrame)
                image.DrawRectangle(left, top, right - left + 1, bottom - top + 1, options.FrameColor);

            int n = lower.Count;
            double minV = options.Min ?? double.PositiveInfinity, maxV = options.Max ?? double.NegativeInfinity;
            if (options.Min is null || options.Max is null)
            {
                // Finite values only: Math.Min/Max propagate NaN, which collapsed every value onto the centre row. The
                // centre line counts too: an excursion outside the band was clipped away, and a flat band drew it flat.
                for (int i = 0; i < n; i++)
                    foreach (double v in center is null ? new[] { lower[i], upper[i] } : new[] { lower[i], upper[i], center[i] })
                    {
                        if (!IsFinite(v)) continue;
                        if (options.Min is null && v < minV) minV = v;
                        if (options.Max is null && v > maxV) maxV = v;
                    }
            }
            OpenPastLimit(ref minV, ref maxV, options.Min.HasValue, options.Max.HasValue);
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
                    bool have = false;
                    double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                    void Take(double a, double b) { lo = Math.Min(lo, Math.Min(a, b)); hi = Math.Max(hi, Math.Max(a, b)); have = true; }
                    if (Finite(i) && Finite(i + 1))
                        Take((1 - f) * lower[i] + f * lower[i + 1], (1 - f) * upper[i] + f * upper[i + 1]);
                    // Every sample drawn in this column widens it: sampled once per column, a spike between two column
                    // positions (more samples than columns) vanished from the envelope.
                    double perColumn = (double)(n - 1) / (right - left);
                    int k0 = Math.Max(0, (int)Math.Floor(u - perColumn)), k1 = Math.Min(n - 1, (int)Math.Ceiling(u + perColumn));
                    for (int k = k0; k <= k1; k++)
                        if (Finite(k) && (int)Math.Round(MapX(k)) == x) Take(lower[k], upper[k]);
                    if (have) Fill(x, lo, hi);
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
                    if (havePrev) CenterSegment(prevX, prevY, center[i - 1], x, y, center[i]);
                    // No finite neighbour on either side (one point, or one between gaps) drew nothing; as LinePlot, a dot.
                    else if (!(i + 1 < n && IsFinite(center[i + 1]))) image.DrawLineClipped(x, y, x, y, options.CenterColor, left, top, right, bottom);
                    prevX = x; prevY = y; havePrev = true;
                }
            }
            return image;

            // An end whose pixel overflowed to ±∞ (1e300 on a 1e-300 range) made DrawLineClipped drop the segment: such an
            // end is moved, in value space, to where the segment crosses the range limit on its side.
            void CenterSegment(double xa, double ya, double va, double xb, double yb, double vb)
            {
                if (!IsFinite(ya) || !IsFinite(yb))
                {
                    if (va > maxV && vb > maxV || va < minV && vb < minV) return;   // wholly outside the range
                    double nxa = xa, nya = ya, nxb = xb, nyb = yb;
                    if (!IsFinite(ya)) { double l = va > maxV ? maxV : minV; nxa = xb + Part(vb, l, va) * (xa - xb); nya = MapY(l); }
                    if (!IsFinite(yb)) { double l = vb > maxV ? maxV : minV; nxb = xa + Part(va, l, vb) * (xb - xa); nyb = MapY(l); }
                    (xa, ya, xb, yb) = (nxa, nya, nxb, nyb);
                }
                image.DrawLineClipped(xa, ya, xb, yb, options.CenterColor, left, top, right, bottom);
            }
        }

        // How far from `from` to `to` the value `at` lies, halved when a difference overflows.
        private static double Part(double from, double at, double to)
            => double.IsInfinity(to - from) || double.IsInfinity(at - from) ? (at / 2 - from / 2) / (to / 2 - from / 2) : (at - from) / (to - from);

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // One explicit limit past all the data (or with no data at all) opens the axis beyond it, as SvgChart's and
        // HexBin's axes do: Min = 100 over data 0..10 drew a reversed 100..10 axis.
        private static void OpenPastLimit(ref double lo, ref double hi, bool explicitMin, bool explicitMax)
        {
            if (explicitMin == explicitMax || lo < hi) return;
            if (explicitMin) { hi = lo + Math.Max(1, Math.Abs(lo) * 0.1); if (double.IsInfinity(hi)) hi = double.MaxValue; }
            else { lo = hi - Math.Max(1, Math.Abs(hi) * 0.1); if (double.IsInfinity(lo)) lo = -double.MaxValue; }
        }

        // (v − min) / (max − min), halved when the span overflows (±1e308 data mapped everything to one row).
        private static double Fraction(double v, double min, double max)
            // Halved when v − min overflows too (a value far outside an explicit range): its ±∞ pixel dropped the whole line.
            => double.IsInfinity(max - min) || double.IsInfinity(v - min) ? (v / 2 - min / 2) / (max / 2 - min / 2) : (v - min) / (max - min);
    }
}
