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
                double s = (y[1] - y[0]) / dx;
                d[0] = d[1] = double.IsInfinity(s) ? 2 * ((0.5 * y[1] - 0.5 * y[0]) / dx) : s;
                return d;
            }
            // 2·dx overflowed for dx past MaxValue/2 and gave a quiet 0 the redo below never saw: halve after dividing there.
            double w = 2 * dx;
            bool wide = double.IsInfinity(w);
            // The one-sided formulas in difference form, −3y0 + 4y1 − y2 = 4(y1 − y0) − (y2 − y0), so constant data gives
            // exactly 0 (a flat 1e8 + 0.1 gave ±1.5e-7 at the ends).
            d[0] = Half(4 * (y[1] - y[0]) - (y[2] - y[0]), dx, w, wide);                         // 2nd-order forward
            d[n - 1] = Half(4 * (y[n - 1] - y[n - 2]) - (y[n - 1] - y[n - 3]), dx, w, wide);     // 2nd-order backward
            for (int i = 1; i < n - 1; i++)
                d[i] = Half(y[i + 1] - y[i - 1], dx, w, wide);                      // 2nd-order central
            // Samples near ±double.MaxValue overflowed the differences (NaN or ∞): redo those from eighths.
            for (int i = 0; i < n; i++)
                if (double.IsNaN(d[i]) || double.IsInfinity(d[i]))
                    d[i] = 4 * ((i == 0 ? -0.375 * y[0] + 0.5 * y[1] - 0.125 * y[2]
                        : i == n - 1 ? 0.375 * y[n - 1] - 0.5 * y[n - 2] + 0.125 * y[n - 3]
                        : 0.125 * y[i + 1] - 0.125 * y[i - 1]) / dx);
            return d;
        }

        /// <summary>Derivative at each sample for strictly ascending x positions.</summary>
        public static double[] Sample(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.", nameof(y));
            if (x.Count < 3) throw new ArgumentException("At least three samples are required for uneven spacing.", nameof(x));

            int n = x.Count;
            for (int i = 0; i < n; i++) // an infinite position made every nearby derivative 0 or NaN
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i]))
                    throw new ArgumentException($"x[{i}] is {x[i]}; positions must be finite.", nameof(x));
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
            FiniteX(x);
            double step = StepFor(x, h, 1e-5);
            double xp = x + step, xm = x - step;
            // Near ±double.MaxValue x ± h overflowed and the result was NaN: take the one-sided difference toward zero.
            if (double.IsInfinity(xp)) xp = x;
            else if (double.IsInfinity(xm)) xm = x;
            if (double.IsInfinity(xp - xm)) throw StepOverflows(x, step);
            if (xp == xm) throw StepTooSmall(x, step); // 0/0 = NaN
            return (f(xp) - f(xm)) / (xp - xm); // the step actually taken, after rounding x ± h
        }

        /// <summary>Richardson-extrapolated derivative of <paramref name="f"/> for higher accuracy. The default step
        /// is 1e-2·max(1, |x|); pass <paramref name="h"/> for an explicit absolute step.</summary>
        public static double RichardsonDerivative(Func<double, double> f, double x, double? h = null)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            FiniteX(x);
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
            // Near ±double.MaxValue x ± h overflowed, and the message blamed a step below the spacing of doubles.
            if (double.IsInfinity(x + h) || double.IsInfinity(x - h)) return OneSidedRichardson(f, x, h);
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

        // Forward differences toward zero, D(h) = f' + c·h + …, extrapolated as 2·D(h/2) − D(h).
        private static double OneSidedRichardson(Func<double, double> f, double x, double h)
        {
            double s = x > 0 ? -1 : 1, fx = f(x);
            double x1 = x + s * h, x2 = x + s * (h / 2);
            if (double.IsInfinity(x1)) throw StepOverflows(x, h);
            double w1 = x1 - x, w2 = x2 - x;
            if (w2 == 0 || w1 == w2) throw StepTooSmall(x, h);
            double d1 = (f(x1) - fx) / w1, d2 = (f(x2) - fx) / w2;
            double ratio = w1 / w2;
            return ratio == 2 ? 2 * d2 - d1 : d2 + (d2 - d1) / (ratio - 1);
        }

        private static double Half(double difference, double dx, double w, bool wide)
            => wide ? difference / dx * 0.5 : difference / w;

        // A NaN or infinite x came back NaN, or (with a step given) threw blaming h for being below the spacing at ∞.
        private static void FiniteX(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
                throw new ArgumentOutOfRangeException(nameof(x), x, "x must be finite.");
        }

        private static ArgumentOutOfRangeException StepOverflows(double x, double h)
            => new ArgumentOutOfRangeException(nameof(h), h, $"Step is too large at x = {x:R}: x ± h overflows a double.");

        private static ArgumentOutOfRangeException StepTooSmall(double x, double h)
            => new ArgumentOutOfRangeException(nameof(h), h, $"Step is below the spacing of doubles at x = {x:R}; x ± h rounds back to x.");

        // Derivative at `at` of the quadratic through (x0,f0),(x1,f1),(x2,f2) — the Lagrange interpolant.
        private static double QuadraticDerivative(double at, double x0, double x1, double x2,
            double f0, double f1, double f2)
        {
            double d = Quadratic(at, x0, x1, x2, f0, f1, f2);
            if (!double.IsNaN(d) && !double.IsInfinity(d)) return d;
            return Finite(f0) && Finite(f1) && Finite(f2) ? WideNewton(at, x0, x1, x2, f0, f1, f2) : d;
        }

        private static double Quadratic(double at, double x0, double x1, double x2,
            double f0, double f1, double f2)
        {
            // Newton's divided differences first: f[x0,x1] + f[x0,x1,x2]·((at − x0) + (at − x1)) is exactly 0 for constant
            // data, where the Lagrange weights left round-off (a flat 0.1 gave −4e-16). The forms below handle overflow.
            // Points spread past double.MaxValue ({−1e308, 0, 1e308}) overflowed every spacing and gave NaN at the ends: the
            // derivative in x is half the one in x/2, and halving such positions is exact.
            double spread = Math.Max(Math.Max(x0, x1), x2) - Math.Min(Math.Min(x0, x1), x2);
            if (double.IsInfinity(spread))
                return 0.5 * Quadratic(0.5 * at, 0.5 * x0, 0.5 * x1, 0.5 * x2, f0, f1, f2);
            {
                double s01 = (f1 - f0) / (x1 - x0), s12 = (f2 - f1) / (x2 - x1);
                // The positions as fractions of x2 − x0 first: divided last, the curvature term underflowed for wide grids.
                double span = x2 - x0;
                double newton = s01 + (s12 - s01) * ((at - x0) / span + (at - x1) / span);
                if (!double.IsNaN(newton) && !double.IsInfinity(newton)) return newton;
            }
            double q0 = (x0 - x1) * (x0 - x2), q1 = (x1 - x0) * (x1 - x2), q2 = (x2 - x0) * (x2 - x1);
            if (Normal(q0) && Normal(q1) && Normal(q2))
            {
                double t0 = f0 * ((at - x1) + (at - x2)) / q0;
                double t1 = f1 * ((at - x0) + (at - x2)) / q1;
                double t2 = f2 * ((at - x0) + (at - x1)) / q2;
                double sum = t0 + t1 + t2;
                if (!double.IsNaN(sum) && !double.IsInfinity(sum)) return sum;
                // Values near ±double.MaxValue overflowed f·weight (a flat 1e308 gave NaN): weights first, in eighths.
                return 8 * (f0 / 8 * (((at - x1) + (at - x2)) / q0)
                          + f1 / 8 * (((at - x0) + (at - x2)) / q1)
                          + f2 / 8 * (((at - x0) + (at - x1)) / q2));
            }
            // The product of two spacings overflowed (points 1e160 apart) or underflowed: divide by one at a time.
            return f0 * (((at - x1) + (at - x2)) / (x0 - x1) / (x0 - x2))
                 + f1 * (((at - x0) + (at - x2)) / (x1 - x0) / (x1 - x2))
                 + f2 * (((at - x0) + (at - x1)) / (x2 - x0) / (x2 - x1));
        }

        // A slope past double range (1e308 across 1e-306 at the end of a 1e308-wide grid) overflowed the Newton form and the
        // Lagrange weights alike, so the end derivative was ∞ − ∞ = NaN. Its two terms Δf₀₁·(1 − r)/Δx₀₁ and Δf₁₂·r/Δx₁₂
        // are formed as mantissa and exponent, where nothing overflows, and added last: a signed ∞ past double range.
        private static double WideNewton(double at, double x0, double x1, double x2, double f0, double f1, double f2)
        {
            double spread = Math.Max(Math.Max(x0, x1), x2) - Math.Min(Math.Min(x0, x1), x2);
            if (double.IsInfinity(spread)) // as in Quadratic: halving such positions is exact
                return 0.5 * WideNewton(0.5 * at, 0.5 * x0, 0.5 * x1, 0.5 * x2, f0, f1, f2);
            // r = pb / s and 1 − r = pa / s, each from its own differences: 1 − r cancelled to 0 at at = x1 when x1 − x0
            // rounded to x2 − x0, dropping the term that overflowed.
            double s = x2 - x0, pa = (x2 - at) + (x1 - at), pb = (at - x0) + (at - x1);
            if (double.IsInfinity(pa) || double.IsInfinity(pb))
            {
                pa = 0.5 * (x2 - at) + 0.5 * (x1 - at);
                pb = 0.5 * (at - x0) + 0.5 * (at - x1);
                s *= 0.5;
            }
            double a = Term(f0, f1, pa, s, x1 - x0, out int ea);
            double b = Term(f1, f2, pb, s, x2 - x1, out int eb);
            if (a == 0) return Ldexp(b, eb);
            if (b == 0) return Ldexp(a, ea);
            int e = Math.Max(ea, eb);
            return Ldexp(Ldexp(a, ea - e) + Ldexp(b, eb - e), e);
        }

        // (g − f)·p/(q·den) as m·2^e with |m| in (0.25, 4); g − f is halved only when it overflows (±1e308 apart).
        private static double Term(double f, double g, double p, double q, double den, out int e)
        {
            double df = g - f;
            int half = 0;
            if (double.IsInfinity(df)) { df = 0.5 * g - 0.5 * f; half = 1; }
            e = 0;
            if (df == 0 || p == 0) return 0;
            double m = Frexp(df, out int en) * Frexp(p, out int ep) / (Frexp(q, out int eq) * Frexp(den, out int ed));
            e = en + half + ep - eq - ed;
            return m;
        }

        // v = m·2^e with 1 ≤ |m| < 2, for finite non-zero v (subnormals included).
        private static double Frexp(double v, out int e)
        {
            int bias = 0;
            if (Math.Abs(v) < 2.2250738585072014E-308) { v *= 18446744073709551616.0; bias = 64; } // 2⁶⁴
            long bits = BitConverter.DoubleToInt64Bits(v);
            e = (int)((bits >> 52) & 0x7FF) - 1023 - bias;
            return BitConverter.Int64BitsToDouble((bits & unchecked((long)0x800FFFFFFFFFFFFFUL)) | (1023L << 52));
        }

        // v·2^e rounded once: a normal result is exact, a subnormal one rounds in the last multiply only.
        private static double Ldexp(double v, int e)
        {
            if (v == 0) return v;
            double m = Frexp(v, out int k);
            long p = (long)e + k;
            if (p > 1023) return m * double.PositiveInfinity;
            if (p >= -1022) return m * Pow2((int)p);
            if (p < -1100) return m * 0.0;
            return m * Pow2((int)p + 1022) * Pow2(-1022);
        }

        private static double Pow2(int e) => BitConverter.Int64BitsToDouble((long)(e + 1023) << 52); // −1022 ≤ e ≤ 1023

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static bool Normal(double v)
        {
            v = Math.Abs(v);
            return v >= 2.2250738585072014E-308 && !double.IsInfinity(v);
        }
    }
}
