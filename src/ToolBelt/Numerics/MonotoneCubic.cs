// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Monotone cubic (Fritsch-Carlson) interpolation: smooth like a spline but guaranteed not to overshoot
    /// between points, so it never invents a non-physical value — which matters a great deal for calibration
    /// curves. The points must be finite and the x values strictly increasing; inputs outside the range clamp to
    /// the endpoints.
    /// Immutable.
    /// </summary>
    public sealed class MonotoneCubic
    {
        private readonly double[] _x;  // as given: segments are found and t is formed on these
        private readonly double[] _h;  // the segment widths times _sx (0 where that underflows; see Width)
        private readonly double[] _y;
        private readonly double[] _m; // tangents
        private readonly double _sx, _sy; // the stored x and y are the inputs times these (see RangeScale)
        private readonly double[] _yIn;   // the y given, returned exactly at the knots (scaling can flush a tiny y to 0)

        public MonotoneCubic(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.", nameof(y));
            if (x.Count < 2) throw new ArgumentException("At least two points are required.", nameof(x));

            int n = x.Count;
            _x = new double[n];
            _y = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i])) throw new ArgumentException($"x[{i}] is not finite.", nameof(x));
                if (double.IsNaN(y[i]) || double.IsInfinity(y[i])) throw new ArgumentException($"y[{i}] is not finite.", nameof(y));
                if (i > 0 && x[i] <= x[i - 1]) throw new ArgumentException("x values must be strictly increasing.", nameof(x));
                _x[i] = x[i];
                _y[i] = y[i];
            }

            _yIn = (double[])_y.Clone();
            _sx = RangeScale(_x);
            _sy = RangeScale(_y);
            for (int i = 0; i < n; i++) _y[i] *= _sy;
            // x is not scaled itself: a width as wide as ±double.MaxValue next to one of 1e-310 is beyond any one scale,
            // and scaled x collapsed ({0, 1e-310, 1e308} became {0, 0, 2^499}, a NaN curve that missed the middle knot).
            _h = new double[n - 1];
            for (int i = 0; i < n - 1; i++) _h[i] = Width(_x[i], _x[i + 1], _sx);

            // Secant slopes between points.
            var delta = new double[n - 1];
            // A rise over a tiny step ({0, 1e-300, 1}) overflowed to ∞ and the tangents became NaN: a secant beyond 2¹⁰¹⁶
            // is held there. The curve still passes through every point and stays monotone; it is just flatter at the
            // knots of such a step.
            for (int i = 0; i < n - 1; i++) delta[i] = Slope(_y[i + 1] - _y[i], _h[i]);

            // Initial tangents: endpoints use the adjacent secant, interior uses the average.
            _m = new double[n];
            _m[0] = delta[0];
            _m[n - 1] = delta[n - 2];
            // At a local extremum (the secants change sign) the tangent must be flat, or the curve overshoots.
            for (int i = 1; i < n - 1; i++)
                _m[i] = delta[i - 1] == 0 || delta[i] == 0 || (delta[i - 1] < 0) != (delta[i] < 0)
                    ? 0 : 0.5 * delta[i - 1] + 0.5 * delta[i];

            // Fritsch-Carlson adjustment to enforce monotonicity (no overshoot).
            for (int i = 0; i < n - 1; i++)
            {
                if (delta[i] == 0)
                {
                    _m[i] = 0;
                    _m[i + 1] = 0;
                }
                else
                {
                    double alpha = _m[i] / delta[i];
                    double beta = _m[i + 1] / delta[i];
                    double r = Math.Max(Math.Abs(alpha), Math.Abs(beta));
                    if (r > 2.1) // possibly outside the circle of radius 3 (r ≤ 2.1 means alpha² + beta² < 9)
                    {
                        // 3·(alpha, beta)/|(alpha, beta)|, formed so neither the squares nor a ratio beyond the double
                        // range (a 1e10 secant beside a 1e-300 one) overflows: 0·∞ gave NaN tangents.
                        if (double.IsInfinity(r))
                        {
                            // Only the infinite components' direction survives, and it is always outside the circle.
                            double ua = double.IsInfinity(alpha) ? Math.Sign(alpha) : 0;
                            double ub = double.IsInfinity(beta) ? Math.Sign(beta) : 0;
                            double unit = Math.Sqrt(ua * ua + ub * ub);
                            _m[i] = 3 * (ua / unit) * delta[i];
                            _m[i + 1] = 3 * (ub / unit) * delta[i];
                            continue;
                        }
                        double a = alpha / r, b = beta / r, norm = r * Math.Sqrt(a * a + b * b);
                        if (norm > 3)
                        {
                            _m[i] = 3 * (alpha / norm) * delta[i];
                            _m[i + 1] = 3 * (beta / norm) * delta[i];
                        }
                    }
                }
            }
        }

        public double Interpolate(double x)
        {
            int n = _x.Length;
            if (double.IsNaN(x)) return double.NaN;
            if (x <= _x[0]) return _yIn[0];
            if (x >= _x[n - 1]) return _yIn[n - 1];

            // Find the segment [lo, lo+1] containing x.
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_x[mid] <= x) lo = mid; else hi = mid;
            }

            if (x == _x[lo]) return _yIn[lo];
            double h = _h[lo];
            double t = Fraction(x, _x[lo], _x[lo + 1]);
            double t2 = t * t, t3 = t2 * t;
            // Cubic Hermite basis functions, in difference form: h00·y0 + h01·y1 rounded off a flat segment (0.1 came back
            // 0.10000000000000002), and the result is held between the segment's ends, as the monotone curve is.
            double h10 = t3 - 2 * t2 + t;
            double h01 = -2 * t3 + 3 * t2;
            double h11 = t3 - t2;
            double v = (_y[lo] + h01 * (_y[lo + 1] - _y[lo]) + h * (h10 * _m[lo] + h11 * _m[lo + 1])) / _sy;
            double a = _yIn[lo], b = _yIn[lo + 1];
            return Math.Min(Math.Max(v, Math.Min(a, b)), Math.Max(a, b));
        }

        // Coordinates beyond 2⁵⁰⁰ are stored scaled down by a power of two (so exactly) to just under 2⁵⁰⁰, and all-tiny ones
        // (below 2⁻⁵⁰⁰) scaled up: differences and slopes of points near ±double.MaxValue or 0 then cannot overflow.
        // Ordinary data keeps a scale of 1 and is untouched.
        private static double RangeScale(double[] v)
        {
            double max = 0;
            foreach (double d in v) max = Math.Max(max, Math.Abs(d));
            int e = (int)((BitConverter.DoubleToInt64Bits(max) >> 52) & 0x7FF) - 1023;
            // Just enough to bring the largest under 2⁵⁰⁰: a fixed 2⁻⁶⁰⁰ flushed small coordinates (1e-150 beside 1e160) to 0.
            if (max > Pow2(500)) return Pow2(499 - e);
            // All tiny: the largest up to [1, 2) (at most 2¹⁰²³ for subnormals), as a 1e9 rise over 1e-300 overflowed.
            if (max < Pow2(-500) && max > 0) return Pow2(-e);
            return 1.0;
        }

        // (b − a)·scale for a < b, without overflowing the difference (halves when it would). It can underflow to 0, for a
        // width far below the widest one; such a segment's slope is then held at MaxSlope.
        private static double Width(double a, double b, double scale)
        {
            double d = b - a;
            return double.IsInfinity(d) ? (0.5 * b - 0.5 * a) * (2 * scale) : d * scale;
        }

        // Where x lies in [a, b], from 0 to 1.
        private static double Fraction(double x, double a, double b)
        {
            double d = b - a;
            double t = double.IsInfinity(d) ? (0.5 * x - 0.5 * a) / (0.5 * b - 0.5 * a) : (x - a) / d;
            return Math.Min(Math.Max(t, 0), 1);
        }

        private static double Slope(double rise, double width)
            => rise == 0 ? 0 : width == 0 ? Math.Sign(rise) * MaxSlope : CapSlope(rise / width);

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        private const double MaxSlope = 7.022238808055922e305; // 2¹⁰¹⁶: three of them still fit in a double

        private static double CapSlope(double s) => Math.Abs(s) > MaxSlope ? Math.Sign(s) * MaxSlope : s;
    }
}
