// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Numerical integration: trapezoid and Simpson rules over sampled data (uniform or uneven spacing),
    /// adaptive Simpson over a function, and a cumulative trapezoid for a running integral. Grade against
    /// closed-form integrals of polynomials and sinusoids.
    /// </summary>
    public static class Integration
    {
        /// <summary>Trapezoidal integral of uniformly spaced samples.</summary>
        public static double Trapezoid(IReadOnlyList<double> y, double dx)
        {
            RequireSamples(y);
            if (!(dx > 0) || double.IsInfinity(dx)) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive and finite.");
            double sum = 0;
            for (int i = 1; i < y.Count; i++)
                sum += Panel(y[i - 1], y[i], dx);
            return sum;
        }

        /// <summary>Trapezoidal integral of samples at arbitrary ascending x positions.</summary>
        public static double Trapezoid(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            RequirePaired(x, y);
            double sum = 0;
            for (int i = 1; i < y.Count; i++)
            {
                double h = x[i] - x[i - 1];
                if (h < 0) throw new ArgumentException("x values must be ascending.", nameof(x));
                sum += Panel(y[i - 1], y[i], h);
            }
            return sum;
        }

        /// <summary>
        /// Composite Simpson's rule over uniformly spaced samples. If the number of intervals is odd, the
        /// final interval uses the third-order end correction SciPy's <c>simpson</c> uses (exact for quadratics),
        /// so accuracy stays O(h⁴); two samples fall back to the trapezoid rule.
        /// </summary>
        public static double Simpson(IReadOnlyList<double> y, double dx)
        {
            RequireSamples(y);
            if (!(dx > 0) || double.IsInfinity(dx)) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive and finite.");

            int intervals = y.Count - 1;
            int even = intervals - (intervals % 2); // largest even interval count
            double sum = 0;
            for (int i = 0; i < even; i += 2)
            {
                double s = y[i] + 4 * y[i + 1] + y[i + 2];
                // Samples near double.MaxValue overflow the weighted sum: use eighths of it instead.
                if (double.IsInfinity(s) && IsFinite(y[i]) && IsFinite(y[i + 1]) && IsFinite(y[i + 2]))
                    sum += (0.125 * y[i] + 0.5 * y[i + 1] + 0.125 * y[i + 2]) * (dx / 3.0) * 8;
                else
                    sum += s * (dx / 3.0);
            }
            if (even != intervals) // trailing odd interval
            {
                int n = intervals;
                sum += n >= 2
                    ? dx * (5.0 / 12 * y[n] + 8.0 / 12 * y[n - 1] - 1.0 / 12 * y[n - 2])
                    : Panel(y[n - 1], y[n], dx);
            }
            return sum;
        }

        /// <summary>
        /// Adaptive Simpson integration of <paramref name="f"/> over [a, b] to the given absolute tolerance (or to
        /// round-off, relative to the integral, when that is coarser). Throws <see cref="InvalidOperationException"/>
        /// if f returns NaN or infinity (e.g. an endpoint singularity) or the tolerance is not met within
        /// <paramref name="maxEvaluations"/> function evaluations.
        /// </summary>
        public static double AdaptiveSimpson(Func<double, double> f, double a, double b, double tolerance = 1e-10,
            int maxEvaluations = 10_000_000)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            if (!(tolerance > 0)) throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be positive.");
            if (double.IsNaN(a) || double.IsInfinity(a)) throw new ArgumentOutOfRangeException(nameof(a), a, "Bound must be finite.");
            if (double.IsNaN(b) || double.IsInfinity(b)) throw new ArgumentOutOfRangeException(nameof(b), b, "Bound must be finite.");
            if (maxEvaluations < 3) throw new ArgumentOutOfRangeException(nameof(maxEvaluations), maxEvaluations, "Need at least 3 evaluations.");
            if (a == b) return 0;

            int budget = maxEvaluations - 3;
            double m = 0.5 * a + 0.5 * b;
            double fa = Eval(f, a), fb = Eval(f, b), fm = Eval(f, m);
            double whole = SimpsonPiece(a, b, fa, fb, fm);
            return Recurse(f, a, b, fa, fb, fm, whole, tolerance, 50, ref budget);
        }

        /// <summary>Running integral of uniformly spaced samples (length matches input; first element is 0).</summary>
        public static double[] CumulativeTrapezoid(IReadOnlyList<double> y, double dx)
        {
            RequireSamples(y);
            if (!(dx > 0) || double.IsInfinity(dx)) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive and finite.");
            var result = new double[y.Count];
            for (int i = 1; i < y.Count; i++)
                result[i] = result[i - 1] + Panel(y[i - 1], y[i], dx);
            return result;
        }

        // One trapezoid panel. Two samples near double.MaxValue overflow their sum: halve them first in that case only,
        // so ordinary results are unchanged.
        private static double Panel(double a, double b, double h)
        {
            double s = a + b;
            return double.IsInfinity(s) && IsFinite(a) && IsFinite(b) ? (0.5 * a + 0.5 * b) * h : s * 0.5 * h;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static double SimpsonPiece(double a, double b, double fa, double fb, double fm)
            => (b - a) / 6.0 * (fa + 4 * fm + fb);

        private static double Recurse(Func<double, double> f, double a, double b,
            double fa, double fb, double fm, double whole, double tol, int depth, ref int budget)
        {
            if ((budget -= 2) < 0)
                throw new InvalidOperationException("Adaptive Simpson did not reach the tolerance within the evaluation budget.");
            double lm = 0.5 * a + 0.5 * b;
            double left = 0.5 * a + 0.5 * lm, right = 0.5 * lm + 0.5 * b;
            double fleft = Eval(f, left), fright = Eval(f, right);
            double leftArea = SimpsonPiece(a, lm, fa, fm, fleft);
            double rightArea = SimpsonPiece(lm, b, fm, fb, fright);
            double combined = leftArea + rightArea;

            // The absolute tolerance halves per split; once the difference is at round-off level relative to
            // the piece, splitting further cannot help (a large integrand took millions of evaluations).
            double diff = Math.Abs(combined - whole);
            if (depth <= 0 || diff <= 15 * tol || diff <= 1e-14 * Math.Abs(combined))
                return combined + (combined - whole) / 15.0; // Richardson correction

            return Recurse(f, a, lm, fa, fm, fleft, leftArea, tol / 2, depth - 1, ref budget)
                 + Recurse(f, lm, b, fm, fb, fright, rightArea, tol / 2, depth - 1, ref budget);
        }

        private static double Eval(Func<double, double> f, double x)
        {
            double v = f(x);
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new InvalidOperationException($"f({x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}) is not finite; adaptive Simpson cannot integrate it.");
            return v;
        }

        private static void RequireSamples(IReadOnlyList<double> y)
        {
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (y.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(y));
        }

        private static void RequirePaired(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            if (x.Count < 2) throw new ArgumentException("At least two samples are required.");
        }
    }
}
