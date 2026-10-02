// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// The handful of special functions that statistics keeps needing and the BCL never ships: the gamma
    /// function and its logarithm, the error function, and the regularized incomplete gamma and beta
    /// functions. These are the building blocks behind the chi-square, Student-t and F distributions.
    /// Implementations follow the standard references (Lanczos for log-gamma; series/continued-fraction for
    /// the incomplete functions) and are accurate to roughly 1e-12 over the usual argument ranges.
    /// </summary>
    public static class SpecialFunctions
    {
        private const double Tiny = 1e-300;
        private const double Epsilon = 1e-15;
        private const int MaxIterations = 300;

        // Lanczos g = 7, n = 9 approximation coefficients.
        private static readonly double[] LanczosG =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };

        /// <summary>Natural log of the gamma function, for <paramref name="x"/> &gt; 0.</summary>
        public static double LnGamma(double x)
        {
            if (x <= 0) throw new ArgumentOutOfRangeException(nameof(x), x, "LnGamma requires x > 0.");
            double a = LanczosG[0];
            double t = x + 6.5; // x-1 + 7 + 0.5
            double xm1 = x - 1;
            for (int i = 1; i < LanczosG.Length; i++)
                a += LanczosG[i] / (xm1 + i);
            return 0.5 * Math.Log(2 * Math.PI) + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }

        /// <summary>The gamma function (Euler's). Defined for all reals except the non-positive integers.</summary>
        public static double Gamma(double x)
        {
            if (x > 0) return Math.Exp(LnGamma(x));
            if (x == Math.Floor(x)) throw new ArgumentException("Gamma is undefined at non-positive integers.", nameof(x));
            // Reflection: Γ(x)·Γ(1-x) = π / sin(πx).
            return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
        }

        /// <summary>The beta function B(a, b) = Γ(a)Γ(b)/Γ(a+b), for a, b &gt; 0.</summary>
        public static double Beta(double a, double b)
        {
            if (a <= 0) throw new ArgumentOutOfRangeException(nameof(a), a, "Must be positive.");
            if (b <= 0) throw new ArgumentOutOfRangeException(nameof(b), b, "Must be positive.");
            return Math.Exp(LnGamma(a) + LnGamma(b) - LnGamma(a + b));
        }

        /// <summary>The error function erf(x).</summary>
        public static double Erf(double x)
        {
            double p = RegularizedGammaP(0.5, x * x);
            return x < 0 ? -p : p;
        }

        /// <summary>The complementary error function erfc(x) = 1 - erf(x).</summary>
        public static double Erfc(double x) => 1.0 - Erf(x);

        /// <summary>
        /// The regularized lower incomplete gamma function P(a, x) = γ(a, x) / Γ(a), for a &gt; 0, x ≥ 0.
        /// Ranges from 0 at x = 0 to 1 as x → ∞.
        /// </summary>
        public static double RegularizedGammaP(double a, double x)
        {
            if (a <= 0) throw new ArgumentOutOfRangeException(nameof(a), a, "Must be positive.");
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), x, "Must be non-negative.");
            if (x == 0) return 0.0;

            if (x < a + 1.0)
            {
                // Power series.
                double ap = a;
                double sum = 1.0 / a;
                double del = sum;
                for (int n = 0; n < MaxIterations; n++)
                {
                    ap += 1.0;
                    del *= x / ap;
                    sum += del;
                    if (Math.Abs(del) < Math.Abs(sum) * Epsilon) break;
                }
                return sum * Math.Exp(-x + a * Math.Log(x) - LnGamma(a));
            }
            else
            {
                // Lentz continued fraction for Q = 1 - P, then complement.
                double b = x + 1.0 - a;
                double c = 1.0 / Tiny;
                double d = 1.0 / b;
                double h = d;
                for (int i = 1; i < MaxIterations; i++)
                {
                    double an = -i * (i - a);
                    b += 2.0;
                    d = an * d + b; if (Math.Abs(d) < Tiny) d = Tiny;
                    c = b + an / c; if (Math.Abs(c) < Tiny) c = Tiny;
                    d = 1.0 / d;
                    double del = d * c;
                    h *= del;
                    if (Math.Abs(del - 1.0) < Epsilon) break;
                }
                double q = Math.Exp(-x + a * Math.Log(x) - LnGamma(a)) * h;
                return 1.0 - q;
            }
        }

        /// <summary>The regularized upper incomplete gamma function Q(a, x) = 1 - P(a, x).</summary>
        public static double RegularizedGammaQ(double a, double x) => 1.0 - RegularizedGammaP(a, x);

        /// <summary>
        /// The regularized incomplete beta function I_x(a, b), for a, b &gt; 0 and x in [0, 1]. Ranges from 0
        /// at x = 0 to 1 at x = 1.
        /// </summary>
        public static double RegularizedBetaI(double x, double a, double b)
        {
            if (a <= 0) throw new ArgumentOutOfRangeException(nameof(a), a, "Must be positive.");
            if (b <= 0) throw new ArgumentOutOfRangeException(nameof(b), b, "Must be positive.");
            if (x < 0 || x > 1) throw new ArgumentOutOfRangeException(nameof(x), x, "Must be in [0, 1].");
            if (x == 0) return 0.0;
            if (x == 1) return 1.0;

            double front = Math.Exp(
                LnGamma(a + b) - LnGamma(a) - LnGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));

            // The continued fraction converges fast only for x < (a+1)/(a+b+2); otherwise use the symmetry
            // I_x(a,b) = 1 - I_{1-x}(b,a).
            if (x < (a + 1.0) / (a + b + 2.0))
                return front * BetaContinuedFraction(x, a, b) / a;
            return 1.0 - front * BetaContinuedFraction(1 - x, b, a) / b;
        }

        // Lentz's algorithm for the beta continued fraction.
        private static double BetaContinuedFraction(double x, double a, double b)
        {
            double qab = a + b, qap = a + 1.0, qam = a - 1.0;
            double c = 1.0;
            double d = 1.0 - qab * x / qap;
            if (Math.Abs(d) < Tiny) d = Tiny;
            d = 1.0 / d;
            double h = d;
            for (int m = 1; m <= MaxIterations; m++)
            {
                double m2 = 2.0 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1.0 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1.0 / d;
                h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1.0 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1.0 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1.0) < Epsilon) break;
            }
            return h;
        }
    }
}
