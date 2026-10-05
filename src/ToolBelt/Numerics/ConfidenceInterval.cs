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
            if (confidence <= 0 || confidence >= 1) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");

            int n = sample.Count;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += sample[i];
            mean /= n;

            double sumSq = 0;
            for (int i = 0; i < n; i++) { double d = sample[i] - mean; sumSq += d * d; }
            double stdErr = Math.Sqrt(sumSq / (n - 1)) / Math.Sqrt(n);

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
            var (n, mean, variance) = Moments(sample, confidence);
            double t = Distributions.StudentTQuantile((1 + confidence) / 2, n - 1);
            double margin = t * Math.Sqrt(variance / n);
            return (mean - margin, mean + margin);
        }

        /// <summary>
        /// Chi-square confidence interval for the population variance: [(n−1)s²/χ²(1−α/2), (n−1)s²/χ²(α/2)]. Assumes
        /// normally distributed data and, unlike the mean intervals, is not robust to departures from normality.
        /// </summary>
        public static (double Lower, double Upper) ForVariance(IReadOnlyList<double> sample, double confidence = 0.95)
        {
            var (n, _, variance) = Moments(sample, confidence);
            double ss = (n - 1) * variance;
            return (ss / Distributions.ChiSquareQuantile((1 + confidence) / 2, n - 1),
                    ss / Distributions.ChiSquareQuantile((1 - confidence) / 2, n - 1));
        }

        /// <summary>Confidence interval for the population standard deviation (square roots of <see cref="ForVariance"/>).</summary>
        public static (double Lower, double Upper) ForStandardDeviation(IReadOnlyList<double> sample, double confidence = 0.95)
        {
            var (lo, hi) = ForVariance(sample, confidence);
            return (Math.Sqrt(lo), Math.Sqrt(hi));
        }

        private static (int N, double Mean, double Variance) Moments(IReadOnlyList<double> sample, double confidence)
        {
            if (sample is null) throw new ArgumentNullException(nameof(sample));
            if (sample.Count < 2) throw new ArgumentException("At least two samples are required.", nameof(sample));
            if (confidence <= 0 || confidence >= 1) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");
            int n = sample.Count;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += sample[i];
            mean /= n;
            double sumSq = 0;
            for (int i = 0; i < n; i++) { double d = sample[i] - mean; sumSq += d * d; }
            return (n, mean, sumSq / (n - 1));
        }

        /// <summary>Wilson score confidence interval for a binomial proportion.</summary>
        public static (double Lower, double Upper) ForProportion(int successes, int trials, double confidence = 0.95)
        {
            if (trials < 1) throw new ArgumentOutOfRangeException(nameof(trials), trials, "Trials must be positive.");
            if (successes < 0 || successes > trials) throw new ArgumentOutOfRangeException(nameof(successes), successes, "Successes must be in [0, trials].");
            if (confidence <= 0 || confidence >= 1) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");

            double p = (double)successes / trials;
            double z = Distributions.NormalQuantile((1 + confidence) / 2);
            double z2 = z * z;
            double denom = 1 + z2 / trials;
            double center = (p + z2 / (2 * trials)) / denom;
            double half = z * Math.Sqrt(p * (1 - p) / trials + z2 / (4.0 * trials * trials)) / denom;
            return (center - half, center + half);
        }
    }
}
