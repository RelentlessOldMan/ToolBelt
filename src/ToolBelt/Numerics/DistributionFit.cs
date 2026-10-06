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
            double mean = x.Average();
            double ss = 0;
            foreach (double v in x) ss += (v - mean) * (v - mean);
            double sd = Math.Sqrt(ss / n);
            if (sd == 0) throw new ArgumentException("All values are equal; the spread cannot be estimated.", nameof(sample));
            double ll = -n / 2.0 * (Math.Log(2 * Math.PI * sd * sd) + 1);
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
            double ll = -n / 2.0 * (Math.Log(2 * Math.PI * sigma * sigma) + 1) - lx.Sum();
            Func<double, double> cdf = v => Distributions.LogNormalCdf(v, mu, sigma);
            return Make(DistributionFamily.LogNormal, P("mu", mu, "sigma", sigma), ll, x, cdf);
        }

        public static DistributionFitResult FitExponential(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: true, min: 1);
            double sum = x.Sum();
            double rate = x.Length / sum;
            double ll = x.Length * Math.Log(rate) - rate * sum;
            Func<double, double> cdf = v => Distributions.ExponentialCdf(v, rate);
            return Make(DistributionFamily.Exponential, P("rate", rate), ll, x, cdf);
        }

        public static DistributionFitResult FitGamma(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: true, min: 2);
            int n = x.Length;
            double mean = x.Average(), meanLog = x.Select(v => Math.Log(v)).Average();
            double s = Math.Log(mean) - meanLog;                 // >= 0 by Jensen; 0 only when all values are equal
            if (!(s > 1e-14)) throw new ArgumentException("All values are equal; the shape cannot be estimated.", nameof(sample));
            double k = (3 - s + Math.Sqrt((s - 3) * (s - 3) + 24 * s)) / (12 * s); // Minka's starting point
            for (int i = 0; i < 100; i++)
            {
                double f = Math.Log(k) - Digamma(k) - s, df = 1 / k - Trigamma(k);
                double next = k - f / df;
                if (!(next > 0)) next = k / 2;
                bool done = Math.Abs(next - k) <= 1e-14 * k;
                k = next;
                if (done) break;
            }
            double theta = mean / k;
            double ll = (k - 1) * x.Select(v => Math.Log(v)).Sum() - x.Sum() / theta - n * (k * Math.Log(theta) + LnGamma(k));
            double shape = k;
            Func<double, double> cdf = v => v <= 0 ? 0 : Distributions.ChiSquareCdf(2 * v / theta, 2 * shape); // Gamma(k, θ) ⇔ χ²(2k) of 2x/θ
            return Make(DistributionFamily.Gamma, P("shape", k, "scale", theta), ll, x, cdf);
        }

        public static DistributionFitResult FitWeibull(IReadOnlyList<double> sample)
        {
            double[] x = Check(sample, positive: true, min: 2);
            int n = x.Length;
            double max = x.Max();
            double[] ly = x.Select(v => Math.Log(v / max)).ToArray();  // scale-free: the shape equation is invariant to scaling
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
                fits.Add(FitLogNormal(x));
                fits.Add(FitExponential(x));
                fits.Add(FitGamma(x));
                fits.Add(FitWeibull(x));
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

        private static double LnGamma(double x)
        {
            if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LnGamma(1 - x);
            double[] g = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
                           12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            double a = g[0], t = x + 6.5, xm1 = x - 1;
            for (int i = 1; i < g.Length; i++) a += g[i] / (xm1 + i);
            return 0.91893853320467274178 + (xm1 + 0.5) * Math.Log(t) - t + Math.Log(a);
        }
    }

}
