// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>The outcome of a root search: whether it converged, the estimate, and diagnostics.</summary>
    public sealed class RootResult
    {
        internal RootResult(bool converged, double root, int iterations, double residual, string message)
        {
            Converged = converged;
            Root = root;
            Iterations = iterations;
            Residual = residual;
            Message = message;
        }

        public bool Converged { get; }
        public double Root { get; }
        public int Iterations { get; }
        public double Residual { get; }
        public string Message { get; }
    }

    /// <summary>
    /// One-dimensional root finding. Bisection and Brent's method require a bracket where the function
    /// changes sign (either order); Brent is the robust default. Newton and secant iterate from a starting guess.
    /// The tolerance on x is absolute plus a few ulps of the root, so a root of any magnitude can converge. Every
    /// method reports non-convergence explicitly (including f returning NaN) rather than silently returning a
    /// meaningless value.
    /// </summary>
    public static class RootFinding
    {
        /// <summary>Bisection on a sign-changing bracket [a, b]. Converges if bracketed and f is continuous.</summary>
        public static RootResult Bisection(Func<double, double> f, double a, double b, double tolerance = 1e-12, int maxIterations = 200)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            RequireTolerance(tolerance);
            if (a > b) (a, b) = (b, a); // a reversed bracket made (b − a)/2 negative, so it "converged" at once
            double fa = f(a), fb = f(b);
            if (double.IsNaN(fa) || double.IsNaN(fb)) return Failure(double.NaN, 0, NaNMessage);
            if (fa == 0) return Success(a, 0, 0);
            if (fb == 0) return Success(b, 0, 0);
            if ((fa < 0) == (fb < 0))
                return Failure(double.NaN, 0, "f(a) and f(b) must have opposite signs to bracket a root.");

            for (int i = 1; i <= maxIterations; i++)
            {
                double m = 0.5 * a + 0.5 * b;
                double fm = f(m);
                if (double.IsNaN(fm)) return Failure(m, i, NaNMessage);
                // m == a or b: the bracket is two adjacent doubles, as tight as it can get.
                if (fm == 0 || 0.5 * (b - a) < Tol(tolerance, m) || m == a || m == b)
                    return Success(m, i, fm);
                if ((fm < 0) == (fa < 0)) { a = m; fa = fm; }
                else { b = m; fb = fm; }
            }
            return Failure((a + b) / 2, maxIterations, "Maximum iterations reached without meeting the tolerance.");
        }

        /// <summary>Brent's method on a sign-changing bracket [a, b] — combines bisection, secant and inverse quadratic.</summary>
        public static RootResult Brent(Func<double, double> f, double a, double b, double tolerance = 1e-12, int maxIterations = 200)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            RequireTolerance(tolerance);
            double fa = f(a), fb = f(b);
            if (double.IsNaN(fa) || double.IsNaN(fb)) return Failure(double.NaN, 0, NaNMessage);
            if (fa == 0) return Success(a, 0, 0);
            if (fb == 0) return Success(b, 0, 0);
            if ((fa < 0) == (fb < 0))
                return Failure(double.NaN, 0, "f(a) and f(b) must have opposite signs to bracket a root.");

            if (Math.Abs(fa) < Math.Abs(fb)) { (a, b) = (b, a); (fa, fb) = (fb, fa); }

            double c = a, fc = fa, d = c;
            bool mflag = true;

            for (int i = 1; i <= maxIterations; i++)
            {
                double tol = Tol(tolerance, b);
                if (fb == 0 || Math.Abs(b - a) < tol)
                    return Success(b, i, fb);

                double s;
                if (fa != fc && fb != fc)
                {
                    // Inverse quadratic interpolation.
                    s = a * fb * fc / ((fa - fb) * (fa - fc))
                      + b * fa * fc / ((fb - fa) * (fb - fc))
                      + c * fa * fb / ((fc - fa) * (fc - fb));
                }
                else
                {
                    s = b - fb * (b - a) / (fb - fa); // secant
                }

                double lo = 0.75 * a + 0.25 * b;
                bool useBisection =
                    !Between(s, lo, b) ||
                    (mflag && Math.Abs(s - b) >= Math.Abs(b - c) / 2) ||
                    (!mflag && Math.Abs(s - b) >= Math.Abs(c - d) / 2) ||
                    (mflag && Math.Abs(b - c) < tol) ||
                    (!mflag && Math.Abs(c - d) < tol);

                if (useBisection) { s = 0.5 * a + 0.5 * b; mflag = true; }
                else mflag = false;

                double fs = f(s);
                if (double.IsNaN(fs)) return Failure(s, i, NaNMessage);
                d = c;
                c = b; fc = fb;
                if ((fa < 0) != (fs < 0)) { b = s; fb = fs; }
                else { a = s; fa = fs; }
                if (Math.Abs(fa) < Math.Abs(fb)) { (a, b) = (b, a); (fa, fb) = (fb, fa); }
            }
            return Failure(b, maxIterations, "Maximum iterations reached without meeting the tolerance.");
        }

        /// <summary>Newton-Raphson from <paramref name="x0"/>; uses a numerical derivative if none is supplied.</summary>
        public static RootResult Newton(Func<double, double> f, double x0, Func<double, double>? derivative = null,
            double tolerance = 1e-12, int maxIterations = 100)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            RequireTolerance(tolerance);
            double x = x0;
            for (int i = 1; i <= maxIterations; i++)
            {
                double fx = f(x);
                if (double.IsNaN(fx)) return Failure(x, i, NaNMessage);
                if (Math.Abs(fx) < tolerance)
                    return Success(x, i, fx);
                double dfx = derivative != null ? derivative(x) : NumericDerivative(f, x);
                if (dfx == 0 || double.IsNaN(dfx))
                    return Failure(x, i, "Derivative vanished; Newton's method cannot proceed.");
                double next = x - fx / dfx;
                double t = Tol(tolerance, next);
                if (Math.Abs(next - x) < t)
                {
                    // A tiny step only means |f / f'| is small, which a wrong or huge derivative also gives (x − 1 with
                    // f' = 1e300 "converged" at 5). Accept it when a root is really there: a small residual, or f
                    // changing sign within the tolerance either side.
                    double fn = f(next);
                    if (Math.Abs(fn) < tolerance || fn == 0 || ChangesSign(f, next, t))
                        return Success(next, i, fn);
                    return Failure(next, i, "Newton's step vanished but f has no root near it; check the derivative.");
                }
                x = next;
            }
            return Failure(x, maxIterations, "Maximum iterations reached without convergence.");
        }

        private static bool ChangesSign(Func<double, double> f, double x, double t)
        {
            double lo = f(x - t), hi = f(x + t);
            return (lo <= 0 && hi >= 0) || (lo >= 0 && hi <= 0);
        }

        /// <summary>Secant method from two starting guesses.</summary>
        public static RootResult Secant(Func<double, double> f, double x0, double x1,
            double tolerance = 1e-12, int maxIterations = 100)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            RequireTolerance(tolerance);
            double f0 = f(x0), f1 = f(x1);
            for (int i = 1; i <= maxIterations; i++)
            {
                if (double.IsNaN(f0) || double.IsNaN(f1)) return Failure(x1, i, NaNMessage);
                if (Math.Abs(f1) < tolerance)
                    return Success(x1, i, f1);
                if (f1 == f0)
                    return Failure(x1, i, "Secant step divided by zero (equal function values).");
                double next = x1 - f1 * (x1 - x0) / (f1 - f0);
                if (Math.Abs(next - x1) < Tol(tolerance, next))
                    return Success(next, i, f(next));
                x0 = x1; f0 = f1;
                x1 = next; f1 = f(x1);
            }
            return Failure(x1, maxIterations, "Maximum iterations reached without convergence.");
        }

        private const string NaNMessage = "f returned NaN; the function is not defined there.";

        // Absolute tolerance plus four ulps of the estimate (Brent's zeroin): an absolute 1e-12 alone is
        // unreachable for a root near 1e10, whose spacing between doubles is 2e-6.
        // NaN made every convergence test false (Bisection) or true (Brent "converged" at once).
        private static void RequireTolerance(double tolerance)
        {
            if (!(tolerance >= 0) || double.IsInfinity(tolerance))
                throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be non-negative and finite.");
        }

        private static double Tol(double tolerance, double x) => tolerance + 4 * 2.220446049250313e-16 * Math.Abs(x);

        // Central difference with a step relative to |x|, so x ± h differs from x at any magnitude.
        private static double NumericDerivative(Func<double, double> f, double x)
        {
            double h = 1e-7 * Math.Max(1, Math.Abs(x));
            double xp = x + h, xm = x - h;
            return (f(xp) - f(xm)) / (xp - xm);
        }

        private static bool Between(double s, double a, double b)
            => (s >= Math.Min(a, b)) && (s <= Math.Max(a, b));

        private static RootResult Success(double root, int iterations, double residual)
            => new RootResult(true, root, iterations, residual, "Converged.");

        private static RootResult Failure(double root, int iterations, string message)
            => new RootResult(false, root, iterations, double.NaN, message);
    }
}
