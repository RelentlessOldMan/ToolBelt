// ToolBelt drop-in — also copy Numerics/Distributions.cs (CDFs) and Numerics/HypothesisTests.cs (Kolmogorov–Smirnov).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ToolBelt.Numerics
{
    /// <summary>A family that <see cref="DistributionFit"/> can fit.</summary>
    public enum DistributionFamily
    {
        Normal,
        LogNormal,
        Exponential,
        Gamma,
        Weibull,
    }

    /// <summary>A fitted distribution: maximum-likelihood parameters, information criteria and a goodness-of-fit check.</summary>
    public sealed class DistributionFitResult
    {
        private readonly Func<double, double> _cdf;

        internal DistributionFitResult(DistributionFamily family, IReadOnlyDictionary<string, double> parameters, double logLikelihood,
            int n, Func<double, double> cdf, TestResult ks)
        {
            Family = family;
            Parameters = parameters;
            LogLikelihood = logLikelihood;
            int k = parameters.Count;
            Aic = 2 * k - 2 * logLikelihood;
            Bic = k * Math.Log(n) - 2 * logLikelihood;
            _cdf = cdf;
            KolmogorovSmirnovStatistic = ks.Statistic;
            KolmogorovSmirnovPValue = ks.PValue;
        }

        public DistributionFamily Family { get; }

        /// <summary>
        /// Named parameters: Normal (mean, sd), LogNormal (mu, sigma of ln x), Exponential (rate), Gamma (shape, scale),
        /// Weibull (shape, scale).
        /// </summary>
        public IReadOnlyDictionary<string, double> Parameters { get; }

        public double LogLikelihood { get; }

        /// <summary>Akaike information criterion (lower is better when comparing fits to the same data).</summary>
        public double Aic { get; }

        /// <summary>Bayesian information criterion.</summary>
        public double Bic { get; }

        /// <summary>The Kolmogorov–Smirnov distance between the sample and the fitted CDF.</summary>
        public double KolmogorovSmirnovStatistic { get; }

        /// <summary>
        /// The KS p-value. The parameters were estimated from the same data, which makes this p-value conservative (too
        /// large — the Lilliefors effect): a small value is strong evidence against the family, a large one is weak evidence for it.
        /// </summary>
        public double KolmogorovSmirnovPValue { get; }

        /// <summary>The fitted cumulative distribution function.</summary>
        public double Cdf(double x) => _cdf(x);

        /// <summary>
        /// "Is the data plausibly from this family?" — true unless the KS test rejects at <paramref name="alpha"/>. Use it to
        /// sanity-check an assumption (e.g. normality before a capability calculation), not to prove one.
        /// </summary>
        public bool IsPlausible(double alpha = 0.05)
        {
            if (!(alpha > 0 && alpha < 1)) throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "Alpha must be in (0, 1).");
            return KolmogorovSmirnovPValue >= alpha;
        }

        public override string ToString() => Family + "(" + string.Join(", ",
            Parameters.Select(p => p.Key + "=" + p.Value.ToString("G6", CultureInfo.InvariantCulture))) + ")";
    }

    /// <summary>
    /// Maximum-likelihood fitting of common continuous distributions, each with log-likelihood, AIC/BIC and a
    /// Kolmogorov–Smirnov goodness-of-fit check — the answer to "is this normal enough?" and "what does this data look
    /// like?". Normal, log-normal and exponential have closed-form estimates; gamma (shape from ln k − ψ(k) =
    /// ln x̄ − mean(ln x)) and Weibull (shape from the profile score equation) are solved by safeguarded Newton iteration.
    /// The normal "sd" is the MLE (divides by n), not the n − 1 sample deviation. Positive-support families need all data
    /// &gt; 0. <see cref="FitAll"/> fits every applicable family and orders them by AIC.
    /// </summary>
    public static class DistributionFit
    {
        public static DistributionFitResult FitNormal(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: false, min: 2);
            int n = x.Length;
            var (mean, sd) = MeanSd(x);
            if (sd == 0) throw new ArgumentException("All values are equal; the spread cannot be estimated.", nameof(sample));
            double ll = -n / 2.0 * (Math.Log(2 * Math.PI) + 2 * Math.Log(sd) + 1);
            Func<double, double> cdf = v => Distributions.NormalCdf(v, mean, sd);
            return Make(DistributionFamily.Normal, P("mean", mean, "sd", sd), ll, x, cdf);
        }

        public static DistributionFitResult FitLogNormal(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: true, min: 2);
            int n = x.Length;
            double[] lx = x.Select(v => Math.Log(v)).ToArray();
            double mu = lx.Average();
            double ss = 0;
            foreach (double v in lx) ss += (v - mu) * (v - mu);
            double sigma = Math.Sqrt(ss / n);
            if (sigma == 0) throw new ArgumentException("All values are equal; the spread cannot be estimated.", nameof(sample));
            double ll = -n / 2.0 * (Math.Log(2 * Math.PI) + 2 * Math.Log(sigma) + 1) - lx.Sum();
            Func<double, double> cdf = v => Distributions.LogNormalCdf(v, mu, sigma);
            return Make(DistributionFamily.LogNormal, P("mu", mu, "sigma", sigma), ll, x, cdf);
        }

        public static DistributionFitResult FitExponential(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: true, min: 1);
            double rate = 1 / MeanSd(x).Mean;
            double ll = x.Length * (Math.Log(rate) - 1);       // Σ rate·x = n at the MLE
            Func<double, double> cdf = v => Distributions.ExponentialCdf(v, rate);
            return Make(DistributionFamily.Exponential, P("rate", rate), ll, x, cdf);
        }

        public static DistributionFitResult FitGamma(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: true, min: 2);
            int n = x.Length;
            double mean = MeanSd(x).Mean;
            // s = ln x̄ − mean(ln x) = mean(dᵢ − ln(1 + dᵢ)) − mean(dᵢ) with dᵢ = xᵢ/x̄ − 1: no cancellation between two O(1)
            // logs, so tight data (s ≈ CV²/2, below 1e-14 at CV 1.4e-7) keeps its shape.
            double s = 0, meanD = 0;
            foreach (double v in x)
            {
                double d = (v - mean) / mean;
                // Far from the mean the logs no longer cancel, and 1 + d lost a value far below it (1e-20 against a mean
                // near 1 rounded to ln 1 = 0: s came out 0 and the fit refused the sample as constant).
                s -= Math.Abs(d) > 0.5 ? Math.Log(v) - Math.Log(mean) - d : Log1pmx(d);
                meanD += d;
            }
            s = (s - meanD) / n;                                  // >= 0 by Jensen; 0 only when all values are equal
            if (!(s > 0)) throw new ArgumentException("All values are equal; the shape cannot be estimated.", nameof(sample));
            double k = (3 - s + Math.Sqrt((s - 3) * (s - 3) + 24 * s)) / (12 * s); // Minka's starting point
            for (int i = 0; i < 100; i++)
            {
                double f = LogMinusDigamma(k) - s, df = InvMinusTrigamma(k);
                double next = k - f / df;
                if (!(next > 0)) next = k / 2;
                bool done = Math.Abs(next - k) <= 1e-14 * k;
                k = next;
                if (done) break;
            }
            double theta = mean / k;
            double ll = (k - 1) * x.Select(v => Math.Log(v)).Sum() - n * k - n * (k * Math.Log(theta) + LnGamma(k)); // Σx/θ = nk
            double shape = k;
            Func<double, double> cdf = v => v <= 0 ? 0 : Distributions.ChiSquareCdf(2 * v / theta, 2 * shape); // Gamma(k, θ) ⇔ χ²(2k) of 2x/θ
            return Make(DistributionFamily.Gamma, P("shape", k, "scale", theta), ll, x, cdf);
        }

        public static DistributionFitResult FitWeibull(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: true, min: 2);
            int n = x.Length;
            double max = x.Max();
            // Scale-free: the shape equation is invariant to scaling. As a difference of logs, since v/max underflowed to 0
            // (ln 0 = −∞) when the sample spanned more than the double range.
            double[] ly = x.Select(v => Math.Log(v) - Math.Log(max)).ToArray();
            if (ly.Max() - ly.Min() < 1e-14) throw new ArgumentException("All values are equal; the shape cannot be estimated.", nameof(sample));
            double meanLy = ly.Average();

            // g(k) = Σ y^k ln y / Σ y^k − 1/k − mean(ln y), increasing in k.
            double G(double k, out double dg)
            {
                double s0 = 0, s1 = 0, s2 = 0;
                foreach (double l in ly)
                {
                    double w = Math.Exp(k * l);
                    s0 += w; s1 += w * l; s2 += w * l * l;
                }
                double m = s1 / s0;
                dg = s2 / s0 - m * m + 1 / (k * k);
                return m - 1 / k - meanLy;
            }
            // Bracket the root of the shape equation; very tight data (CV ≲ 0.1%) has shape far above 1000.
            double lo = 1e-3, hi = 1e3;
            while (G(hi, out _) < 0 && hi < 1e12) { lo = hi; hi *= 10; }
            while (G(lo, out _) > 0 && lo > 1e-12) { hi = lo; lo /= 10; }
            double kk = 1.2 / Math.Sqrt(Math.Max(1e-12, ly.Select(l => (l - meanLy) * (l - meanLy)).Average())); // ≈ π/(√6·sd(ln x))
            kk = Math.Min(Math.Max(kk, lo * 2), hi / 2);
            for (int i = 0; i < 200; i++)
            {
                double g = G(kk, out double dg);
                if (g < 0) lo = kk; else hi = kk;
                double next = kk - g / dg;
                if (!(next > lo && next < hi)) next = Math.Sqrt(lo * hi);
                bool done = Math.Abs(next - kk) <= 1e-14 * kk;
                kk = next;
                if (done || hi / lo < 1 + 1e-15) break;
            }
            double sumYk = ly.Sum(l => Math.Exp(kk * l));
            double lambda = max * Math.Pow(sumYk / n, 1 / kk);
            double shapeK = kk;
            double sumLogX = x.Select(v => Math.Log(v)).Sum();
            double ll = n * Math.Log(kk) - n * kk * Math.Log(lambda) + (kk - 1) * sumLogX - x.Sum(v => Math.Pow(v / lambda, shapeK));
            Func<double, double> cdf = v => v <= 0 ? 0 : -Expm1(-Math.Pow(v / lambda, shapeK)); // 1 − e^(−t), accurate for small t
            return Make(DistributionFamily.Weibull, P("shape", kk, "scale", lambda), ll, x, cdf);
        }

        /// <summary>Fits every family the data allows (positive-support ones only for positive data), best AIC first.</summary>
        public static IReadOnlyList<DistributionFitResult> FitAll(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: false, min: 2);
            var fits = new List<DistributionFitResult> { FitNormal(x) };
            if (x.All(v => v > 0))
            {
                // Data whose spread the normal fit resolves can still be too tight for a shape estimate (values a few ulps
                // apart): skip that family rather than fail the whole comparison.
                foreach (var fit in new Func<double[], DistributionFitResult>[] { FitLogNormal, FitExponential, FitGamma, FitWeibull })
                {
                    try { fits.Add(fit(x)); }
                    catch (ArgumentException) { }
                }
            }
            return fits.OrderBy(f => f.Aic).ToList();
        }

        // ---------- helpers ----------

        private static DistributionFitResult Make(DistributionFamily family, IReadOnlyDictionary<string, double> p, double ll, double[] x, Func<double, double> cdf)
            => new DistributionFitResult(family, p, ll, x.Length, cdf, HypothesisTests.KolmogorovSmirnovOneSample(x, cdf));

        private static IReadOnlyDictionary<string, double> P(string a, double av, string? b = null, double bv = 0)
        {
            var d = new Dictionary<string, double> { [a] = av };
            if (b != null) d[b] = bv;
            return d;
        }

        private static double[] Check(IReadOnlyList<double> sample, bool positive, int min)
        {
            if (sample is null) throw new ArgumentNullException(nameof(sample));
            if (sample.Count < min) throw new ArgumentException($"Need at least {min} values.", nameof(sample));
            var x = new double[sample.Count];
            for (int i = 0; i < x.Length; i++)
            {
                double v = sample[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Values must be finite.", nameof(sample));
                if (positive && !(v > 0)) throw new ArgumentException("This family needs strictly positive data.", nameof(sample));
                x[i] = v;
            }
            return x;
        }

        // Mean and MLE standard deviation, scaled by the largest magnitude so sums and squares of values near 1e308 do not
        // overflow (nor squares near 1e-170 underflow).
        private static (double Mean, double Sd) MeanSd(double[] x)
        {
            double scale = 0;
            foreach (double v in x) scale = Math.Max(scale, Math.Abs(v));
            if (scale == 0) return (0, 0);
            double mean = 0;
            foreach (double v in x) mean += v / scale;
            mean /= x.Length;
            double ss = 0;
            foreach (double v in x) { double d = v / scale - mean; ss += d * d; }
            return (mean * scale, Math.Sqrt(ss / x.Length) * scale);
        }

        // ln(1 + d) − d, accurate for small d.
        private static double Log1pmx(double d)
        {
            if (Math.Abs(d) > 0.5) return Math.Log(1 + d) - d;
            double term = d, sum = 0;
            for (int j = 2; j < 200; j++)
            {
                term *= -d;
                double t = term / j;
                sum += t;
                if (Math.Abs(t) <= 1e-17 * Math.Abs(sum)) break;
            }
            return sum;
        }

        // ln k − ψ(k), directly from the asymptotic series for large k (forming the difference of two O(ln k) values
        // leaves no digits when the answer is 1/(2k) ≈ 1e-14).
        private static double LogMinusDigamma(double k)
        {
            if (k < 6) return Math.Log(k) - Digamma(k);
            double f = 1 / (k * k);
            return 0.5 / k + f * (1.0 / 12 - f * (1.0 / 120 - f * (1.0 / 252 - f * (1.0 / 240 - f * (1.0 / 132)))));
        }

        // 1/k − ψ′(k), likewise.
        private static double InvMinusTrigamma(double k)
        {
            if (k < 6) return 1 / k - Trigamma(k);
            double f = 1 / (k * k);
            return -(f / 2 + f / k * (1.0 / 6 - f * (1.0 / 30 - f * (1.0 / 42 - f * (1.0 / 30)))));
        }

        // e^x − 1 without cancellation for small x (Goldberg).
        private static double Expm1(double x)
        {
            double u = Math.Exp(x);
            if (u == 1) return x;
            double um1 = u - 1;
            return um1 == -1 ? -1 : um1 * x / Math.Log(u);
        }

        private static double Digamma(double x)
        {
            double r = 0;
            while (x < 6) { r -= 1 / x; x += 1; }
            double f = 1 / (x * x);
            return r + Math.Log(x) - 0.5 / x - f * (1.0 / 12 - f * (1.0 / 120 - f * (1.0 / 252 - f * (1.0 / 240 - f * (1.0 / 132)))));
        }

        private static double Trigamma(double x)
        {
            double r = 0;
            while (x < 6) { r += 1 / (x * x); x += 1; }
            double f = 1 / (x * x);
            return r + 1 / x + f / 2 + f / x * (1.0 / 6 - f * (1.0 / 30 - f * (1.0 / 42 - f * (1.0 / 30))));
        }

        // The same code as Distributions.cs (this copy had drifted: no tiny-x branch, no series near 1 and 2).
        private const double LnSqrt2Pi = 0.91893853320467274178;

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
    }

}
