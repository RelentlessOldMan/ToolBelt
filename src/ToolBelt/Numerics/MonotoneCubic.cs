// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Numerics;

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
            double t = Fraction(x, _x[lo], _x[lo + 1]);
            // The cubic Hermite curve in difference form (h00·y0 + h01·y1 rounded off a flat segment: 0.1 came back
            // 0.10000000000000002), rounded once, and held between the segment's ends, as the monotone curve is.
            double v = Evaluate(lo, t) / _sy;
            double a = _yIn[lo], b = _yIn[lo + 1];
            return Math.Min(Math.Max(v, Math.Min(a, b)), Math.Max(a, b));
        }

        // The scaled curve at t on segment lo, correctly rounded. Rounding to nearest is monotone and t only grows with x,
        // so the result never steps back inside a segment: the plain double evaluation, a few ulps off, went down by an ulp
        // on increasing data (in 46 of 3,000 random tables). A double-double evaluation settles almost every t; the rest,
        // too near a rounding boundary to tell, are evaluated exactly.
        private double Evaluate(int lo, double t)
        {
            double y0 = _y[lo], h = _h[lo];
            TwoSum(_y[lo + 1], -y0, out double dh, out double dl);   // D = y1 − y0
            TwoProd(h, _m[lo], out double ah, out double al);        // A = h·m0
            TwoProd(h, _m[lo + 1], out double bh, out double bl);    // B = h·m1
            // v = y0 + t·(A + t·(c2 + t·c3)), with c2 = 3D − 2A − B and c3 = A + B − 2D
            Add(ah, al, bh, bl, out double sh, out double sl);
            Add(sh, sl, -2 * dh, -2 * dl, out double c3h, out double c3l);
            Mul(dh, dl, 3, out double d3h, out double d3l);
            Add(d3h, d3l, -sh, -sl, out double c2h, out double c2l);
            Add(c2h, c2l, -ah, -al, out c2h, out c2l);
            Mul(c3h, c3l, t, out double rh, out double rl);
            Add(rh, rl, c2h, c2l, out rh, out rl);
            Mul(rh, rl, t, out rh, out rl);
            Add(rh, rl, ah, al, out rh, out rl);
            Mul(rh, rl, t, out rh, out rl);
            Add(rh, rl, y0, 0, out rh, out rl);
            // Each double-double step is off by a few 2⁻¹⁰⁶ of its operands; 2⁻⁹⁶ of their sizes covers them all, and
            // 2⁻¹⁰⁴⁰ the error terms that underflow.
            double ad = Math.Abs(dh), aa = Math.Abs(ah), ab = Math.Abs(bh);
            double size = Math.Abs(y0) + t * (aa + t * (3 * ad + 2 * aa + ab + t * (2 * ad + aa + ab)));
            double err = 2 * (size * 1.262177448353619e-29 + 8.0e-314); // 2⁻⁹⁶, and about 2⁻¹⁰⁴⁰
            double v = rh + rl, e = rl - (v - rh);
            if (v + (e - err) == v && v + (e + err) == v) return v;
            return Exact(lo, t);
        }

        private double Exact(int lo, double t)
        {
            Dyadic y0 = Dyadic.Of(_y[lo]), h = Dyadic.Of(_h[lo]), tt = Dyadic.Of(t);
            Dyadic d = Dyadic.Of(_y[lo + 1]) + y0.Times(-1);
            Dyadic a = h * Dyadic.Of(_m[lo]), b = h * Dyadic.Of(_m[lo + 1]);
            Dyadic c3 = a + b + d.Times(-2), c2 = d.Times(3) + a.Times(-2) + b.Times(-1);
            Dyadic v = y0 + tt * (a + tt * (c2 + tt * c3));
            return v.ToDouble();
        }

        private static void TwoSum(double a, double b, out double s, out double e)
        {
            s = a + b;
            double bp = s - a;
            e = (a - (s - bp)) + (b - bp);
        }

        // p + e = a·b exactly (Dekker's product), short of underflow. A tangent held at 2¹⁰¹⁶ would overflow the split,
        // so it is traded against the other factor by 2¹²⁸ first (the widths are under 2⁵⁰², so that stays in range).
        private static void TwoProd(double a, double b, out double p, out double e)
        {
            if (Math.Abs(b) > 8.452712498170644e270) { a *= 3.402823669209385e38; b *= 2.938735877055719e-39; } // 2⁹⁰⁰; 2¹²⁸
            p = a * b;
            Split(a, out double ah, out double al);
            Split(b, out double bh, out double bl);
            e = ((ah * bh - p) + ah * bl + al * bh) + al * bl;
        }

        private static void Split(double a, out double hi, out double lo)
        {
            double t = 134217729.0 * a;   // 2^27 + 1
            hi = t - (t - a);
            lo = a - hi;
        }

        private static void Add(double ah, double al, double bh, double bl, out double h, out double l)
        {
            TwoSum(ah, bh, out double s, out double e);
            e += al + bl;
            h = s + e;
            l = e - (h - s);
        }

        private static void Mul(double ah, double al, double b, out double h, out double l)
        {
            TwoProd(ah, b, out double p, out double e);
            e += al * b;
            h = p + e;
            l = e - (h - p);
        }

        // An exact binary value M·2^E, for the rare evaluation the double-double one cannot settle.
        private readonly struct Dyadic
        {
            private readonly BigInteger _m;
            private readonly long _e;

            private Dyadic(BigInteger m, long e) { _m = m; _e = e; }

            public static Dyadic Of(double x)
            {
                long bits = BitConverter.DoubleToInt64Bits(x);
                int be = (int)((bits >> 52) & 0x7FF);
                long m = bits & 0xFFFFFFFFFFFFFL;
                if (be == 0) be = 1; else m |= 1L << 52;
                return new Dyadic(bits < 0 ? -m : m, be - 1075);
            }

            public Dyadic Times(int k) => new Dyadic(_m * k, _e);

            public static Dyadic operator *(Dyadic a, Dyadic b) => new Dyadic(a._m * b._m, a._e + b._e);

            public static Dyadic operator +(Dyadic a, Dyadic b)
            {
                if (a._m.IsZero) return b;
                if (b._m.IsZero) return a;
                long e = Math.Min(a._e, b._e);
                return new Dyadic((a._m << (int)(a._e - e)) + (b._m << (int)(b._e - e)), e);
            }

            // Rounded once to the nearest double (ties to even), subnormals and overflow included.
            public double ToDouble()
            {
                BigInteger m = _m;
                if (m.IsZero) return 0;
                bool neg = m.Sign < 0;
                if (neg) m = -m;
                long top = BitLength(m) - 1 + _e;              // floor(log2 |M·2^E|)
                if (top > 1023) return neg ? double.NegativeInfinity : double.PositiveInfinity;
                long q = Math.Max(top - 52, -1074);            // the result's ulp
                long shift = q - _e;
                BigInteger kept;
                if (shift <= 0) kept = m << (int)-shift;
                else
                {
                    kept = m >> (int)shift;
                    BigInteger rest = m - (kept << (int)shift), half = BigInteger.One << (int)(shift - 1);
                    if (rest > half || (rest == half && !kept.IsEven)) kept += 1;
                }
                double r = (long)kept;                         // at most 2^53, so exact; 2^53·2^971 overflows to ∞
                r = q >= -1022 ? r * Pow2((int)q) : r * Pow2(-1022) * Pow2((int)(q + 1022));
                return neg ? -r : r;
            }

            private static long BitLength(BigInteger m)
            {
                byte[] b = m.ToByteArray();
                int i = b.Length - 1;
                while (i > 0 && b[i] == 0) i--;
                long n = 8L * i;
                for (int t = b[i]; t != 0; t >>= 1) n++;
                return n;
            }
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
