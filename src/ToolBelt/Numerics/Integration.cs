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
            if (dx <= 0) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive.");
            double sum = 0;
            for (int i = 1; i < y.Count; i++)
                sum += (y[i - 1] + y[i]) * 0.5 * dx;
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
                sum += (y[i - 1] + y[i]) * 0.5 * h;
            }
            return sum;
        }

        /// <summary>
        /// Composite Simpson's rule over uniformly spaced samples. If the number of intervals is odd, the
        /// final interval is handled with the trapezoid rule.
        /// </summary>
        public static double Simpson(IReadOnlyList<double> y, double dx)
        {
            RequireSamples(y);
            if (dx <= 0) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive.");

            int intervals = y.Count - 1;
            int even = intervals - (intervals % 2); // largest even interval count
            double sum = 0;
            for (int i = 0; i < even; i += 2)
                sum += (y[i] + 4 * y[i + 1] + y[i + 2]) * (dx / 3.0);
            if (even != intervals) // trailing odd interval
                sum += (y[intervals - 1] + y[intervals]) * 0.5 * dx;
            return sum;
        }

        /// <summary>Adaptive Simpson integration of <paramref name="f"/> over [a, b] to the given tolerance.</summary>
        public static double AdaptiveSimpson(Func<double, double> f, double a, double b, double tolerance = 1e-10)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            if (tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be positive.");
            if (a == b) return 0;

            double fa = f(a), fb = f(b), m = (a + b) / 2, fm = f(m);
            double whole = SimpsonPiece(a, b, fa, fb, fm);
            return Recurse(f, a, b, fa, fb, fm, whole, tolerance, 50);
        }

        /// <summary>Running integral of uniformly spaced samples (length matches input; first element is 0).</summary>
        public static double[] CumulativeTrapezoid(IReadOnlyList<double> y, double dx)
        {
            RequireSamples(y);
            if (dx <= 0) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Spacing must be positive.");
            var result = new double[y.Count];
            for (int i = 1; i < y.Count; i++)
                result[i] = result[i - 1] + (y[i - 1] + y[i]) * 0.5 * dx;
            return result;
        }

        private static double SimpsonPiece(double a, double b, double fa, double fb, double fm)
            => (b - a) / 6.0 * (fa + 4 * fm + fb);

        private static double Recurse(Func<double, double> f, double a, double b,
            double fa, double fb, double fm, double whole, double tol, int depth)
        {
            double lm = (a + b) / 2;
            double left = (a + lm) / 2, right = (lm + b) / 2;
            double fleft = f(left), fright = f(right);
            double leftArea = SimpsonPiece(a, lm, fa, fm, fleft);
            double rightArea = SimpsonPiece(lm, b, fm, fb, fright);
            double combined = leftArea + rightArea;

            if (depth <= 0 || Math.Abs(combined - whole) <= 15 * tol)
                return combined + (combined - whole) / 15.0; // Richardson correction

            return Recurse(f, a, lm, fa, fm, fleft, leftArea, tol / 2, depth - 1)
                 + Recurse(f, lm, b, fm, fb, fright, rightArea, tol / 2, depth - 1);
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
