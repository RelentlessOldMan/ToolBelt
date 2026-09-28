// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Numerical differentiation over sampled data (uniform or uneven spacing) and over a function.
    /// Sample-based methods use second-order central differences in the interior and second-order one-sided
    /// differences at the ends; the uneven case differentiates the local quadratic interpolant, which is
    /// exact for quadratics. Complements the existing 2-D gradient (the grid case).
    /// </summary>
    public static class Differentiation
    {
        /// <summary>Derivative at each uniformly spaced sample.</summary>
        public static double[] Sample(IReadOnlyList<double> y, double dx)
        {
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (y.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(y));
            if (dx <= 0) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive.");

            int n = y.Count;
            var d = new double[n];
            if (n == 2)
            {
                d[0] = d[1] = (y[1] - y[0]) / dx;
                return d;
            }
            d[0] = (-3 * y[0] + 4 * y[1] - y[2]) / (2 * dx);                     // 2nd-order forward
            d[n - 1] = (3 * y[n - 1] - 4 * y[n - 2] + y[n - 3]) / (2 * dx);      // 2nd-order backward
            for (int i = 1; i < n - 1; i++)
                d[i] = (y[i + 1] - y[i - 1]) / (2 * dx);                          // 2nd-order central
            return d;
        }

        /// <summary>Derivative at each sample for arbitrary ascending x positions.</summary>
        public static double[] Sample(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            if (x.Count < 3) throw new ArgumentException("At least three samples are required for uneven spacing.", nameof(x));

            int n = x.Count;
            var d = new double[n];
            // Each interior point uses its two neighbors; ends reuse the nearest triple.
            d[0] = QuadraticDerivative(x[0], x[0], x[1], x[2], y[0], y[1], y[2]);
            for (int i = 1; i < n - 1; i++)
                d[i] = QuadraticDerivative(x[i], x[i - 1], x[i], x[i + 1], y[i - 1], y[i], y[i + 1]);
            d[n - 1] = QuadraticDerivative(x[n - 1], x[n - 3], x[n - 2], x[n - 1], y[n - 3], y[n - 2], y[n - 1]);
            return d;
        }

        /// <summary>Central-difference derivative of <paramref name="f"/> at <paramref name="x"/>.</summary>
        public static double Derivative(Func<double, double> f, double x, double h = 1e-5)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h), h, "Step must be positive.");
            return (f(x + h) - f(x - h)) / (2 * h);
        }

        /// <summary>Richardson-extrapolated derivative of <paramref name="f"/> for higher accuracy.</summary>
        public static double RichardsonDerivative(Func<double, double> f, double x, double h = 1e-2)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h), h, "Step must be positive.");
            double d1 = (f(x + h) - f(x - h)) / (2 * h);
            double d2 = (f(x + h / 2) - f(x - h / 2)) / h;
            return (4 * d2 - d1) / 3.0; // cancels the leading O(h^2) error term
        }

        // Derivative at `at` of the quadratic through (x0,f0),(x1,f1),(x2,f2) — the Lagrange interpolant.
        private static double QuadraticDerivative(double at, double x0, double x1, double x2,
            double f0, double f1, double f2)
        {
            double t0 = f0 * ((at - x1) + (at - x2)) / ((x0 - x1) * (x0 - x2));
            double t1 = f1 * ((at - x0) + (at - x2)) / ((x1 - x0) * (x1 - x2));
            double t2 = f2 * ((at - x0) + (at - x1)) / ((x2 - x0) * (x2 - x1));
            return t0 + t1 + t2;
        }
    }
}
