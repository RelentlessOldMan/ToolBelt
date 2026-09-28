// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// A polynomial with real coefficients in ascending order (index i is the coefficient of xⁱ).
    /// Supports Horner evaluation, exact derivative and integral, least-squares fitting (via
    /// <see cref="LinearAlgebra"/>), and real-root finding for degree ≤ 3. Immutable.
    /// </summary>
    public sealed class Polynomial
    {
        private readonly double[] _coefficients;

        /// <param name="coefficients">Ascending order: c₀ + c₁x + c₂x² + … Trailing zeros are trimmed.</param>
        public Polynomial(params double[] coefficients)
        {
            if (coefficients is null) throw new ArgumentNullException(nameof(coefficients));
            int last = coefficients.Length - 1;
            while (last > 0 && coefficients[last] == 0) last--;
            _coefficients = new double[last + 1];
            Array.Copy(coefficients, _coefficients, last + 1);
        }

        public IReadOnlyList<double> Coefficients => _coefficients;

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

        /// <summary>Least-squares polynomial fit of the given degree through the sample points.</summary>
        public static Polynomial Fit(IReadOnlyList<double> x, IReadOnlyList<double> y, int degree)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            if (degree < 0) throw new ArgumentOutOfRangeException(nameof(degree), degree, "Degree must be non-negative.");
            if (x.Count < degree + 1) throw new ArgumentException($"Need at least {degree + 1} points to fit degree {degree}.");

            int m = degree + 1;
            // Normal equations: (VᵀV) c = Vᵀy, where V is the Vandermonde matrix. Sums of powers up to 2·degree.
            var powerSums = new double[2 * degree + 1];
            var rhs = new double[m];
            for (int k = 0; k < x.Count; k++)
            {
                double xp = 1;
                for (int p = 0; p < powerSums.Length; p++) { powerSums[p] += xp; xp *= x[k]; }
                double xq = 1;
                for (int i = 0; i < m; i++) { rhs[i] += y[k] * xq; xq *= x[k]; }
            }
            var normal = new double[m, m];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < m; j++)
                    normal[i, j] = powerSums[i + j];

            double[] coeffs = LinearAlgebra.Solve(normal, rhs);
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
            double disc = b * b - 4 * a * c;
            if (disc < 0) return Array.Empty<double>();
            if (disc == 0) return new[] { -b / (2 * a) };
            double sqrt = Math.Sqrt(disc);
            double r1 = (-b - sqrt) / (2 * a), r2 = (-b + sqrt) / (2 * a);
            return r1 <= r2 ? new[] { r1, r2 } : new[] { r2, r1 };
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

            double discriminant = q * q / 4.0 + p * p * p / 27.0;
            var roots = new List<double>();

            if (discriminant > 1e-12)
            {
                double sqrtD = Math.Sqrt(discriminant);
                double t = Cbrt(-q / 2 + sqrtD) + Cbrt(-q / 2 - sqrtD);
                roots.Add(t - shift);
            }
            else if (discriminant < -1e-12)
            {
                // Three distinct real roots (trigonometric method).
                double m = 2 * Math.Sqrt(-p / 3.0);
                double theta = Math.Acos(Clamp(3 * q / (p * m), -1, 1)) / 3.0;
                for (int k = 0; k < 3; k++)
                    roots.Add(m * Math.Cos(theta - 2 * Math.PI * k / 3.0) - shift);
            }
            else if (Math.Abs(p) < 1e-12)
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

            roots.Sort();
            return roots.ToArray();
        }

        private static double Cbrt(double x) => Math.Sign(x) * Math.Pow(Math.Abs(x), 1.0 / 3.0); // netstandard2.0 lacks Math.Cbrt
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
