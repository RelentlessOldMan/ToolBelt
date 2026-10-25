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
            if (!(dx > 0) || double.IsInfinity(dx)) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive and finite.");

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

        /// <summary>Derivative at each sample for strictly ascending x positions.</summary>
        public static double[] Sample(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            if (x.Count < 3) throw new ArgumentException("At least three samples are required for uneven spacing.", nameof(x));

            int n = x.Count;
            for (int i = 1; i < n; i++)
                if (!(x[i] > x[i - 1])) // a repeated x divided by zero and returned ±∞
                    throw new ArgumentException($"x values must be strictly ascending (x[{i}] = {x[i]}, x[{i - 1}] = {x[i - 1]}).", nameof(x));
            var d = new double[n];
            // Each interior point uses its two neighbors; ends reuse the nearest triple.
            d[0] = QuadraticDerivative(x[0], x[0], x[1], x[2], y[0], y[1], y[2]);
            for (int i = 1; i < n - 1; i++)
                d[i] = QuadraticDerivative(x[i], x[i - 1], x[i], x[i + 1], y[i - 1], y[i], y[i + 1]);
            d[n - 1] = QuadraticDerivative(x[n - 1], x[n - 3], x[n - 2], x[n - 1], y[n - 3], y[n - 2], y[n - 1]);
            return d;
        }

        /// <summary>
        /// Central-difference derivative of <paramref name="f"/> at <paramref name="x"/>. The default step is
        /// 1e-5·max(1, |x|), scaled so x ± h differs from x at any magnitude (a fixed 1e-5 vanished at x = 1e12);
        /// pass <paramref name="h"/> for an explicit absolute step. A step below the spacing of doubles at x throws
        /// <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        public static double Derivative(Func<double, double> f, double x, double? h = null)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            double step = StepFor(x, h, 1e-5);
            double xp = x + step, xm = x - step;
            if (xp == xm) throw StepTooSmall(x, step); // 0/0 = NaN
            return (f(xp) - f(xm)) / (xp - xm); // the step actually taken, after rounding x ± h
        }

        /// <summary>Richardson-extrapolated derivative of <paramref name="f"/> for higher accuracy. The default step
        /// is 1e-2·max(1, |x|); pass <paramref name="h"/> for an explicit absolute step.</summary>
        public static double RichardsonDerivative(Func<double, double> f, double x, double? h = null)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            double hs = StepFor(x, h, 1e-2);
            return Richardson(f, x, hs);
        }

        private static double StepFor(double x, double? h, double relative)
        {
            if (h is double given)
            {
                if (!(given > 0) || double.IsInfinity(given)) throw new ArgumentOutOfRangeException(nameof(h), given, "Step must be positive and finite.");
                return given;
            }
            return relative * Math.Max(1, Math.Abs(x));
        }

        private static double Richardson(Func<double, double> f, double x, double h)
        {
            // Differences over the steps actually taken: at x = 1e6 the nominal 2h = 2e-7 is not representable as a
            // spacing, and dividing by it reported 1.0000000000000002 for f(x) = x (and 0 at x = 1e8).
            double xp1 = x + h, xm1 = x - h, xp2 = x + h / 2, xm2 = x - h / 2;
            double w1 = xp1 - xm1, w2 = xp2 - xm2;
            if (w2 == 0 || w1 == w2) throw StepTooSmall(x, h);
            double d1 = (f(xp1) - f(xm1)) / w1;
            double d2 = (f(xp2) - f(xm2)) / w2;
            double ratio = w1 / w2;
            if (ratio == 2) return (4 * d2 - d1) / 3.0; // cancels the leading O(h^2) error term
            return d2 + (d2 - d1) / (ratio * ratio - 1);  // the same extrapolation for steps that rounded unevenly
        }

        private static ArgumentOutOfRangeException StepTooSmall(double x, double h)
            => new ArgumentOutOfRangeException(nameof(h), h, $"Step is below the spacing of doubles at x = {x:R}; x ± h rounds back to x.");

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
