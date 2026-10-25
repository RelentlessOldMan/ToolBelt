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
            // Scale by a power of two (exact, roots unchanged) so b² cannot overflow: x² + 1e200x + 1 gave [−∞, −0].
            double s = PowerOfTwoScale(Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(c))));
            a *= s; b *= s; c *= s;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return Array.Empty<double>();
            if (disc == 0) return new[] { -b / (2 * a) };
            // Stable form: (−b ± √D)/2a loses the small root to cancellation when b² ≫ 4ac.
            double q = -0.5 * (b + (b >= 0 ? Math.Sqrt(disc) : -Math.Sqrt(disc)));
            double r1 = q / a, r2 = c / q;
            return r1 <= r2 ? new[] { r1, r2 } : new[] { r2, r1 };
        }

        // 2^−e for the binary exponent e of a normal positive m, so m·2^−e is in [1, 2); 1 for a subnormal.
        private static double PowerOfTwoScale(double m)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(m) >> 52) & 0x7FF) - 1023;
            return e == -1023 ? 1 : Math.Pow(2, -e);
        }

        private double[] CubicRoots()
        {
            // Normalize to x³ + bx² + cx + d, then depress to t³ + pt + q via x = t - b/3.
            double b = _coefficients[2] / _coefficients[3];
            double c = _coefficients[1] / _coefficients[3];
            double d = _coefficients[0] / _coefficients[3];
            double p = c - b * b / 3.0;
            double q = 2 * b * b * b / 27.0 - b * c / 3.0 + d;
            double shift = b / 3.0;

            // "Zero" is judged against the size of the terms that formed p, q and the discriminant (their
            // round-off), not an absolute constant: roots near 0.001 have a discriminant near 1e-20.
            double pScale = Math.Abs(c) + b * b / 3.0;
            double qScale = 2 * Math.Abs(b * b * b) / 27.0 + Math.Abs(b * c) / 3.0 + Math.Abs(d);
            double discriminant = q * q / 4.0 + p * p * p / 27.0;
            double discTol = 1e-12 * (qScale * qScale / 4.0 + pScale * pScale * pScale / 27.0);
            var roots = new List<double>();

            if (discriminant > discTol)
            {
                double sqrtD = Math.Sqrt(discriminant);
                double t = Cbrt(-q / 2 + sqrtD) + Cbrt(-q / 2 - sqrtD);
                roots.Add(t - shift);
            }
            else if (discriminant < -discTol)
            {
                // Three distinct real roots (trigonometric method).
                double m = 2 * Math.Sqrt(-p / 3.0);
                double theta = Math.Acos(Clamp(3 * q / (p * m), -1, 1)) / 3.0;
                for (int k = 0; k < 3; k++)
                    roots.Add(m * Math.Cos(theta - 2 * Math.PI * k / 3.0) - shift);
            }
            else if (Math.Abs(p) <= 1e-8 * pScale)
            {
                // p ≈ 0 and discriminant ≈ 0 ⇒ triple root at t = 0.
                roots.Add(-shift);
            }
            else
            {
                // Discriminant ~0: a double root and a single root.
                double t1 = 3 * q / p;          // the simple root
                double t2 = -3 * q / (2 * p);   // the repeated root
                roots.Add(t1 - shift);
                roots.Add(t2 - shift);
            }

            // Polish each root with Newton on the monic cubic, keeping a step only if it shrinks the residual.
            for (int r = 0; r < roots.Count; r++)
            {
                double x = roots[r];
                for (int it = 0; it < 3; it++)
                {
                    double f = ((x + b) * x + c) * x + d;
                    double df = (3 * x + 2 * b) * x + c;
                    if (f == 0 || df == 0) break;
                    double next = x - f / df;
                    double fn = ((next + b) * next + c) * next + d;
                    if (!(Math.Abs(fn) < Math.Abs(f))) break;
                    x = next;
                }
                roots[r] = x;
            }

            roots.Sort();
            return roots.ToArray();
        }

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
