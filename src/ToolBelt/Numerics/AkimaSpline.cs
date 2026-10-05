// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Akima spline interpolation (Akima 1970): a smooth (C¹) piecewise cubic whose slope at each point is a weighted
    /// average of the neighbouring segment slopes, weighted so that a sudden change in the data only affects the curve
    /// nearby. It wiggles far less than a natural cubic spline around steps and outliers, while still passing through
    /// every point and reproducing straight lines exactly. The <c>modified</c> variant ("makima") adds weights that
    /// stop overshoot where several consecutive points are equal. Unlike <see cref="MonotoneCubic"/> it does not
    /// guarantee monotonicity. x must be strictly increasing; inputs outside the range clamp to the end values, as in
    /// <see cref="MonotoneCubic"/>. Immutable.
    /// </summary>
    public sealed class AkimaSpline
    {
        private readonly double[] _x, _y, _t;

        public AkimaSpline(IReadOnlyList<double> x, IReadOnlyList<double> y, bool modified = false)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            int n = x.Count;
            if (n < 2) throw new ArgumentException("Need at least two points.", nameof(x));
            _x = new double[n];
            _y = new double[n];
            for (int i = 0; i < n; i++)
            {
                _x[i] = x[i];
                _y[i] = y[i];
                if (double.IsNaN(_x[i]) || double.IsNaN(_y[i])) throw new ArgumentException("Points must not be NaN.");
                if (i > 0 && !(_x[i] > _x[i - 1])) throw new ArgumentException("x must be strictly increasing.", nameof(x));
            }

            // Segment slopes m[0..n−2], stored with two extrapolated slopes on each side (offset 2).
            var m = new double[n + 3];
            for (int i = 0; i < n - 1; i++) m[i + 2] = (_y[i + 1] - _y[i]) / (_x[i + 1] - _x[i]);
            if (n == 2)
            {
                m[0] = m[1] = m[3] = m[4] = m[2];
            }
            else
            {
                m[1] = 2 * m[2] - m[3];
                m[0] = 2 * m[1] - m[2];
                m[n + 1] = 2 * m[n] - m[n - 1];
                m[n + 2] = 2 * m[n + 1] - m[n];
            }

            _t = new double[n];
            for (int i = 0; i < n; i++)
            {
                // Point i sits between segment slopes m[i+1] (left) and m[i+2] (right).
                double mL2 = m[i], mL1 = m[i + 1], mR1 = m[i + 2], mR2 = m[i + 3];
                double w1 = Math.Abs(mR2 - mR1), w2 = Math.Abs(mL1 - mL2);
                if (modified)
                {
                    w1 += Math.Abs(mR2 + mR1) / 2;
                    w2 += Math.Abs(mL1 + mL2) / 2;
                }
                _t[i] = w1 + w2 == 0 ? (mL1 + mR1) / 2 : (w1 * mL1 + w2 * mR1) / (w1 + w2);
            }
        }

        /// <summary>The interpolated value (clamped to the end values outside the data range).</summary>
        public double Interpolate(double x)
        {
            if (x <= _x[0]) return _y[0];
            if (x >= _x[_x.Length - 1]) return _y[_y.Length - 1];
            int i = Segment(x);
            double h = _x[i + 1] - _x[i], s = (x - _x[i]) / h;
            double h00 = (1 + 2 * s) * (1 - s) * (1 - s), h10 = s * (1 - s) * (1 - s);
            double h01 = s * s * (3 - 2 * s), h11 = s * s * (s - 1);
            return h00 * _y[i] + h10 * h * _t[i] + h01 * _y[i + 1] + h11 * h * _t[i + 1];
        }

        /// <summary>The first derivative (0 outside the data range, matching the clamped values).</summary>
        public double Derivative(double x)
        {
            if (x < _x[0] || x > _x[_x.Length - 1]) return 0;
            if (x == _x[_x.Length - 1]) return _t[_t.Length - 1];
            int i = Segment(x);
            double h = _x[i + 1] - _x[i], s = (x - _x[i]) / h;
            double d00 = 6 * s * s - 6 * s, d10 = 3 * s * s - 4 * s + 1, d01 = -6 * s * s + 6 * s, d11 = 3 * s * s - 2 * s;
            return (d00 * _y[i] + d01 * _y[i + 1]) / h + d10 * _t[i] + d11 * _t[i + 1];
        }

        /// <summary>The slope the spline uses at each data point.</summary>
        public IReadOnlyList<double> Slopes => _t;

        private int Segment(double x)
        {
            int lo = 0, hi = _x.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_x[mid] <= x) lo = mid; else hi = mid;
            }
            return lo;
        }
    }
}
