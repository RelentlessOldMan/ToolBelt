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
            if (!(width > 0)) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (!(height > 0)) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
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
                t = max == min ? 0.5 : (v - min) / (max - min);
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
            return min + t * (max - min);
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
