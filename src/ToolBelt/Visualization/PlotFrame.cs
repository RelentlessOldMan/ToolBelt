// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// The mapping between a plot's data space and its pixel area: a pixel rectangle (y grows downward, as in
    /// SVG and raster images) plus the data ranges shown in it, optionally logarithmic. Renderers and overlays
    /// share one frame so annotations, gridlines and series agree to the pixel. Degenerate (zero-width) data
    /// ranges map to the centre of the area rather than dividing by zero. Values outside the range map outside
    /// the area — clipping is the caller's choice.
    /// </summary>
    public readonly struct PlotFrame
    {
        public PlotFrame(
            double left, double top, double width, double height,
            double xMin, double xMax, double yMin, double yMax,
            bool logX = false, bool logY = false)
        {
            if (!(width > 0) || double.IsInfinity(width)) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive and finite.");
            if (!(height > 0) || double.IsInfinity(height)) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive and finite.");
            if (double.IsNaN(left) || double.IsInfinity(left)) throw new ArgumentOutOfRangeException(nameof(left), left, "Left must be finite.");
            if (double.IsNaN(top) || double.IsInfinity(top)) throw new ArgumentOutOfRangeException(nameof(top), top, "Top must be finite.");
            // Each finite on its own, but Right or Bottom overflowed to ∞ (1e308 + 1e308) and every mapping with it.
            if (double.IsInfinity(left + width)) throw new ArgumentOutOfRangeException(nameof(width), width, "Left + Width must be finite.");
            if (double.IsInfinity(top + height)) throw new ArgumentOutOfRangeException(nameof(height), height, "Top + Height must be finite.");
            CheckRange(xMin, xMax, logX, nameof(xMin));
            CheckRange(yMin, yMax, logY, nameof(yMin));
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            XMin = xMin;
            XMax = xMax;
            YMin = yMin;
            YMax = yMax;
            LogX = logX;
            LogY = logY;
        }

        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
        public double Right => Left + Width;
        public double Bottom => Top + Height;

        public double XMin { get; }
        public double XMax { get; }
        public double YMin { get; }
        public double YMax { get; }
        public bool LogX { get; }
        public bool LogY { get; }

        /// <summary>Data x to pixel x. On a log axis a non-positive value has no position and yields NaN.</summary>
        public double MapX(double x) => Map(x, XMin, XMax, LogX, Left, Width, invert: false);

        /// <summary>Data y to pixel y (larger values are higher, i.e. smaller pixel y). NaN for non-positive values on a log axis.</summary>
        public double MapY(double y) => Map(y, YMin, YMax, LogY, Top, Height, invert: true);

        /// <summary>Pixel x back to data x.</summary>
        public double UnmapX(double px) => Unmap(px, XMin, XMax, LogX, Left, Width, invert: false);

        /// <summary>Pixel y back to data y.</summary>
        public double UnmapY(double py) => Unmap(py, YMin, YMax, LogY, Top, Height, invert: true);

        /// <summary>True if <paramref name="x"/> lies within the x range (inclusive).</summary>
        public bool ContainsX(double x) => x >= Math.Min(XMin, XMax) && x <= Math.Max(XMin, XMax);

        /// <summary>True if <paramref name="y"/> lies within the y range (inclusive).</summary>
        public bool ContainsY(double y) => y >= Math.Min(YMin, YMax) && y <= Math.Max(YMin, YMax);

        /// <summary>The same data ranges in a different pixel rectangle (e.g. a panel of a multi-panel figure).</summary>
        public PlotFrame WithArea(double left, double top, double width, double height)
            => new PlotFrame(left, top, width, height, XMin, XMax, YMin, YMax, LogX, LogY);

        /// <summary>The same pixel rectangle showing different data ranges.</summary>
        public PlotFrame WithRanges(double xMin, double xMax, double yMin, double yMax)
            => new PlotFrame(Left, Top, Width, Height, xMin, xMax, yMin, yMax, LogX, LogY);

        private static double Map(double v, double min, double max, bool log, double origin, double extent, bool invert)
        {
            double t;
            if (log)
            {
                if (!(v > 0)) return double.NaN;
                double lmin = Math.Log10(min), lmax = Math.Log10(max);
                t = lmax == lmin ? 0.5 : (Math.Log10(v) - lmin) / (lmax - lmin);
            }
            else
            {
                t = max == min ? 0.5
                    // A ±1e308 range overflowed; so did v − min for a value far outside a finite range, which mapped to ±∞
                    // rather than outside the area (LinePlot dropped the segment, SvgChart drew a dot).
                    : double.IsInfinity(max - min) || double.IsInfinity(v - min) ? (v / 2 - min / 2) / (max / 2 - min / 2)
                    : (v - min) / (max - min);
            }
            return invert ? origin + extent - t * extent : origin + t * extent;
        }

        private static double Unmap(double p, double min, double max, bool log, double origin, double extent, bool invert)
        {
            double t = invert ? (origin + extent - p) / extent : (p - origin) / extent;
            if (log)
            {
                double lmin = Math.Log10(min), lmax = Math.Log10(max);
                return Math.Pow(10, lmin + t * (lmax - lmin));
            }
            return double.IsInfinity(max - min) ? 2 * (min / 2 + t * (max / 2 - min / 2)) : min + t * (max - min);
        }

        // A segment to a point whose pixel position overflows (1e300 on a 1e-10 range maps to ±∞) was dropped as a gap by
        // the renderers. Clipped to the ranges in axis space (log10 on a log axis, the value on a linear one, halved when a
        // span would overflow), where the pixel map is affine, its ends map to finite pixels. It is clipped from the end
        // nearer the ranges: from the far end x0 + t·dx cancels (t ≈ 1). False if the segment misses the ranges.
        internal bool ClipToRanges(ref double x0, ref double y0, ref double x1, ref double y1)
        {
            double kx = Halving(x0, x1, XMin, XMax, LogX), ky = Halving(y0, y1, YMin, YMax, LogY);
            double ax = Axis(x0, LogX, kx), ay = Axis(y0, LogY, ky), bx = Axis(x1, LogX, kx), by = Axis(y1, LogY, ky);
            if (double.IsNaN(ax) || double.IsNaN(ay) || double.IsNaN(bx) || double.IsNaN(by)
                || double.IsInfinity(ax) || double.IsInfinity(ay) || double.IsInfinity(bx) || double.IsInfinity(by)) return false;
            double xlo = Axis(Math.Min(XMin, XMax), LogX, kx), xhi = Axis(Math.Max(XMin, XMax), LogX, kx);
            double ylo = Axis(Math.Min(YMin, YMax), LogY, ky), yhi = Axis(Math.Max(YMin, YMax), LogY, ky);
            // Both ends past the same edge miss the ranges, decided exactly: the edge quotients rounded to t0 = t1 = 1 for
            // y 1.6e308 to 1 over [0, 2e-323], and the raw outer end was kept (its pixel is −∞ there).
            if ((ax < xlo && bx < xlo) || (ax > xhi && bx > xhi) || (ay < ylo && by < ylo) || (ay > yhi && by > yhi)) return false;
            double Out(double x, double y) => Math.Max(Math.Max(xlo - x, x - xhi) / (xhi - xlo), Math.Max(ylo - y, y - yhi) / (yhi - ylo));
            bool flip = Out(ax, ay) > Out(bx, by);
            if (flip) { (ax, bx) = (bx, ax); (ay, by) = (by, ay); }
            double t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
            int e0 = -1, e1 = -1;
            if (!Edge(-dx, ax - xlo, 0, ref t0, ref t1, ref e0, ref e1) || !Edge(dx, xhi - ax, 1, ref t0, ref t1, ref e0, ref e1)
                || !Edge(-dy, ay - ylo, 2, ref t0, ref t1, ref e0, ref e1) || !Edge(dy, yhi - ay, 3, ref t0, ref t1, ref e0, ref e1)) return false;
            double ux0 = ax + t0 * dx, uy0 = ay + t0 * dy;
            double ux1 = t1 == 1 ? bx : ax + t1 * dx, uy1 = t1 == 1 ? by : ay + t1 * dy;
            // An end cut at an edge lies on it: t underflowed to 0 when the range was tiny against the jump (5e-301 over
            // 1e300), which left that end at the start and drew a point.
            Snap(e0, ref ux0, ref uy0, xlo, xhi, ylo, yhi);
            Snap(e1, ref ux1, ref uy1, xlo, xhi, ylo, yhi);
            double cx0 = Data(ux0, LogX, kx), cy0 = Data(uy0, LogY, ky);
            double cx1 = Data(ux1, LogX, kx), cy1 = Data(uy1, LogY, ky);
            if (flip) { x0 = cx1; y0 = cy1; x1 = cx0; y1 = cy0; }
            else { x0 = cx0; y0 = cy0; x1 = cx1; y1 = cy1; }
            return true;
        }

        private static double Halving(double a, double b, double min, double max, bool log)
            => !log && (double.IsInfinity(b - a) || double.IsInfinity(max - min) || double.IsInfinity(b - min) || double.IsInfinity(a - max)) ? 0.5 : 1;

        private static double Axis(double v, bool log, double k) => log ? (v > 0 ? Math.Log10(v) : double.NaN) : v * k;

        private static double Data(double u, bool log, double k) => log ? Math.Pow(10, u) : u / k;

        private static void Snap(int edge, ref double x, ref double y, double xlo, double xhi, double ylo, double yhi)
        {
            if (edge == 0) x = xlo;
            else if (edge == 1) x = xhi;
            else if (edge == 2) y = ylo;
            else if (edge == 3) y = yhi;
        }

        private static bool Edge(double p, double q, int edge, ref double t0, ref double t1, ref int e0, ref int e1)
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) { t0 = r; e0 = edge; } }
            else { if (r < t0) return false; if (r < t1) { t1 = r; e1 = edge; } }
            return true;
        }

        private static void CheckRange(double min, double max, bool log, string name)
        {
            if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
                throw new ArgumentOutOfRangeException(name, "Data range must be finite.");
            if (log && (!(min > 0) || !(max > 0)))
                throw new ArgumentOutOfRangeException(name, "A logarithmic range must be strictly positive.");
        }
    }
}
