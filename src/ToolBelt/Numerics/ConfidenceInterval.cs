// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Confidence intervals using the normal (large-sample) approximation: a z-based interval for a mean and
    /// the Wilson score interval for a proportion. The Wilson interval is used instead of the naive normal
    /// approximation because the latter is wrong at the extremes (a genuinely common error). For small
    /// samples a Student-t interval would be wider; that is not provided here.
    /// </summary>
    public static class ConfidenceInterval
    {
        /// <summary>Normal-approximation confidence interval for the population mean.</summary>
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
