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
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly double[] _m; // tangents
        private readonly double _sx, _sy; // the stored x and y are the inputs times these (see RangeScale)
        private readonly double[] _yIn;   // the y given, returned exactly at the knots (scaling can flush a tiny y to 0)

        public MonotoneCubic(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
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
            for (int i = 0; i < n; i++) { _x[i] *= _sx; _y[i] *= _sy; }

            // Secant slopes between points.
            var delta = new double[n - 1];
            for (int i = 0; i < n - 1; i++) delta[i] = (_y[i + 1] - _y[i]) / (_x[i + 1] - _x[i]);

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
                    double s = alpha * alpha + beta * beta;
                    if (s > 9)
                    {
                        double tau = 3.0 / Math.Sqrt(s);
                        _m[i] = tau * alpha * delta[i];
                        _m[i + 1] = tau * beta * delta[i];
                    }
                }
            }
        }

        public double Interpolate(double x)
        {
            int n = _x.Length;
            x *= _sx;
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
            double h = _x[lo + 1] - _x[lo];
            double t = (x - _x[lo]) / h;
            double t2 = t * t, t3 = t2 * t;
            // Cubic Hermite basis functions.
            double h00 = 2 * t3 - 3 * t2 + 1;
            double h10 = t3 - 2 * t2 + t;
            double h01 = -2 * t3 + 3 * t2;
            double h11 = t3 - t2;
            return (h00 * _y[lo] + h10 * h * _m[lo] + h01 * _y[lo + 1] + h11 * h * _m[lo + 1]) / _sy;
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

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
    }
}
