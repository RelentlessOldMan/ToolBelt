// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>The outcome of a bootstrap resampling: the interval and the full resampled distribution.</summary>
    public sealed class BootstrapResult
    {
        internal BootstrapResult(double lower, double upper, double[] distribution)
        {
            Lower = lower;
            Upper = upper;
            Distribution = distribution;
        }

        public double Lower { get; }
        public double Upper { get; }
        /// <summary>The statistic evaluated on each resample, sorted ascending.</summary>
        public IReadOnlyList<double> Distribution { get; }
    }

    /// <summary>
    /// Bootstrap resampling: repeatedly resamples the data with replacement, evaluates a caller-supplied
    /// statistic on each resample, and returns the percentile confidence interval plus the resampled
    /// distribution. Works for any statistic (mean, median, ratio, ...) with no distributional assumptions,
    /// and makes other estimates verifiable by simulation. The random source is injected, so results are
    /// reproducible with a seeded generator.
    /// </summary>
    public static class Bootstrap
    {
        public static BootstrapResult PercentileInterval(
            IReadOnlyList<double> sample,
            Func<IReadOnlyList<double>, double> statistic,
            Random random,
            int resamples = 2000,
            double confidence = 0.95)
        {
            if (sample is null) throw new ArgumentNullException(nameof(sample));
            if (statistic is null) throw new ArgumentNullException(nameof(statistic));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (sample.Count < 1) throw new ArgumentException("At least one sample is required.", nameof(sample));
            if (resamples < 1) throw new ArgumentOutOfRangeException(nameof(resamples), resamples, "Resample count must be positive.");
            if (confidence <= 0 || confidence >= 1) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");

            int n = sample.Count;
            var estimates = new double[resamples];
            var resample = new double[n];
            for (int r = 0; r < resamples; r++)
            {
                for (int i = 0; i < n; i++) resample[i] = sample[random.Next(n)]; // draw with replacement
                estimates[r] = statistic(resample);
            }

            Array.Sort(estimates);
            double alpha = (1 - confidence) / 2;
            return new BootstrapResult(Quantile(estimates, alpha), Quantile(estimates, 1 - alpha), estimates);
        }

        private static double Quantile(double[] sorted, double p)
        {
            if (sorted.Length == 1) return sorted[0];
            double position = p * (sorted.Length - 1);
            int lo = (int)Math.Floor(position);
            int hi = (int)Math.Ceiling(position);
            if (lo == hi) return sorted[lo];
            double frac = position - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }
    }
}
