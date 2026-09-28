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
    /// changes sign; Brent is the robust default. Newton and secant iterate from a starting guess. Every
    /// method reports non-convergence explicitly rather than silently returning a meaningless value.
    /// </summary>
    public static class RootFinding
    {
        /// <summary>Bisection on a sign-changing bracket [a, b]. Always converges if bracketed.</summary>
        public static RootResult Bisection(Func<double, double> f, double a, double b, double tolerance = 1e-12, int maxIterations = 200)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            double fa = f(a), fb = f(b);
            if (fa == 0) return Success(a, 0, 0);
            if (fb == 0) return Success(b, 0, 0);
            if (Math.Sign(fa) == Math.Sign(fb))
                return Failure(double.NaN, 0, "f(a) and f(b) must have opposite signs to bracket a root.");

            for (int i = 1; i <= maxIterations; i++)
            {
                double m = (a + b) / 2;
                double fm = f(m);
                if (fm == 0 || (b - a) / 2 < tolerance)
                    return Success(m, i, fm);
                if (Math.Sign(fm) == Math.Sign(fa)) { a = m; fa = fm; }
                else { b = m; fb = fm; }
            }
            return Failure((a + b) / 2, maxIterations, "Maximum iterations reached without meeting the tolerance.");
        }

        /// <summary>Brent's method on a sign-changing bracket [a, b] — combines bisection, secant and inverse quadratic.</summary>
        public static RootResult Brent(Func<double, double> f, double a, double b, double tolerance = 1e-12, int maxIterations = 200)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            double fa = f(a), fb = f(b);
            if (fa == 0) return Success(a, 0, 0);
            if (fb == 0) return Success(b, 0, 0);
            if (Math.Sign(fa) == Math.Sign(fb))
                return Failure(double.NaN, 0, "f(a) and f(b) must have opposite signs to bracket a root.");

            if (Math.Abs(fa) < Math.Abs(fb)) { (a, b) = (b, a); (fa, fb) = (fb, fa); }

            double c = a, fc = fa, d = c;
            bool mflag = true;

            for (int i = 1; i <= maxIterations; i++)
            {
                if (fb == 0 || Math.Abs(b - a) < tolerance)
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

                double lo = (3 * a + b) / 4;
                bool useBisection =
                    !Between(s, lo, b) ||
                    (mflag && Math.Abs(s - b) >= Math.Abs(b - c) / 2) ||
                    (!mflag && Math.Abs(s - b) >= Math.Abs(c - d) / 2) ||
                    (mflag && Math.Abs(b - c) < tolerance) ||
                    (!mflag && Math.Abs(c - d) < tolerance);

                if (useBisection) { s = (a + b) / 2; mflag = true; }
                else mflag = false;

                double fs = f(s);
                d = c;
                c = b; fc = fb;
                if (Math.Sign(fa) != Math.Sign(fs)) { b = s; fb = fs; }
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
            double x = x0;
            for (int i = 1; i <= maxIterations; i++)
            {
                double fx = f(x);
                if (Math.Abs(fx) < tolerance)
                    return Success(x, i, fx);
                double dfx = derivative != null ? derivative(x) : (f(x + 1e-7) - f(x - 1e-7)) / 2e-7;
                if (dfx == 0 || double.IsNaN(dfx))
                    return Failure(x, i, "Derivative vanished; Newton's method cannot proceed.");
                double next = x - fx / dfx;
                if (Math.Abs(next - x) < tolerance)
                    return Success(next, i, f(next));
                x = next;
            }
            return Failure(x, maxIterations, "Maximum iterations reached without convergence.");
        }

        /// <summary>Secant method from two starting guesses.</summary>
        public static RootResult Secant(Func<double, double> f, double x0, double x1,
            double tolerance = 1e-12, int maxIterations = 100)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            double f0 = f(x0), f1 = f(x1);
            for (int i = 1; i <= maxIterations; i++)
            {
                if (Math.Abs(f1) < tolerance)
                    return Success(x1, i, f1);
                if (f1 == f0)
                    return Failure(x1, i, "Secant step divided by zero (equal function values).");
                double next = x1 - f1 * (x1 - x0) / (f1 - f0);
                if (Math.Abs(next - x1) < tolerance)
                    return Success(next, i, f(next));
                x0 = x1; f0 = f1;
                x1 = next; f1 = f(x1);
            }
            return Failure(x1, maxIterations, "Maximum iterations reached without convergence.");
        }

        private static bool Between(double s, double a, double b)
            => (s >= Math.Min(a, b)) && (s <= Math.Max(a, b));

        private static RootResult Success(double root, int iterations, double residual)
            => new RootResult(true, root, iterations, residual, "Converged.");

        private static RootResult Failure(double root, int iterations, string message)
            => new RootResult(false, root, iterations, double.NaN, message);
    }
}
