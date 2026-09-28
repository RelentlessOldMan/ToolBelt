// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Monotone cubic (Fritsch-Carlson) interpolation: smooth like a spline but guaranteed not to overshoot
    /// between points, so it never invents a non-physical value — which matters a great deal for calibration
    /// curves. The x values must be strictly increasing; inputs outside the range clamp to the endpoints.
    /// Immutable.
    /// </summary>
    public sealed class MonotoneCubic
    {
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly double[] _m; // tangents

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
                if (i > 0 && x[i] <= x[i - 1]) throw new ArgumentException("x values must be strictly increasing.", nameof(x));
                _x[i] = x[i];
                _y[i] = y[i];
            }

            // Secant slopes between points.
            var delta = new double[n - 1];
            for (int i = 0; i < n - 1; i++) delta[i] = (_y[i + 1] - _y[i]) / (_x[i + 1] - _x[i]);

            // Initial tangents: endpoints use the adjacent secant, interior uses the average.
            _m = new double[n];
            _m[0] = delta[0];
            _m[n - 1] = delta[n - 2];
            for (int i = 1; i < n - 1; i++) _m[i] = (delta[i - 1] + delta[i]) / 2.0;

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
            if (x <= _x[0]) return _y[0];
            if (x >= _x[n - 1]) return _y[n - 1];

            // Find the segment [lo, lo+1] containing x.
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_x[mid] <= x) lo = mid; else hi = mid;
            }

            double h = _x[lo + 1] - _x[lo];
            double t = (x - _x[lo]) / h;
            double t2 = t * t, t3 = t2 * t;
            // Cubic Hermite basis functions.
            double h00 = 2 * t3 - 3 * t2 + 1;
            double h10 = t3 - 2 * t2 + t;
            double h01 = -2 * t3 + 3 * t2;
            double h11 = t3 - t2;
            return h00 * _y[lo] + h10 * h * _m[lo] + h01 * _y[lo + 1] + h11 * h * _m[lo + 1];
        }
    }
}
