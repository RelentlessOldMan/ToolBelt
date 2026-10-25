// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A polynomial with real coefficients in ascending order (index i is the coefficient of xⁱ).
    /// Supports Horner evaluation, exact derivative and integral, least-squares fitting (Householder QR on
    /// centred, scaled x), and real-root finding for degree ≤ 3. Immutable.
    /// </summary>
    public sealed class Polynomial
    {
        private readonly double[] _coefficients;

        /// <param name="coefficients">Ascending order: c₀ + c₁x + c₂x² + … Trailing zeros are trimmed; an empty
        /// array is the zero polynomial.</param>
        public Polynomial(params double[] coefficients)
        {
            if (coefficients is null) throw new ArgumentNullException(nameof(coefficients));
            if (coefficients.Length == 0) coefficients = new[] { 0.0 };
            int last = coefficients.Length - 1;
            while (last > 0 && coefficients[last] == 0) last--;
            _coefficients = new double[last + 1];
            Array.Copy(coefficients, _coefficients, last + 1);
            Coefficients = Array.AsReadOnly(_coefficients); // the bare array could be cast back and written
        }

        public IReadOnlyList<double> Coefficients { get; }

        /// <summary>The degree (0 for a constant, including the zero polynomial).</summary>
        public int Degree => _coefficients.Length - 1;

        /// <summary>Evaluates the polynomial at <paramref name="x"/> using Horner's method.</summary>
        public double Evaluate(double x)
        {
            double result = 0;
            for (int i = _coefficients.Length - 1; i >= 0; i--)
                result = result * x + _coefficients[i];
            return result;
        }

        public Polynomial Derivative()
        {
            if (_coefficients.Length == 1) return new Polynomial(0.0);
            var d = new double[_coefficients.Length - 1];
            for (int i = 1; i < _coefficients.Length; i++) d[i - 1] = _coefficients[i] * i;
            return new Polynomial(d);
        }

        public Polynomial Integral(double constant = 0)
        {
            var integral = new double[_coefficients.Length + 1];
            integral[0] = constant;
            for (int i = 0; i < _coefficients.Length; i++) integral[i + 1] = _coefficients[i] / (i + 1);
            return new Polynomial(integral);
        }

        /// <summary>
        /// Least-squares polynomial fit of the given degree through the sample points. Solved by Householder QR
        /// on x centred and scaled to [-1, 1] (not the normal equations, which square the condition number), then
        /// expanded back to ascending coefficients in x. Throws <see cref="InvalidOperationException"/> when the x
        /// values cannot determine the fit (fewer distinct x than degree + 1).
        /// </summary>
        public static Polynomial Fit(IReadOnlyList<double> x, IReadOnlyList<double> y, int degree)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            if (degree < 0) throw new ArgumentOutOfRangeException(nameof(degree), degree, "Degree must be non-negative.");
            if (x.Count - 1 < degree) // not x.Count < degree + 1, which wrapped for int.MaxValue
                throw new ArgumentException($"Need at least {(long)degree + 1} points to fit degree {degree}.");

            int n = x.Count, m = degree + 1;
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int k = 0; k < n; k++)
            {
                if (double.IsNaN(x[k]) || double.IsInfinity(x[k])) throw new ArgumentException($"x[{k}] is not finite.", nameof(x));
                if (double.IsNaN(y[k]) || double.IsInfinity(y[k])) throw new ArgumentException($"y[{k}] is not finite.", nameof(y));
                if (x[k] < lo) lo = x[k];
                if (x[k] > hi) hi = x[k];
            }
            // u = (x − shift) / scale lies in [-1, 1]. Halves first, so a range near ±double.MaxValue cannot overflow.
            double shift = 0.5 * lo + 0.5 * hi, scale = 0.5 * hi - 0.5 * lo;
            if (scale == 0) scale = 1; // all x equal: only degree 0 is determined, and the rank check reports the rest

            var a = new double[n, m];
            var b = new double[n];
            for (int k = 0; k < n; k++)
            {
                double u = (x[k] - shift) / scale, up = 1;
                for (int j = 0; j < m; j++) { a[k, j] = up; up *= u; }
                b[k] = y[k];
            }

            // Householder QR: reduce a to upper-triangular R, applying the same reflections to b.
            var diag = new double[m];
            for (int j = 0; j < m; j++)
            {
                double norm = 0;
                for (int k = j; k < n; k++) norm = Hypot(norm, a[k, j]);
                double alpha = a[j, j] > 0 ? -norm : norm;
                diag[j] = alpha;
                if (norm == 0) continue;
                a[j, j] -= alpha;                 // v = column − alpha·e_j, stored in place
                double vtv = -2 * alpha * a[j, j]; // vᵀv = 2·norm·(norm + |a_jj|)
                for (int c = j + 1; c < m; c++)
                {
                    double dot = 0;
                    for (int k = j; k < n; k++) dot += a[k, j] * a[k, c];
                    double f = 2 * dot / vtv;
                    for (int k = j; k < n; k++) a[k, c] -= f * a[k, j];
                }
                double db = 0;
                for (int k = j; k < n; k++) db += a[k, j] * b[k];
                double fb = 2 * db / vtv;
                for (int k = j; k < n; k++) b[k] -= fb * a[k, j];
            }

            // Rank check relative to the largest diagonal; the columns (powers of u in [-1, 1]) are well scaled.
            double maxDiag = 0;
            for (int j = 0; j < m; j++) maxDiag = Math.Max(maxDiag, Math.Abs(diag[j]));
            for (int j = 0; j < m; j++)
                if (!(Math.Abs(diag[j]) > maxDiag * n * 1e-13))
                    throw new InvalidOperationException($"The x values do not determine a degree-{degree} fit (too few distinct x).");

            var cu = new double[m];
            for (int j = m - 1; j >= 0; j--)
            {
                double sum = b[j];
                for (int c = j + 1; c < m; c++) sum -= a[j, c] * cu[c];
                cu[j] = sum / diag[j];
            }

            // Expand Σ cu_j·((x − shift)/scale)^j into powers of x: Horner with the linear factor p0 + p1·x.
            var coeffs = new double[m];
            double p0 = -shift / scale, p1 = 1 / scale;
            coeffs[0] = cu[m - 1];
            for (int j = m - 2, len = 1; j >= 0; j--, len++)
            {
                for (int k = len; k >= 1; k--) coeffs[k] = coeffs[k] * p0 + coeffs[k - 1] * p1;
                coeffs[0] = coeffs[0] * p0 + cu[j];
            }
            return new Polynomial(coeffs);
        }

        /// <summary>The real roots, for degree 1 to 3. Degree 0 has none; degree ≥ 4 is not supported.</summary>
        public double[] RealRoots()
        {
            switch (Degree)
            {
                case 0:
                    return Array.Empty<double>();
                case 1:
                    return new[] { -_coefficients[0] / _coefficients[1] };
                case 2:
                    return QuadraticRoots();
                case 3:
                    return CubicRoots();
                default:
                    throw new NotSupportedException("Real-root finding is supported for degree 1 to 3 only.");
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = _coefficients.Length - 1; i >= 0; i--)
            {
                double c = _coefficients[i];
                if (c == 0 && _coefficients.Length > 1) continue;
                if (sb.Length > 0) sb.Append(c < 0 ? " - " : " + ");
                else if (c < 0) sb.Append('-');
                sb.Append(Math.Abs(c).ToString(CultureInfo.InvariantCulture));
                if (i >= 1) sb.Append('x');
                if (i >= 2) sb.Append('^').Append(i.ToString(CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? "0" : sb.ToString();
        }

        private double[] QuadraticRoots()
        {
            double a = _coefficients[2], b = _coefficients[1], c = _coefficients[0];
            // Scale by a power of two (exact, roots unchanged) so the largest coefficient is in [1, 2): b² can then neither
            // overflow (x² + 1e200x + 1 gave [−∞, −0]) nor underflow (−1e-320x² + 1e-320 gave [−0]).
            int e = Exponent(Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(c))));
            a = ScaleB(a, -e); b = ScaleB(b, -e); c = ScaleB(c, -e);
            double disc = b * b - 4 * a * c;
            if (disc < 0) return Array.Empty<double>();
            if (disc == 0) return new[] { -b / (2 * a) };
            // Stable form: (−b ± √D)/2a loses the small root to cancellation when b² ≫ 4ac.
            double q = -0.5 * (b + (b >= 0 ? Math.Sqrt(disc) : -Math.Sqrt(disc)));
            double r1 = q / a, r2 = c / q;
            return r1 <= r2 ? new[] { r1, r2 } : new[] { r2, r1 };
        }

        private const double Epsilon = 2.220446049250313e-16;

        private double[] CubicRoots()
        {
            // Monic x³ + bx² + cx + d.
            double b = _coefficients[2] / _coefficients[3];
            double c = _coefficients[1] / _coefficients[3];
            double d = _coefficients[0] / _coefficients[3];

            // Substitute x = σy with σ a power of two above every root's natural size (|b|, √|c|, ∛|d|): the cubic in y
            // has coefficients of at most 1, so nothing below overflows (roots near 1e120 cubed did) and the tolerances
            // are relative to a unit-sized problem. Scaling by a power of two is exact.
            double size = Math.Max(Math.Abs(b), Math.Max(Math.Sqrt(Math.Abs(c)), Cbrt(Math.Abs(d))));
            if (size == 0) return new[] { 0.0 }; // x³
            int k = Exponent(size) + 1;
            double B = ScaleB(b, -k), C = ScaleB(c, -2 * k), D = ScaleB(d, -3 * k);

            // Depress to t³ + pt + q via y = t − B/3.
            double p = C - B * B / 3.0;
            double q = 2 * B * B * B / 27.0 - B * C / 3.0 + D;
            double shift = B / 3.0;

            // "Zero" is judged against the round-off of the discriminant: q and p carry errors of a few ε times the size
            // of the terms that formed them, which moves q²/4 + p³/27 by about |q|·δq/2 + p²·δp/9. (A looser bound merged
            // roots 1e-5 apart into one.)
            double pScale = Math.Abs(C) + B * B / 3.0;
            double qScale = 2 * Math.Abs(B * B * B) / 27.0 + Math.Abs(B * C) / 3.0 + Math.Abs(D);
            double discriminant = q * q / 4.0 + p * p * p / 27.0;
            double discTol = 32 * Epsilon * (Math.Abs(q) * qScale / 2.0 + p * p * pScale / 9.0);
            var roots = new List<double>();

            if (discriminant > discTol)
            {
                double sqrtD = Math.Sqrt(discriminant);
                double t = Cbrt(-q / 2 + sqrtD) + Cbrt(-q / 2 - sqrtD);
                roots.Add(Polish(ScaleB(t - shift, k), b, c, d));
            }
            else if (discriminant < -discTol)
            {
                // Three distinct real roots (trigonometric method). Only the largest is taken from it: a root much
                // smaller than σ is lost in y (1 and 2 next to 1e110), so the other two come from deflating the cubic.
                double m = 2 * Math.Sqrt(-p / 3.0);
                double theta = Math.Acos(Clamp(3 * q / (p * m), -1, 1)) / 3.0;
                double largest = 0;
                for (int i = 0; i < 3; i++)
                {
                    double y = m * Math.Cos(theta - 2 * Math.PI * i / 3.0) - shift;
                    if (Math.Abs(y) > Math.Abs(largest)) largest = y;
                }
                double x1 = Polish(ScaleB(largest, k), b, c, d);
                roots.Add(x1);
                AddDeflated(roots, x1, b, c, d, real: true);
            }
            else if (Math.Abs(p) <= 1e-8 * pScale)
            {
                // p ≈ 0 and discriminant ≈ 0 ⇒ triple root at t = 0.
                roots.Add(ScaleB(-shift, k));
            }
            else
            {
                // Discriminant ~0: a double root and a single root (as far as y can tell).
                double ySimple = 3 * q / p - shift;
                double yDouble = -3 * q / (2 * p) - shift;
                if (Math.Abs(ySimple) >= Math.Abs(yDouble))
                {
                    // The "double" root may be two roots too small for y to separate (1e-300x³ + x² − 1 has −1e300 and
                    // ±1): deflate and let the quadratic decide.
                    double x1 = Polish(ScaleB(ySimple, k), b, c, d);
                    roots.Add(x1);
                    AddDeflated(roots, x1, b, c, d, real: false);
                }
                else
                {
                    // The repeated root dominates; the simple one is −d / r², which keeps it when it is tiny.
                    double r = ScaleB(yDouble, k);
                    roots.Add(r);
                    roots.Add(Polish(-d / r / r, b, c, d));
                }
            }

            roots.Sort();
            return roots.ToArray();
        }

        // The two roots left after dividing the monic cubic by (x − x1): their product is −d/x1 and their sum
        // (c − product)/x1, both free of cancellation when x1 is the largest root. When the roots are known to be
        // real the discriminant is clamped at 0; otherwise a discriminant within round-off of 0 is a double root and a
        // clearly negative one a complex pair.
        private static void AddDeflated(List<double> roots, double x1, double b, double c, double d, bool real)
        {
            double product = -d / x1;
            double half = -0.5 * ((c - product) / x1); // z² + 2·half·z + product = 0
            double size = Math.Max(Math.Abs(half), Math.Sqrt(Math.Abs(product)));
            if (size == 0) { roots.Add(0); return; }
            if (double.IsNaN(size) || double.IsInfinity(size)) return;
            int e = Exponent(size);
            double h = ScaleB(half, -e), pr = ScaleB(product, -2 * e);
            double disc = h * h - pr;
            double tol = 32 * Epsilon * (h * h + Math.Abs(pr));
            if (real) disc = Math.Max(disc, 0);
            else if (disc < -tol) return;
            else if (disc <= tol) { roots.Add(Polish(-half, b, c, d)); return; }
            double z1 = ScaleB(-(h + (h >= 0 ? Math.Sqrt(disc) : -Math.Sqrt(disc))), e);
            double z2 = z1 == 0 ? 0 : product / z1;
            roots.Add(Polish(z1, b, c, d));
            roots.Add(Polish(z2, b, c, d));
        }

        // Newton on the monic cubic, keeping a step only if it shrinks the residual; skipped where x³ overflows.
        private static double Polish(double x, double b, double c, double d)
        {
            for (int it = 0; it < 3; it++)
            {
                double f = ((x + b) * x + c) * x + d;
                double df = (3 * x + 2 * b) * x + c;
                if (f == 0 || df == 0 || double.IsNaN(f) || double.IsInfinity(f) || double.IsNaN(df) || double.IsInfinity(df)) break;
                double next = x - f / df;
                double fn = ((next + b) * next + c) * next + d;
                if (!(Math.Abs(fn) < Math.Abs(f))) break;
                x = next;
            }
            return x;
        }

        // floor(log2 x) for a positive finite x, subnormals included.
        private static int Exponent(double x)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
            return e == 0 ? Exponent(x * 18446744073709551616.0) - 64 : e - 1023;
        }

        // x·2^k without overflowing the factor itself (2^k alone is out of range for |k| > 1023).
        private static double ScaleB(double x, int k)
        {
            while (k > 1023) { x *= Pow2(1023); k -= 1023; }
            while (k < -1022) { x *= Pow2(-1022); k += 1022; }
            return x * Pow2(k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        private static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            if (a < b) { double t = a; a = b; b = t; }
            if (a == 0) return 0;
            double r = b / a;
            return a * Math.Sqrt(1 + r * r);
        }

        private static double Cbrt(double x) => Math.Sign(x) * Math.Pow(Math.Abs(x), 1.0 / 3.0); // netstandard2.0 lacks Math.Cbrt
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
