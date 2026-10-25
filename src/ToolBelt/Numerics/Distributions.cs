// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Probability density (Pdf), cumulative distribution (Cdf) and quantile (inverse CDF) functions for the common
    /// continuous distributions: normal, log-normal, exponential, uniform, Student's t, chi-square and F.
    /// <para>
    /// Accuracy: the normal, t, chi-square and F functions are built on regularized incomplete gamma and beta functions
    /// that return each tail directly (never 1 − the other), with cancellation-free log-beta and gamma-prefix forms for
    /// large parameters. Graded against 40-digit references in the hard regimes — tails down to 1e-300, x near 0,
    /// fractional and enormous (to 1e9) degrees of freedom — the worst relative error measured is below 1e-13 (see the
    /// test suite's reference table and its generator script). For the t distribution, the incomplete-beta continued
    /// fraction (whose error grows with the degrees of freedom) and a large-df asymptotic expansion (Didonato &amp; Morris'
    /// BGRAT with one correction term) are chosen per call by estimated error. Quantiles are found by a safeguarded
    /// Newton iteration in whichever space keeps precision — the upper-tail mass when p is near 1, the central mass when
    /// p is near ½ — so <c>StudentTQuantile(1 − 1e-12, 3)</c> is as accurate as the median. The normal quantile starts
    /// from Acklam's rational approximation and is polished by one Halley step. Degrees of freedom may be any positive
    /// real. Results below the smallest double (e.g. P(T &lt; −40) at large df) underflow to 0. Pairs with the
    /// reproducible generator for simulation; for secrets use the cryptographic random source instead.
    /// </para>
    /// </summary>
    public static class Distributions
    {
        private const double Sqrt2Pi = 2.5066282746310002;
        private const double LnSqrt2Pi = 0.91893853320467274178;

        // ---- Normal ----

        public static double NormalPdf(double x, double mean = 0, double standardDeviation = 1)
        {
            RequirePositive(standardDeviation, nameof(standardDeviation));
            double z = (x - mean) / standardDeviation;
            return Math.Exp(-0.5 * z * z) / (standardDeviation * Sqrt2Pi);
        }

        public static double NormalCdf(double x, double mean = 0, double standardDeviation = 1)
        {
            RequirePositive(standardDeviation, nameof(standardDeviation));
            return StandardNormalCdf((x - mean) / standardDeviation);
        }

        /// <summary>
        /// Upper tail P(X &gt; x), computed directly so tiny tail probabilities keep full relative precision (1 − Cdf rounds
        /// them to 0 below about 1e-16). Use it for p-values. Student's t, chi-square and F have matching <c>…Survival</c>
        /// methods.
        /// </summary>
        public static double NormalSurvival(double x, double mean = 0, double standardDeviation = 1)
        {
            RequirePositive(standardDeviation, nameof(standardDeviation));
            return StandardNormalCdf(-(x - mean) / standardDeviation);
        }

        /// <summary>The inverse normal CDF (quantile) for a probability in (0, 1).</summary>
        public static double NormalQuantile(double p, double mean = 0, double standardDeviation = 1)
        {
            RequirePositive(standardDeviation, nameof(standardDeviation));
            RequireOpenProbability(p);
            return mean + standardDeviation * StandardNormalQuantile(p);
        }

        // ---- Log-normal (parameters of the underlying normal: ln X ~ N(mu, sigma²)) ----

        public static double LogNormalPdf(double x, double mu = 0, double sigma = 1)
        {
            RequirePositive(sigma, nameof(sigma));
            if (x <= 0) return 0;
            double z = (Math.Log(x) - mu) / sigma;
            return Math.Exp(-0.5 * z * z) / (x * sigma * Sqrt2Pi);
        }

        public static double LogNormalCdf(double x, double mu = 0, double sigma = 1)
        {
            RequirePositive(sigma, nameof(sigma));
            return x <= 0 ? 0 : StandardNormalCdf((Math.Log(x) - mu) / sigma);
        }

        public static double LogNormalQuantile(double p, double mu = 0, double sigma = 1)
        {
            RequirePositive(sigma, nameof(sigma));
            RequireOpenProbability(p);
            return Math.Exp(mu + sigma * StandardNormalQuantile(p));
        }

        // ---- Exponential ----

        public static double ExponentialPdf(double x, double rate)
        {
            RequirePositive(rate, nameof(rate));
            return x < 0 ? 0 : rate * Math.Exp(-rate * x);
        }

        public static double ExponentialCdf(double x, double rate)
        {
            RequirePositive(rate, nameof(rate));
            return x < 0 ? 0 : -Expm1(-rate * x);              // 1 − e^(−λx) without cancellation for small λx
        }

        public static double ExponentialQuantile(double p, double rate)
        {
            RequirePositive(rate, nameof(rate));
            if (!(p >= 0 && p < 1)) throw new ArgumentOutOfRangeException(nameof(p), p, "Probability must be in [0, 1).");
            return -Log1p(-p) / rate;
        }

        // ---- Uniform ----

        public static double UniformPdf(double x, double a, double b)
        {
            RequireOrdered(a, b);
            return (x < a || x > b) ? 0 : 1.0 / (b - a);
        }

        public static double UniformCdf(double x, double a, double b)
        {
            RequireOrdered(a, b);
            if (x < a) return 0;
            if (x > b) return 1;
            return (x - a) / (b - a);
        }

        // ---- Student's t ----

        public static double StudentTPdf(double x, double degreesOfFreedom)
        {
            double v = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            // Γ((v+1)/2) / (√(vπ) Γ(v/2)) = 1 / (√v · B(v/2, 1/2)); the log-beta form stays exact for huge v.
            // ln(1 + z²) with z = x/√v; for |z| > 1e8 as 2·ln|z| + ln(1 + 1/z²), since z² overflows beyond 1e154.
            double z = Math.Abs(x) / Math.Sqrt(v);
            double ln1pz2 = z > 1e8 ? 2 * Math.Log(z) + 1 / (z * z) : Log1p(z * z);
            return Math.Exp(-LnBeta(v / 2, 0.5) - 0.5 * Math.Log(v) - (v + 1) / 2 * ln1pz2);
        }

        public static double StudentTCdf(double x, double degreesOfFreedom)
        {
            double v = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            if (double.IsNaN(x)) return double.NaN;
            var (tail, central) = TMasses(Math.Abs(x), v);
            return x >= 0 ? 0.5 + central : tail;
        }

        /// <summary>Upper tail P(T &gt; x), computed directly (full precision for tiny tails).</summary>
        public static double StudentTSurvival(double x, double degreesOfFreedom)
        {
            double v = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            if (double.IsNaN(x)) return double.NaN;
            var (tail, central) = TMasses(Math.Abs(x), v);
            return x >= 0 ? tail : 0.5 + central;
        }

        /// <summary>The t quantile, e.g. <c>StudentTQuantile(0.975, n − 1)</c> for a two-sided 95% interval.</summary>
        public static double StudentTQuantile(double p, double degreesOfFreedom)
        {
            double v = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            RequireOpenProbability(p);
            if (p == 0.5) return 0;
            // By symmetry solve for |t| using the smaller upper-tail mass u, computed without cancellation.
            double u = p < 0.5 ? p : 1 - p; // exact for p >= 0.5 (Sterbenz)
            double sign = p < 0.5 ? -1 : 1;
            double guess = Math.Abs(StandardNormalQuantile(u));
            double t;
            if (u < 0.25)
                t = SolveDecreasing(x => TMasses(x, v).Tail, x => StudentTPdf(x, v), u, guess);
            else
                t = SolveIncreasing(x => TMasses(x, v).Central, x => StudentTPdf(x, v), 0.5 - u, guess);
            return sign * t;
        }

        // ---- Chi-square ----

        public static double ChiSquarePdf(double x, double degreesOfFreedom)
        {
            double k = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            if (x < 0) return 0;
            if (x == 0) return k < 2 ? double.PositiveInfinity : (k == 2 ? 0.5 : 0);
            if (double.IsPositiveInfinity(x)) return 0;
            return Math.Exp(LnPrefix(k / 2, x / 2)) / x; // ½(x/2)^(a−1)e^(−x/2)/Γ(a) = [ (x/2)^a e^(−x/2)/Γ(a) ] / x
        }

        public static double ChiSquareCdf(double x, double degreesOfFreedom)
        {
            double k = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            if (x <= 0) return 0;
            GammaPQ(k / 2, x / 2, out double p, out _);
            return p;
        }

        /// <summary>Upper tail P(X &gt; x), computed directly (full precision for tiny tails).</summary>
        public static double ChiSquareSurvival(double x, double degreesOfFreedom)
        {
            double k = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            if (double.IsNaN(x)) return double.NaN;
            if (x <= 0) return 1;
            GammaPQ(k / 2, x / 2, out _, out double q);
            return q;
        }

        /// <summary>The chi-square quantile, e.g. the 95% critical value <c>ChiSquareQuantile(0.95, k)</c>.</summary>
        public static double ChiSquareQuantile(double p, double degreesOfFreedom)
        {
            double k = RequireDf(degreesOfFreedom, nameof(degreesOfFreedom));
            RequireOpenProbability(p);
            double a = k / 2;
            double guess = ChiSquareGuess(p, k);
            if (p <= 0.5)
                return SolveIncreasing(x => { GammaPQ(a, x / 2, out double lo, out _); return lo; }, x => ChiSquarePdf(x, k), p, guess);
            return SolveDecreasing(x => { GammaPQ(a, x / 2, out _, out double up); return up; }, x => ChiSquarePdf(x, k), 1 - p, guess);
        }

        // ---- F (Fisher–Snedecor) ----

        public static double FPdf(double x, double numeratorDf, double denominatorDf)
        {
            double d1 = RequireDf(numeratorDf, nameof(numeratorDf)), d2 = RequireDf(denominatorDf, nameof(denominatorDf));
            if (x < 0) return 0;
            if (x == 0) return d1 < 2 ? double.PositiveInfinity : (d1 == 2 ? 1 : 0);
            // r = d1x/(d1x + d2): density = r^(d1/2) (1 − r)^(d2/2) / (x · B(d1/2, d2/2)).
            double den = d1 * x + d2, r = d1 * x / den, rc = d2 / den;
            return Math.Exp(d1 / 2 * LnOf(r, rc) + d2 / 2 * LnOf(rc, r) - Math.Log(x) - LnBeta(d1 / 2, d2 / 2));
        }

        public static double FCdf(double x, double numeratorDf, double denominatorDf)
        {
            double d1 = RequireDf(numeratorDf, nameof(numeratorDf)), d2 = RequireDf(denominatorDf, nameof(denominatorDf));
            if (x <= 0) return 0;
            return FMasses(x, d1, d2).Lower;
        }

        /// <summary>Upper tail P(F &gt; x), computed directly (full precision for tiny tails).</summary>
        public static double FSurvival(double x, double numeratorDf, double denominatorDf)
        {
            double d1 = RequireDf(numeratorDf, nameof(numeratorDf)), d2 = RequireDf(denominatorDf, nameof(denominatorDf));
            if (double.IsNaN(x)) return double.NaN;
            if (x <= 0) return 1;
            return FMasses(x, d1, d2).Upper;
        }

        /// <summary>The F quantile, e.g. the critical value <c>FQuantile(0.95, d1, d2)</c>.</summary>
        public static double FQuantile(double p, double numeratorDf, double denominatorDf)
        {
            double d1 = RequireDf(numeratorDf, nameof(numeratorDf)), d2 = RequireDf(denominatorDf, nameof(denominatorDf));
            RequireOpenProbability(p);
            double guess = 1;
            if (p < 1e-10)
            {
                // Far in the lower tail F ≈ yᵃ/(a·B(a, b)) with y = d1·x/(d1·x + d2): start there rather than at 1.
                double a = d1 / 2, b = d2 / 2;
                double y = Math.Exp((Math.Log(p) + Math.Log(a) + LnBeta(a, b)) / a);
                if (y > 0 && y < 0.01) guess = d2 * y / (d1 * (1 - y));
            }
            if (p <= 0.5)
                return SolveIncreasing(x => FMasses(x, d1, d2).Lower, x => FPdf(x, d1, d2), p, guess);
            return SolveDecreasing(x => FMasses(x, d1, d2).Upper, x => FPdf(x, d1, d2), 1 - p, guess);
        }

        // ================= internals =================

        private static double StandardNormalCdf(double z)
        {
            if (double.IsNaN(z)) return double.NaN;
            GammaPQ(0.5, z * z / 2, out double p, out double q); // erf(|z|/√2) = P, erfc = Q
            return z < 0 ? 0.5 * q : 0.5 + 0.5 * p;
        }

        // Acklam's rational approximation (~1e-9 relative), polished by one Halley step against the exact CDF.
        private static double StandardNormalQuantile(double p)
        {
            double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
            double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
            double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
            double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
            const double pLow = 0.02425, pHigh = 1 - 0.02425;

            double x;
            if (p < pLow)
            {
                double q = Math.Sqrt(-2 * Math.Log(p));
                x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                    ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }
            else if (p <= pHigh)
            {
                double q = p - 0.5;
                double r = q * q;
                x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                    (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
            }
            else
            {
                double q = Math.Sqrt(-2 * Math.Log(1 - p));
                x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                     ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }

            // Halley refinement, with the error e = Φ(x) − p measured in the mass that keeps precision: near the median
            // Φ(x) − ½ = ±½P against p − ½ (exact there, Sterbenz) — forming 1 − p − ½Q leaves only absolute precision,
            // a relative error of 1e-9 at p = ½ + 1e-10; in the tails Φ(x) = ½Q for x < 0 and 1 − ½Q for x ≥ 0.
            GammaPQ(0.5, x * x / 2, out double pp, out double qq);
            double e = p > 0.25 && p < 0.75 ? (x < 0 ? -0.5 * pp : 0.5 * pp) - (p - 0.5)
                     : x < 0 ? 0.5 * qq - p : (1 - p) - 0.5 * qq;
            double u = e * Sqrt2Pi * Math.Exp(x * x / 2);
            if (!double.IsNaN(u) && !double.IsInfinity(u)) x -= u / (1 + x * u / 2);
            return x;
        }

        // For x >= 0: Tail = P(T > x), Central = P(0 < T < x). Both computed directly (no 1 − p cancellation).
        //
        // Two methods, chosen per call by estimated error. The incomplete-beta continued fraction loses accuracy in
        // proportion to the degrees of freedom (its odd terms approach −1 near the switch point: ~5e-17·v relative), while
        // the large-a asymptotic expansion of Didonato & Morris (TOMS 708, BGRAT) with its first correction term has
        // error ~0.05·(z+1)⁴/v⁴. Measured against 50-digit references this keeps the t CDF near 1e-13 or better for
        // every v, instead of degrading to ~1e-8 at v = 1e9.
        private static (double Tail, double Central) TMasses(double x, double v)
        {
            if (x == 0) return (0.5, 0);
            if (double.IsPositiveInfinity(x)) return (0, 0.5);
            if (v >= 100)
            {
                // The asymptotic error is relative to the tail (≈ ½ near the median); the central mass is ½ − tail, so near
                // the median its relative error is larger by tail/central. The continued fraction computes either mass
                // directly with relative error ~5e-17·v. Pick whichever is smaller for the mass that may be needed.
                double z = (v / 2 - 0.25) * Log1p(x * x / v);
                double v2 = v * v;
                double asymRel = 0.05 * Math.Pow(z + 1, 4) / (v2 * v2);
                double centralEstimate = 0.5 * Math.Min(1, 1.1283791670955126 * Math.Sqrt(z)); // ½·erf(√z) ≈ √(z/π) for small z
                double amplification = centralEstimate > 0 ? Math.Max(1, 0.5 / centralEstimate) : double.PositiveInfinity;
                if (asymRel * amplification < 5e-17 * v) return TMassesAsymptotic(x, v);
            }
            // P(|T| > x) = I_w(v/2, 1/2) with w = v/(v+x²); 1 − w = x²/(v+x²) is formed directly, not by subtraction. Past
            // x² = v both come from r = (√v/x)², so x beyond 1e154 (where x² overflows) still has a tail.
            double w, wc;
            if (x * x <= v) { double x2 = x * x; w = v / (v + x2); wc = x2 / (v + x2); }
            else
            {
                double r = Math.Sqrt(v) / x, r2 = r * r;
                if (r2 < 1e-280)
                {
                    // w ≈ v/x² underflows (x near 1e200 for v = ½): I_w(a, ½) = w^a/(a·B(a, ½))·(1 + O(w)) = r^v/(a·B(a, ½)).
                    // r^v by Pow while r is a normal double; exp of the log (≈ −690) would carry its ulp error ×690.
                    double scale = Math.Exp(-Math.Log(v / 2) - LnBeta(v / 2, 0.5));
                    double tail = r >= 2.2250738585072014e-308 ? Math.Pow(r, v) * scale
                                             : Math.Exp(v / 2 * (Math.Log(v) - 2 * Math.Log(x)) - Math.Log(v / 2) - LnBeta(v / 2, 0.5));
                    return (0.5 * tail, 0.5 - 0.5 * tail);
                }
                w = r2 / (1 + r2); wc = 1 / (1 + r2);
            }
            BetaI(w, wc, v / 2, 0.5, out double ix, out double ixc);
            return (0.5 * ix, 0.5 * ixc);
        }

        // BGRAT-style expansion for I_y(1/2, v/2), the t tail at large v: with ν = v/2 − ¼ and z = ν·ln(1 + x²/v),
        //   tail = ½·R·(Q(½, z) − c),  c = (¾·Q + (z + 3/2)·e^(−z)·√(z/π)) / (48ν²),  R = Γ(v/2 + ½) / (Γ(v/2)·√ν).
        // The central mass is formed from its own pieces, P − (R − 1)·Q + R·c, so it stays accurate for tiny x.
        private static (double Tail, double Central) TMassesAsymptotic(double x, double v)
        {
            double a = v / 2, nu = a - 0.25;
            double z = nu * Log1p(x * x / v);
            // R − 1 is tiny (~1/(64a²)) and the central mass for small x can be smaller still, so it is summed from its
            // asymptotic series (coefficients extracted at 150 digits; truncation < 2e-22 for a >= 500) rather than formed
            // as expm1 of lnR, whose O(10)-sized log-gamma pieces carry ~1e-15 absolute error.
            double rm1;
            if (a >= 500)
            {
                double ia = 1 / a;
                rm1 = ia * ia * (1.0 / 64 + ia * (1.0 / 128 + ia * (5.0 / 8192 + ia * (-11.0 / 8192 + ia * (31.0 / 524288)))));
            }
            else
            {
                rm1 = Expm1(0.5 * Math.Log(Math.PI) - LnBeta(a, 0.5) - 0.5 * Math.Log(nu)); // lnR = lnΓ(½) − lnB(a, ½) − ½ln ν
            }
            double r = 1 + rm1;
            GammaPQ(0.5, z, out double p, out double q);
            double c = (0.75 * q + (z + 1.5) * Math.Exp(-z) * Math.Sqrt(z / Math.PI)) / (48 * nu * nu);
            return (0.5 * r * (q - c), 0.5 * (p - rm1 * q + r * c));
        }

        // Lower = P(F < x), Upper = P(F > x), each computed directly.
        private static (double Lower, double Upper) FMasses(double x, double d1, double d2)
        {
            if (double.IsPositiveInfinity(x)) return (1, 0);
            double num = d1 * x, den = d1 * x + d2;
            BetaI(num / den, d2 / den, d1 / 2, d2 / 2, out double lower, out double upper);
            return (lower, upper);
        }

        private static double ChiSquareGuess(double p, double k)
        {
            // Wilson–Hilferty; for small p and few degrees of freedom use the leading term of the lower tail instead.
            double z = StandardNormalQuantile(p);
            double h = 2 / (9 * k);
            double wh = k * Math.Pow(Math.Max(1 - h + z * Math.Sqrt(h), 1e-3), 3);
            if (p < 0.05)
            {
                // The lower tail's leading term (x/2)^a/Γ(a + 1): good for few degrees of freedom, and for any number once
                // x ≪ k (Wilson–Hilferty, clamped at k·1e-9, started Newton 100 orders of magnitude too high at p = 1e-300).
                double a = k / 2;
                double small = 2 * Math.Exp((Math.Log(p) + LnGamma(a + 1)) / a);
                if (small > 0 && !double.IsInfinity(small) && (k < 5 || small < 0.01 * k)) return small;
            }
            return wh > 0 ? wh : k;
        }

        // ---- root finding: safeguarded Newton with bracketing ----

        /// <summary>Solves f(x) = target for x >= 0, where f increases from f(0) toward its limit.</summary>
        private static double SolveIncreasing(Func<double, double> f, Func<double, double> slope, double target, double guess)
            => Solve(x => f(x) - target, slope, guess);

        /// <summary>Solves f(x) = target for x >= 0, where f decreases (a tail mass).</summary>
        private static double SolveDecreasing(Func<double, double> f, Func<double, double> negSlope, double target, double guess)
            => Solve(x => target - f(x), negSlope, guess);

        // g is increasing on [0, ∞) with g(0) <= 0 < g(∞); g'(x) = slope(x) >= 0. Geometric midpoints take separate roots
        // (lo·hi overflows for roots past 1e154).
        private static double Solve(Func<double, double> g, Func<double, double> slope, double guess)
        {
            double lo = 0, hi = guess > 0 && !double.IsInfinity(guess) ? guess : 1;
            for (int i = 0; i < 2100 && g(hi) < 0; i++) { lo = hi; hi *= 2; }   // expand until bracketed
            if (g(hi) < 0) return hi;

            double x = guess > lo && guess < hi ? guess : (lo > 0 ? Math.Sqrt(lo) * Math.Sqrt(hi) : hi / 2);
            double jump = 16;
            int slow = 0;
            for (int iter = 0; iter < 400; iter++)
            {
                double gx = g(x);
                if (gx == 0) return x;
                if (gx < 0) lo = x; else hi = x;

                double d = slope(x);
                double next = d > 0 && !double.IsInfinity(d) ? x - gx / d : double.NaN;
                if (!(next > lo && next < hi))
                    next = lo > 0 && hi / lo > 4 ? Math.Sqrt(lo) * Math.Sqrt(hi) : (lo == 0 && hi > 1e-300 && x == hi ? hi / 16 : 0.5 * (lo + hi));
                else if (lo == 0 && next > 0.5 * x && next < 0.999 * x)
                {
                    // Above the root with nothing below it yet, on a steep power law (a far lower tail, CDF ~ xᵐ), Newton
                    // shrinks x by only (1 − 1/m) a step and ran out of iterations, returning the unconverged x. After three
                    // such steps jump down by a growing factor until the root is bracketed.
                    if (++slow >= 3)
                    {
                        double down = x / jump;
                        if (down > 0) next = down;
                        jump = Math.Min(jump * jump, 1e100);
                        slow = 0;
                    }
                }
                else slow = 0;
                if (Math.Abs(next - x) <= 4e-16 * Math.Abs(next)) return next;
                x = next;
                if (hi - lo <= 4e-16 * hi) return x;
            }
            return x;
        }

        // ---- special functions (duplicated privately: no leaf-to-leaf dependency) ----

        private static readonly double[] Lanczos =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
        };

        private static double LnGamma(double x)
        {
            if (double.IsPositiveInfinity(x)) return x;
            if (x < 0.5)
            {
                // Γ(x) = Γ(x + 1)/x for 0 < x < ½; the reflection formula's π/sin(πx) overflows for x below ~1e-308.
                if (x > 0) return LnGamma(x + 1) - Math.Log(x);
                return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LnGamma(1 - x); // reflection
            }
            // Near its zeros at 1 and 2 ln Γ is tiny, and the Lanczos sum, accurate only absolutely, kept ~1e-5 of it.
            if (x >= 0.8 && x <= 1.2) return LnGammaNearOne(x - 1);
            if (x >= 1.8 && x <= 2.2) return Math.Log(x - 1) + LnGammaNearOne(x - 2);   // x − 1 is exact
            double a = Lanczos[0];
            double t = x + 6.5;
            double xm1 = x - 1;
            for (int i = 1; i < Lanczos.Length; i++) a += Lanczos[i] / (xm1 + i);
            return LnSqrt2Pi + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }

        // ζ(2) … ζ(25), for the series of ln Γ(1 + z).
        private static readonly double[] Zeta =
        {
            1.6449340668482264, 1.2020569031595942, 1.0823232337111381, 1.03692775514337, 1.0173430619844492,
            1.0083492773819229, 1.0040773561979444, 1.0020083928260821, 1.000994575127818, 1.0004941886041194,
            1.000246086553308, 1.0001227133475785, 1.0000612481350588, 1.000030588236307, 1.0000152822594086,
            1.0000076371976379, 1.000003817293265, 1.0000019082127165, 1.0000009539620338, 1.0000004769329869,
            1.0000002384505027, 1.0000001192199259, 1.0000000596081891, 1.0000000298035034,
        };

        // ln Γ(1 + z) = −γz + Σₖ₌₂ ζ(k)(−z)ᵏ/k for |z| <= 0.2 (the terms fall by 5× each, 24 of them reach 1e-18 relative).
        private static double LnGammaNearOne(double z)
        {
            double w = -z, wk = w, sum = 0.57721566490153286 * w;
            for (int k = 2; k < Zeta.Length + 2; k++)
            {
                wk *= w;
                sum += Zeta[k - 2] * wk / k;
            }
            return sum;
        }

        private const double Tiny = 1e-300;
        private const double Eps = 1e-16;

        /// <summary>Regularized incomplete gamma P(a, x) and its complement Q(a, x), each computed directly.</summary>
        private static void GammaPQ(double a, double x, out double p, out double q)
        {
            if (x <= 0) { p = 0; q = 1; return; }
            if (double.IsPositiveInfinity(x)) { p = 1; q = 0; return; }
            if (a >= TemmeMinShape) { GammaTemme(a, x, out p, out q); return; }
            double lnPre = LnPrefix(a, x);
            if (x < a + 1)
            {
                double ap = a, sum = 1.0 / a, del = sum;
                for (int n = 0; n < 100000; n++)
                {
                    ap += 1;
                    del *= x / ap;
                    sum += del;
                    if (Math.Abs(del) < Math.Abs(sum) * Eps) break;
                }
                p = sum * Math.Exp(lnPre);
                if (p > 1) p = 1;
                q = 1 - p;
                if (a < 1 && p > 0.5)
                {
                    // A small shape puts P near 1, and 1 − P kept few of Q's digits (3e-8 relative at a = 5e-9). Directly:
                    // Q = 1 − xᵃ/Γ(a + 1) − a·xᵃ/Γ(a + 1)·Σₙ₌₁ (−x)ⁿ/(n!(a + n)), the first difference by expm1.
                    // lnΓ(1 + a) from a itself: a + 1 rounds away ~1e-16 of a, a relative 2e-8 of a shape near 5e-9.
                    double u = a * Math.Log(x) - (a <= 0.2 ? LnGammaNearOne(a) : LnGamma(a + 1)), term = 1, s = 0;
                    for (int n = 1; n < 200; n++)
                    {
                        term *= -x / n;
                        double t = term / (a + n);
                        s += t;
                        if (Math.Abs(t) <= Eps * Math.Abs(s)) break;
                    }
                    q = -Expm1(u) - a * Math.Exp(u) * s;
                }
                return;
            }
            q = Math.Exp(lnPre) * GammaCf(a, x);
            if (q > 1) q = 1;
            p = 1 - q;
        }

        // Continued fraction for Q(a, x) without its prefix x^a e^(−x)/Γ(a) (modified Lentz), for x >= a + 1.
        private static double GammaCf(double a, double x)
        {
            double b = x + 1 - a, c = 1 / Tiny, d = 1 / b, h = d;
            for (int i = 1; i < 100000; i++)
            {
                double an = -i * (i - a);
                b += 2;
                d = an * d + b; if (Math.Abs(d) < Tiny) d = Tiny;
                c = b + an / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < Eps) break;
            }
            return h;
        }

        // e^(z²)·erfc(z) for z >= 0, without the underflow of erfc past z ≈ 27.
        private static double Erfcx(double z)
        {
            if (z < 1.5)
            {
                GammaPQ(0.5, z * z, out _, out double q);
                return Math.Exp(z * z) * q;
            }
            return z / 1.7724538509055160 * GammaCf(0.5, z * z); // erfc(z) = (z e^(−z²)/√π)·CF
        }

        // The series and continued fraction above need ~10·√a terms, so past a ≈ 1e8 (chi-square df 2e8) they hit their
        // iteration cap and return silently wrong values. From a = 1e7 up, Temme's uniform asymptotic expansion
        // (DLMF 8.12.7–8.12.10) is used instead, with two correction terms (truncation ~ 4e-3/a², under 1e-16 here):
        //   Q(a, x) = ½·erfc(η·√(a/2)) + e^(−aη²/2)/√(2πa) · (c₀(η) + c₁(η)/a),  ½η² = λ − 1 − ln λ,  λ = x/a,
        // and P by the mirror formula for η < 0, so the small tail is always computed directly. Measured against 40-digit
        // references for a from 1e5 to 1e9 out to ±30σ: below 1.3e-13 relative (the residue is the conditioning of x).
        private const double TemmeMinShape = 1e7;

        // Taylor coefficients of c₀(η) and c₁(η) at η = 0 (exact rationals from series reversion, rounded), used where the
        // closed forms below cancel.
        private static readonly double[] TemmeC0 =
        {
            -0.3333333333333333, 0.08333333333333333, -0.014814814814814815, 0.0011574074074074073, 0.0003527336860670194,
            -0.0001787551440329218, 3.919263178522438e-05, -2.185448510679992e-06, -1.85406221071516e-06,
            8.296711340953087e-07, -1.7665952736826078e-07, 6.707853543401498e-09, 1.0261809784240309e-08,
            -4.382036018453353e-09, 9.14769958223679e-10, -2.5514193994946248e-11,
        };

        private static readonly double[] TemmeC1 =
        {
            -0.001851851851851852, -0.003472222222222222, 0.0026455026455026454, -0.0009902263374485596,
            0.00020576131687242798, -4.018775720164609e-07, -1.8098550334489977e-05, 7.64916091608111e-06,
            -1.6120900894563446e-06, 4.647127802807434e-09, 1.378633446915721e-07, -5.752545603517705e-08,
            1.1951628599778148e-08, -1.7543241719747647e-11,
        };

        private static void GammaTemme(double a, double x, out double p, out double q)
        {
            double t = (x - a) / a, lm1 = t;            // λ − 1
            double y = -a * Log1pmx(t);                  // aη²/2 = a(λ − 1 − ln λ), cancellation-free
            double eta = Math.Sqrt(2 * y / a);
            if (t < 0) eta = -eta;
            double c0 = Math.Abs(eta) < 0.5 ? Horner(TemmeC0, eta) : 1 / lm1 - 1 / eta;
            double c1 = Math.Abs(eta) < 1 ? Horner(TemmeC1, eta)
                : 1 / (eta * eta * eta) - 1 / (lm1 * lm1 * lm1) - 1 / (lm1 * lm1) - 1 / (12 * lm1);
            double r = Math.Exp(-y) / Math.Sqrt(2 * Math.PI * a) * (c0 + c1 / a);
            GammaPQ(0.5, y, out _, out double erfcHalf);
            double half = 0.5 * erfcHalf;                // ½·erfc(|η|·√(a/2))
            if (eta >= 0) { q = half + r; p = 1 - q; }
            else { p = half - r; q = 1 - p; }
        }

        private static double Horner(double[] c, double x)
        {
            double r = 0;
            for (int i = c.Length - 1; i >= 0; i--) r = r * x + c[i];
            return r;
        }

        /// <summary>
        /// Regularized incomplete beta I_x(a, b) and its complement, given x and 1 − x separately (so the caller can
        /// supply 1 − x without cancellation). Each value is computed directly in its own small regime.
        /// </summary>
        private static void BetaI(double x, double oneMinusX, double a, double b, out double ix, out double ixc)
        {
            if (x <= 0) { ix = 0; ixc = 1; return; }
            if (oneMinusX <= 0) { ix = 1; ixc = 0; return; }
            if (Math.Min(a, b) > 100)
            {
                // Near the mode with both parameters large (TOMS 708's region for BASYM): λ = a − (a+b)x, formed on the
                // side where it cancels least; λ >= 0 is the lower side.
                double lambda = a > b ? (a + b) * oneMinusX - b : a - (a + b) * x;
                double limit = 0.03 * Math.Min(a, b);
                if (lambda >= 0 && lambda <= limit) { ix = Basym(a, b, lambda); ixc = 1 - ix; return; }
                if (lambda < 0 && -lambda <= limit) { ixc = Basym(b, a, -lambda); ix = 1 - ixc; return; }
            }
            if (x < (a + 1) / (a + b + 2))
            {
                if (UseBgrat(a, b) && Bgrat(x, oneMinusX, a, b, out ix, out ixc)) return;
                ix = Math.Exp(BetaLnFront(x, oneMinusX, a, b)) * BetaCf(x, oneMinusX, a, b) / a;
                ixc = 1 - ix;
            }
            else
            {
                if (UseBgrat(b, a) && Bgrat(oneMinusX, x, b, a, out ixc, out ix)) return;
                ixc = Math.Exp(BetaLnFront(x, oneMinusX, a, b)) * BetaCf(oneMinusX, x, b, a) / b;
                ix = 1 - ixc;
            }
        }

        // ln( x^a (1 − x)^b / B(a, b) ). For a, b >= 10 the three terms are each ~(a + b) and cancel to a small number, so
        // with Stirling for ln B they are recombined as a·(ln(1 + e₁) − e₁) + b·(ln(1 + e₂) − e₂) + ½ln(ab/(a+b)) − ½ln 2π
        // − corr, where x = x₀(1 + e₁), 1 − x = y₀(1 + e₂) about the mode x₀ = a/(a+b) (a·e₁ + b·e₂ = 0 exactly), the
        // same decomposition as TOMS 708's BRCOMP. The direct form was off by ~1e-11 relative at a = b = 1e6.
        private static double BetaLnFront(double x, double oneMinusX, double a, double b)
        {
            if (Math.Min(a, b) < 10) return a * LnOf(x, oneMinusX) + b * LnOf(oneMinusX, x) - LnBeta(a, b);
            double ab = a + b;
            // λ = a − (a+b)x = (a+b)(1 − x) − b, formed on the side where it cancels least (TOMS 708); e₁ = −λ/a, e₂ = λ/b.
            double lambda = a > b ? ab * oneMinusX - b : a - ab * x;
            // Far from the mode (|e| > 0.6) ln(1 + e) is taken as ln(x/x₀) directly, as BRCOMP does.
            double e1 = -lambda / a, e2 = lambda / b;
            double u = Math.Abs(e1) > 0.6 ? Math.Log(x / (a / ab)) - e1 : Log1pmx(e1);
            double v = Math.Abs(e2) > 0.6 ? Math.Log(oneMinusX / (b / ab)) - e2 : Log1pmx(e2);
            double corr = StirlingCorr(a) + StirlingCorr(b) - StirlingCorr(ab);
            return a * u + b * v + 0.5 * Math.Log(a / ab * b) - LnSqrt2Pi - corr;
        }

        // The continued fraction's relative error grows like 1e-17 × the larger parameter (its odd terms approach −1), so
        // at F's denominator df of 1e8–1e9 it is off by ~1e-8. There the large-parameter expansion takes over; measured
        // against 40-digit references it stays near 1e-14 while small² ≤ big (it diverges once small² ≫ big).
        private static bool UseBgrat(double big, double small) => big >= 1000 && small * small <= big;

        // Didonato & Morris' asymptotic expansion (TOMS 708, BGRAT) for I_x(a, b) with a large and b small:
        //   I_x(a, b) = R·(Q(b, z) + r·Σ dₙJₙ),  ν = a + (b − 1)/2,  z = −ν·ln x,  R = Γ(a+b)/(Γ(a)·ν^b),
        //   r = z^b·e^(−z)/Γ(b). The complement 1 − I is formed from its own pieces, P(b, z) − (R − 1)·Q − R·r·Σ, so
        // it stays accurate too (both callers have z ≳ b + 1, where P is not small). Returns false if it fails to
        // converge, and the caller falls back to the continued fraction.
        private static bool Bgrat(double x, double oneMinusX, double a, double b, out double ix, out double ixc)
        {
            ix = ixc = double.NaN;
            double bm1 = b - 1, nu = a + 0.5 * bm1;
            double lnx = oneMinusX > 0.375 ? Math.Log(x) : Log1p(-oneMinusX);
            double z = -nu * lnx;
            if (!(z > 0)) return false;
            // ln R = lnΓ(a+b) − lnΓ(a) − b·ln ν, from Stirling with the a·ln a pieces cancelled analytically (a >= 1000).
            double lnR = (a + b - 0.5) * Log1p(b / a) - b - b * Log1p(bm1 / (2 * a)) + StirlingCorr(a + b) - StirlingCorr(a);
            double rr = Math.Exp(lnR), rm1 = Expm1(lnR);
            GammaPQ(b, z, out double p, out double q);
            double r = Math.Exp(LnPrefix(b, z));
            double sum = 0;
            if (r > 0)
            {
                double v = 0.25 / (nu * nu), t2 = 0.25 * lnx * lnx;
                double j = q / r, t = 1, cn = 1, scale = j;
                var c = new double[30];
                var d = new double[30];
                int n2 = 0;
                bool converged = false;
                for (int n = 1; n <= 30; n++)
                {
                    double bp2n = b + n2;
                    j = (bp2n * (bp2n + 1) * j + (z + bp2n + 1) * t) * v;
                    n2 += 2;
                    t *= t2;
                    cn /= n2 * (n2 + 1.0);
                    c[n - 1] = cn;
                    double s = 0, coef = b - n;
                    for (int i = 1; i < n; i++) { s += coef * c[i - 1] * d[n - i - 1]; coef += b; }
                    d[n - 1] = bm1 * cn + s / n;
                    double dj = d[n - 1] * j;
                    sum += dj;
                    if (double.IsNaN(sum) || scale + sum <= 0) return false;
                    if (Math.Abs(dj) <= 1e-17 * (scale + sum)) { converged = true; break; }
                }
                if (!converged) return false;
            }
            ix = rr * (q + r * sum);
            ixc = p - rm1 * q - rr * r * sum;
            return ix >= 0 && ixc >= 0;
        }

        // Didonato & Morris' asymptotic expansion (TOMS 708, BASYM) for I_x(a, b) with a and b both large and x at or
        // below the mode, λ = a − (a+b)x in [0, 0.03·min(a, b)]. The continued fraction's rounding error grows with the
        // parameters there (2.6e-13 at I_½(1e6, 1e6), worse beyond); this is exact at the mode and otherwise within the
        // conditioning of x (measured to 1e9 against 40-digit quadrature).
        private static double Basym(double a, double b, double lambda)
        {
            const double e0 = 1.1283791670955126, e1 = 0.35355339059327376; // 2/√π, 2^(−3/2)
            const int num = 20;
            var a0 = new double[num + 1];
            var b0 = new double[num + 1];
            var c = new double[num + 1];
            var d = new double[num + 1];
            double h, r0, r1, w0;
            if (a < b) { h = a / b; r0 = 1 / (h + 1); r1 = (b - a) / b; w0 = 1 / Math.Sqrt(a * (h + 1)); }
            else { h = b / a; r0 = 1 / (h + 1); r1 = (b - a) / a; w0 = 1 / Math.Sqrt(b * (h + 1)); }
            double f = -a * Log1pmx(-lambda / a) - b * Log1pmx(lambda / b);
            double t = Math.Exp(-f);
            if (t == 0) return 0;
            double z0 = Math.Sqrt(f), z = 0.5 * (z0 / e1), z2 = f + f;
            a0[0] = 2.0 / 3 * r1;
            c[0] = -0.5 * a0[0];
            d[0] = -c[0];
            double j0 = 0.5 / e0 * Erfcx(z0), j1 = e1;
            double sum = j0 + d[0] * w0 * j1;
            double s = 1, h2 = h * h, hn = 1, w = w0, znm1 = z, zn = z2;
            for (int n = 2; n <= num; n += 2)
            {
                hn *= h2;
                a0[n - 1] = 2 * r0 * (1 + h * hn) / (n + 2);
                int np1 = n + 1;
                s += hn;
                a0[np1 - 1] = 2 * r1 * s / (n + 3);
                for (int i = n; i <= np1; i++)
                {
                    double r = -0.5 * (i + 1);
                    b0[0] = r * a0[0];
                    for (int m = 2; m <= i; m++)
                    {
                        double bsum = 0;
                        for (int j = 1; j < m; j++) bsum += (j * r - (m - j)) * a0[j - 1] * b0[m - j - 1];
                        b0[m - 1] = r * a0[m - 1] + bsum / m;
                    }
                    c[i - 1] = b0[i - 1] / (i + 1);
                    double dsum = 0;
                    for (int j = 1; j < i; j++) dsum += d[i - j - 1] * c[j - 1];
                    d[i - 1] = -(dsum + c[i - 1]);
                }
                j0 = e1 * znm1 + (n - 1) * j0;
                j1 = e1 * zn + n * j1;
                znm1 *= z2;
                zn *= z2;
                w *= w0;
                double t0 = d[n - 1] * w * j0;
                w *= w0;
                double t1 = d[np1 - 1] * w * j1;
                sum += t0 + t1;
                if (Math.Abs(t0) + Math.Abs(t1) <= Eps * sum) break;
            }
            double u = Math.Exp(-(StirlingCorr(a) + StirlingCorr(b) - StirlingCorr(a + b)));
            return e0 * t * u * sum;
        }

        // Continued fraction for I_x(a, b) (modified Lentz).
        private static double BetaCf(double x, double oneMinusX, double a, double b)
        {
            double qab = a + b, qap = a + 1, qam = a - 1;
            // 1 − (a+b)x/(a+1) = ((a+1) − (a+b)x)/(a+1) = (1 − b + (a+b)(1 − x))/(a+1). Each form subtracts numbers as large
            // as its biggest term, so take the one whose terms are smaller: the second for x near 1 with a large, the first
            // for x small with b large (F's lower tail at a big denominator df, where the second lost 1e-8).
            double c = 1;
            double d = Math.Max(qap, qab * x) <= Math.Max(Math.Abs(1 - b), qab * oneMinusX)
                ? (qap - qab * x) / qap
                : (1 - b + qab * oneMinusX) / qap;
            if (Math.Abs(d) < Tiny) d = Tiny;
            d = 1 / d;
            double h = d;
            for (int m = 1; m <= 100000; m++)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1 / d;
                h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1 + aa * d; if (Math.Abs(d) < Tiny) d = Tiny;
                c = 1 + aa / c; if (Math.Abs(c) < Tiny) c = Tiny;
                d = 1 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < Eps) break;
            }
            return h;
        }

        // log(1 + x) accurate to a few ulps everywhere (Goldberg): corrects for the rounding of 1 + x.
        private static double Log1p(double x)
        {
            if (double.IsPositiveInfinity(x)) return x;          // ln(∞)·∞/∞ would be NaN
            double u = 1 + x;
            return u == 1 ? x : Math.Log(u) * x / (u - 1);
        }

        // e^x − 1 accurate to a few ulps (Goldberg / Kahan).
        private static double Expm1(double x)
        {
            double u = Math.Exp(x);
            if (u == 1) return x;
            double um1 = u - 1;
            return um1 == -1 ? -1 : um1 * x / Math.Log(u);
        }

        // log(1 + t) − t without cancellation for small t (the "bd0" trick behind accurate gamma tails).
        private static double Log1pmx(double t)
        {
            if (Math.Abs(t) >= 0.5) return Log1p(t) - t;
            double term = t, sum = 0;
            for (int n = 2; n < 200; n++)
            {
                term *= -t;
                double add = term / n;
                sum += add;
                if (Math.Abs(add) <= 1e-17 * Math.Abs(sum)) break;
            }
            return sum;
        }

        // ln(y) given y and 1 − y (both supplied accurately): the log of a value near 1 goes through log1p.
        private static double LnOf(double y, double oneMinusY) => y < 0.5 ? Math.Log(y) : Log1p(-oneMinusY);

        // Stirling series correction: lnΓ(x) = (x − ½)ln x − x + ½ln 2π + StirlingCorr(x), for x ≥ 10.
        private static double StirlingCorr(double x)
        {
            double r = 1 / x, r2 = r * r;
            return r * (1.0 / 12 + r2 * (-1.0 / 360 + r2 * (1.0 / 1260 + r2 * (-1.0 / 1680 + r2 * (1.0 / 1188
                 + r2 * (-691.0 / 360360 + r2 * (1.0 / 156 + r2 * (-3617.0 / 122400))))))));
        }

        // ln B(a, b) without the catastrophic lnΓ(a+b) − lnΓ(a) − lnΓ(b) cancellation for large arguments
        // (the same decomposition R's lbeta uses).
        private static double LnBeta(double a, double b)
        {
            double p = Math.Min(a, b), q = Math.Max(a, b);
            if (p >= 10)
            {
                double corr = StirlingCorr(p) + StirlingCorr(q) - StirlingCorr(p + q);
                return -0.5 * Math.Log(q) + LnSqrt2Pi + corr + (p - 0.5) * Math.Log(p / (p + q)) + q * Log1p(-p / (p + q));
            }
            if (q >= 10)
            {
                double corr = StirlingCorr(q) - StirlingCorr(p + q);
                return LnGamma(p) + corr + p - p * Math.Log(p + q) + (q - 0.5) * Log1p(-p / (p + q));
            }
            return LnGamma(p) + LnGamma(q) - LnGamma(p + q);
        }

        // ln( x^a e^(−x) / Γ(a) ), stable for large a where a·ln x, x and lnΓ(a) are huge and nearly cancel.
        private static double LnPrefix(double a, double x)
        {
            if (a < 10) return a * Math.Log(x) - x - LnGamma(a);
            // a·ln x − x − lnΓ(a) = a·(ln r − (r − 1)) + ½ln a − ½ln 2π − corr(a), r = x/a. Near r = 1 use the
            // cancellation-free log1p(t) − t series; far from it take ln r directly (1 + t would round away digits).
            double r = x / a;
            double core = r < 0.5 || r > 2 ? Math.Log(r) - (r - 1) : Log1pmx((x - a) / a);
            return a * core + 0.5 * Math.Log(a) - LnSqrt2Pi - StirlingCorr(a);
        }

        // ---- validation ----

        private static void RequirePositive(double value, string name)
        {
            if (!(value > 0) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, value, "Must be positive and finite.");
        }

        private static double RequireDf(double df, string name)
        {
            if (!(df > 0) || double.IsInfinity(df)) throw new ArgumentOutOfRangeException(name, df, "Degrees of freedom must be positive and finite.");
            return df;
        }

        private static void RequireOpenProbability(double p)
        {
            if (!(p > 0 && p < 1)) throw new ArgumentOutOfRangeException(nameof(p), p, "Probability must be in (0, 1).");
        }

        private static void RequireOrdered(double a, double b)
        {
            if (double.IsNaN(a) || double.IsInfinity(a)) throw new ArgumentOutOfRangeException(nameof(a), a, "Bounds must be finite.");
            if (double.IsNaN(b) || double.IsInfinity(b)) throw new ArgumentOutOfRangeException(nameof(b), b, "Bounds must be finite.");
            if (a >= b) throw new ArgumentException($"Lower bound {a} must be less than upper bound {b}.", nameof(b));
        }
    }
}
