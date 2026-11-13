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
                if (!(h >= 0)) throw new ArgumentException("x values must be ascending.", nameof(x)); // NaN too
                // Finite ends 2e308 apart overflowed the width to ∞ (a panel of height 1e-10 integrated to ∞): halve it.
                if (double.IsInfinity(h) && IsFinite(x[i]) && IsFinite(x[i - 1]))
                    sum += 2 * Panel(y[i - 1], y[i], 0.5 * x[i] - 0.5 * x[i - 1]);
                else
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
            // A subnormal dx / 3 rounds (ε / 3 is 0, so [1, 1, 1] over ε integrated to 0): there the samples are scaled
            // first and divided after. Elsewhere dx / 3 is kept, so ordinary results are unchanged.
            bool tiny = dx < 1e-290;
            for (int i = 0; i < even; i += 2)
            {
                double s = y[i] + 4 * y[i + 1] + y[i + 2];
                // Samples near double.MaxValue overflow the weighted sum: use eighths of it instead.
                if (double.IsInfinity(s) && IsFinite(y[i]) && IsFinite(y[i + 1]) && IsFinite(y[i + 2]))
                {
                    double eighths = 0.125 * y[i] + 0.5 * y[i + 1] + 0.125 * y[i + 2];
                    sum += tiny ? eighths * dx / 3.0 * 8 : eighths * (dx / 3.0) * 8;
                }
                else
                    sum += tiny ? s * dx / 3.0 : s * (dx / 3.0);
            }
            if (even != intervals) // trailing odd interval
            {
                int n = intervals;
                if (n >= 2)
                {
                    double t = 5.0 / 12 * y[n] + 8.0 / 12 * y[n - 1] - 1.0 / 12 * y[n - 2];
                    // As above: near double.MaxValue the combination overflows, so use halves of it.
                    if (double.IsInfinity(t) && IsFinite(y[n]) && IsFinite(y[n - 1]) && IsFinite(y[n - 2]))
                        sum += dx * (5.0 / 24 * y[n] + 8.0 / 24 * y[n - 1] - 1.0 / 24 * y[n - 2]) * 2;
                    else
                        sum += dx * t;
                }
                else
                {
                    sum += Panel(y[n - 1], y[n], dx);
                }
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
            if (double.IsInfinity(s) && IsFinite(a) && IsFinite(b)) return (0.5 * a + 0.5 * b) * h;
            // Scaled before halving: a subnormal sum halved to 0 first ([ε, 0] over 2^100 integrated to 0, not 2^-975).
            double sh = s * h;
            return IsFinite(sh) ? sh * 0.5 : s * 0.5 * h;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // Samples near double.MaxValue overflow the weighted sum (and a span over the whole range overflows b − a) though
        // the piece itself is in range: only then is it formed from quartered samples, exactly scaled back by 4.
        private static double SimpsonPiece(double a, double b, double fa, double fb, double fm)
        {
            double span = b - a;
            if (Math.Abs(span) < 1e-290)
            {
                // A subnormal span / 6 rounds (1 over [0, 3ε] integrated to 0): scale the samples first, divide after.
                double s = fa + 4 * fm + fb;
                return IsFinite(s) ? span * s / 6.0 : span * (0.25 * fa + fm + 0.25 * fb) / 6.0 * 4;
            }
            double h6 = (b - a) / 6.0, piece = h6 * (fa + 4 * fm + fb);
            if (IsFinite(piece)) return piece;
            if (!IsFinite(h6)) h6 = (0.5 * b - 0.5 * a) / 3.0;
            return h6 * (0.25 * fa + fm + 0.25 * fb) * 4;
        }

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
            // A piece beyond the double range is the integral overflowing: splitting cannot bring it back (∞ − ∞ was NaN,
            // so it split until the budget ran out).
            if (double.IsInfinity(combined)) return combined;
            double diff = Math.Abs(combined - whole);
            if (depth <= 0 || diff <= 15 * tol || diff <= 1e-14 * Math.Abs(combined))
            {
                double corrected = combined + (combined - whole) / 15.0; // Richardson correction
                return IsFinite(corrected) ? corrected : combined + (combined / 15.0 - whole / 15.0);
            }

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
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.", nameof(y));
            if (x.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(y));
        }
    }
}
