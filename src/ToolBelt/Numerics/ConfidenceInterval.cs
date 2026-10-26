// ToolBelt drop-in — also copy Numerics/Distributions.cs (quantiles).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Confidence intervals: for a mean, the Student-t interval (<see cref="ForMeanT"/> — exact for normal data and
    /// the right default, especially for small samples) or the large-sample z interval (<see cref="ForMean"/>, which
    /// under-covers when n is small: a nominal 95% z interval covers only about 88% at n = 5); chi-square intervals for
    /// a variance and standard deviation (normal data assumed, and sensitive to departures from it); and the Wilson
    /// score interval for a proportion, used instead of the naive normal approximation because that one is wrong at
    /// the extremes (a genuinely common error). For any other statistic use the bootstrap.
    /// </summary>
    public static class ConfidenceInterval
    {
        /// <summary>
        /// Normal-approximation (z) confidence interval for the population mean. Appropriate for large samples; for small
        /// ones prefer <see cref="ForMeanT"/>, which widens the interval to account for estimating the spread.
        /// </summary>
        public static (double Lower, double Upper) ForMean(IReadOnlyList<double> sample, double confidence = 0.95)
        {
            if (sample is null) throw new ArgumentNullException(nameof(sample));
            if (sample.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(sample));
            if (!(confidence > 0 && confidence < 1)) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");

            int n = sample.Count;
            for (int i = 0; i < n; i++)
                if (double.IsNaN(sample[i]) || double.IsInfinity(sample[i])) throw new ArgumentException($"Value [{i}] is not finite.", nameof(sample));
            var (mean, sd) = MeanSd(sample);
            double stdErr = sd / Math.Sqrt(n);

            double z = Distributions.NormalQuantile((1 + confidence) / 2);
            double margin = z * stdErr;
            return (mean - margin, mean + margin);
        }

        /// <summary>
        /// Student-t confidence interval for the population mean: mean ± t(1−α/2, n−1)·s/√n. Exact when the data are
        /// normal and robust to moderate departures; converges to <see cref="ForMean"/> as n grows.
        /// </summary>
        public static (double Lower, double Upper) ForMeanT(IReadOnlyList<double> sample, double confidence = 0.95)
        {
            var (n, mean, sd) = Moments(sample, confidence);
            double t = Distributions.StudentTQuantile((1 + confidence) / 2, n - 1);
            double margin = t * (sd / Math.Sqrt(n));
            return (mean - margin, mean + margin);
        }

        /// <summary>
        /// Chi-square confidence interval for the population variance: [(n−1)s²/χ²(1−α/2), (n−1)s²/χ²(α/2)]. Assumes
        /// normally distributed data and, unlike the mean intervals, is not robust to departures from normality.
        /// </summary>
        public static (double Lower, double Upper) ForVariance(IReadOnlyList<double> sample, double confidence = 0.95)
        {
            var (lo, hi) = ForStandardDeviation(sample, confidence);
            return (lo * lo, hi * hi);
        }

        /// <summary>Confidence interval for the population standard deviation (square roots of <see cref="ForVariance"/>).</summary>
        public static (double Lower, double Upper) ForStandardDeviation(IReadOnlyList<double> sample, double confidence = 0.95)
        {
            // From s itself: s² under- or overflowed for data near 1e±160 (an interval of (0, 0) or (∞, ∞)).
            var (n, _, sd) = Moments(sample, confidence);
            return (sd * Math.Sqrt((n - 1) / Distributions.ChiSquareQuantile((1 + confidence) / 2, n - 1)),
                    sd * Math.Sqrt((n - 1) / Distributions.ChiSquareQuantile((1 - confidence) / 2, n - 1)));
        }

        private static (int N, double Mean, double Sd) Moments(IReadOnlyList<double> sample, double confidence)
        {
            if (sample is null) throw new ArgumentNullException(nameof(sample));
            if (sample.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(sample));
            if (!(confidence > 0 && confidence < 1)) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");
            int n = sample.Count;
            for (int i = 0; i < n; i++)
                if (double.IsNaN(sample[i]) || double.IsInfinity(sample[i])) throw new ArgumentException($"Value [{i}] is not finite.", nameof(sample));
            var (mean, sd) = MeanSd(sample);
            return (n, mean, sd);
        }

        // Mean and sample standard deviation of finite data, in units of the largest magnitude p (a power of two, so
        // normal-range data give the same bits): the mean about the first value, so a constant sample has exactly that
        // mean and zero spread, and the deviations squared relative to the largest. Unscaled, the sums overflowed for
        // data near 1e308 and the squares under- or overflowed for data near 1e±160.
        private static (double Mean, double Sd) MeanSd(IReadOnlyList<double> x)
        {
            int n = x.Count;
            double big = 0;
            for (int i = 0; i < n; i++) big = Math.Max(big, Math.Abs(x[i]));
            double p = Pow2Scale(big), x0 = x[0] / p, shift = 0;
            for (int i = 0; i < n; i++) shift += x[i] / p - x0;
            double mean = x0 + shift / n;
            double max = 0;
            for (int i = 0; i < n; i++) max = Math.Max(max, Math.Abs(x[i] / p - mean));
            double s = Pow2Scale(max), ss = 0;
            for (int i = 0; i < n; i++) { double d = (x[i] / p - mean) / s; ss += d * d; }
            return (mean * p, Math.Sqrt(ss / (n - 1)) * s * p);
        }

        // The power of two at or below m (1 for 0): dividing by it is exact, so data in the normal range give the same
        // bits, while data near 1e±308 neither over- nor underflow when summed or squared.
        private static double Pow2Scale(double m)
        {
            if (m == 0 || double.IsInfinity(m)) return 1;
            double p = Math.Pow(2, Math.Floor(Math.Log(m) / Math.Log(2)));
            // The logarithm can land a hair either side of an integer; settle on exactly the power at or below m.
            while (p > m) p /= 2;
            while (p * 2 <= m) p *= 2;
            return p;
        }

        /// <summary>Wilson score confidence interval for a binomial proportion.</summary>
        public static (double Lower, double Upper) ForProportion(int successes, int trials, double confidence = 0.95)
        {
            if (trials < 1) throw new ArgumentOutOfRangeException(nameof(trials), trials, "Trials must be positive.");
            if (successes < 0 || successes > trials) throw new ArgumentOutOfRangeException(nameof(successes), successes, "Successes must be in [0, trials].");
            if (!(confidence > 0 && confidence < 1)) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");

            double p = (double)successes / trials;
            double z = Distributions.NormalQuantile((1 + confidence) / 2);
            double z2 = z * z;
            double denom = 1 + z2 / trials;
            // 2.0·trials: 2·trials overflowed int past ~1.07 billion trials. The Wilson bounds are exactly 0 at no successes
            // and exactly 1 at all successes, which center ∓ half missed by a rounding error (just below 0, just above 1).
            double center = (p + z2 / (2.0 * trials)) / denom;
            double half = z * Math.Sqrt(p * (1 - p) / trials + z2 / (4.0 * trials * trials)) / denom;
            double lower = successes == 0 ? 0 : Math.Max(0, center - half);
            double upper = successes == trials ? 1 : Math.Min(1, center + half);
            return (lower, upper);
        }
    }
}
