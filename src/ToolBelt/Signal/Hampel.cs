// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// The Hampel filter: a sliding-window outlier detector that replaces spikes exceeding a robust
    /// threshold (median ± k·MAD) with the local median. Far gentler on real edges than a plain median
    /// filter because it only touches points that genuinely look like outliers.
    /// </summary>
    public static class Hampel
    {
        // Scale factor making the MAD a consistent estimator of the standard deviation for Gaussian data.
        private const double MadToSigma = 1.4826;

        /// <summary>
        /// Returns a copy of <paramref name="samples"/> with outliers replaced. A point is an outlier when
        /// it lies more than <paramref name="nSigmas"/> robust standard deviations from the median of the
        /// window spanning ±<paramref name="halfWindow"/> samples around it.
        /// </summary>
        public static double[] Filter(double[] samples, int halfWindow, double nSigmas = 3.0)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (halfWindow < 1) throw new ArgumentOutOfRangeException(nameof(halfWindow), halfWindow, "Half-window must be at least 1.");
            if (nSigmas < 0) throw new ArgumentOutOfRangeException(nameof(nSigmas), nSigmas, "nSigmas must be non-negative.");

            int n = samples.Length;
            var result = (double[])samples.Clone();
            var window = new double[2 * halfWindow + 1];

            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - halfWindow);
                int hi = Math.Min(n - 1, i + halfWindow);
                int count = hi - lo + 1;
                for (int j = 0; j < count; j++) window[j] = samples[lo + j];

                double median = Median(window, count);
                // MAD = median of |x - median| over the window.
                for (int j = 0; j < count; j++) window[j] = Math.Abs(samples[lo + j] - median);
                double mad = Median(window, count);
                double sigma = MadToSigma * mad;

                // When the window is essentially constant, sigma collapses to 0 and the threshold with
                // it — so any point that differs from the median is (correctly) flagged as an outlier.
                if (Math.Abs(samples[i] - median) > nSigmas * sigma)
                    result[i] = median;
            }
            return result;
        }

        /// <summary>The number of samples the filter would replace, without modifying the input.</summary>
        public static int CountOutliers(double[] samples, int halfWindow, double nSigmas = 3.0)
        {
            var filtered = Filter(samples, halfWindow, nSigmas);
            int count = 0;
            for (int i = 0; i < samples.Length; i++)
                if (filtered[i] != samples[i]) count++;
            return count;
        }

        private static double Median(double[] buffer, int count)
        {
            var copy = new double[count];
            Array.Copy(buffer, copy, count);
            Array.Sort(copy);
            int mid = count / 2;
            return (count % 2 != 0) ? copy[mid] : 0.5 * (copy[mid - 1] + copy[mid]);
        }
    }
}
