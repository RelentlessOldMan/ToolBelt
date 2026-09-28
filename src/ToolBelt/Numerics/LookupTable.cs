// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>How a <see cref="LookupTable"/> behaves for inputs outside its known range.</summary>
    public enum Extrapolation
    {
        /// <summary>Return the nearest endpoint value.</summary>
        Clamp,
        /// <summary>Continue the slope of the nearest end segment.</summary>
        Linear,
        /// <summary>Throw an exception.</summary>
        Throw,
    }

    /// <summary>
    /// A 1-D calibration table: piecewise-linear interpolation between known (x, y) points with an explicit
    /// extrapolation policy, plus inverse lookup on a monotone table. Every calibration path needs this and
    /// hand-rolls it with an off-by-one at the last segment. The x values must be strictly increasing.
    /// </summary>
    public sealed class LookupTable
    {
        private readonly double[] _x;
        private readonly double[] _y;
        private readonly Extrapolation _policy;

        public LookupTable(IReadOnlyList<double> x, IReadOnlyList<double> y, Extrapolation policy = Extrapolation.Clamp)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            if (x.Count < 2) throw new ArgumentException("At least two points are required.", nameof(x));

            _x = new double[x.Count];
            _y = new double[y.Count];
            for (int i = 0; i < x.Count; i++)
            {
                if (i > 0 && x[i] <= x[i - 1])
                    throw new ArgumentException("x values must be strictly increasing.", nameof(x));
                _x[i] = x[i];
                _y[i] = y[i];
            }
            _policy = policy;
        }

        /// <summary>Interpolates the y value at <paramref name="x"/>, applying the extrapolation policy at the ends.</summary>
        public double Interpolate(double x)
        {
            int n = _x.Length;
            if (x < _x[0]) return Extrapolate(x, 0, 1, "below");
            if (x > _x[n - 1]) return Extrapolate(x, n - 2, n - 1, "above");

            int hi = UpperSegment(_x, x);
            return Lerp(_x[hi - 1], _y[hi - 1], _x[hi], _y[hi], x);
        }

        /// <summary>Finds the x that maps to <paramref name="y"/>; requires the y values to be monotone.</summary>
        public double InverseLookup(double y)
        {
            bool increasing = _y[_y.Length - 1] > _y[0];
            for (int i = 1; i < _y.Length; i++)
            {
                bool ok = increasing ? _y[i] >= _y[i - 1] : _y[i] <= _y[i - 1];
                if (!ok) throw new InvalidOperationException("Inverse lookup requires monotone y values.");
            }

            double first = _y[0], last = _y[_y.Length - 1];
            double lo = Math.Min(first, last), hiv = Math.Max(first, last);
            if (y < lo || y > hiv)
                throw new ArgumentOutOfRangeException(nameof(y), y, "Value is outside the table's y range.");

            for (int i = 1; i < _y.Length; i++)
            {
                double a = _y[i - 1], b = _y[i];
                if ((y >= Math.Min(a, b)) && (y <= Math.Max(a, b)))
                    return Lerp(a, _x[i - 1], b, _x[i], y);
            }
            return _x[_x.Length - 1];
        }

        private double Extrapolate(double x, int i0, int i1, string side)
        {
            if (_policy == Extrapolation.Throw)
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Value is {side} the table range and extrapolation is disabled.");
            if (_policy == Extrapolation.Clamp)
                return side == "below" ? _y[0] : _y[_x.Length - 1];
            return Lerp(_x[i0], _y[i0], _x[i1], _y[i1], x); // Linear
        }

        private static int UpperSegment(double[] arr, double value)
        {
            int lo = 1, hi = arr.Length - 1;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (arr[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static double Lerp(double x0, double y0, double x1, double y1, double x)
            => y0 + (y1 - y0) * (x - x0) / (x1 - x0);
    }
}
