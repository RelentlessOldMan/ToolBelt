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
        /// window spanning ±<paramref name="halfWindow"/> samples around it. NaN samples (dropouts) are left out of the
        /// window statistics and replaced by the median too; a window with no other values leaves them NaN.
        /// </summary>
        public static double[] Filter(double[] samples, int halfWindow, double nSigmas = 3.0)
            => Run(samples, halfWindow, nSigmas, out _);

        /// <summary>The number of samples the filter would replace, without modifying the input.</summary>
        public static int CountOutliers(double[] samples, int halfWindow, double nSigmas = 3.0)
        {
            Run(samples, halfWindow, nSigmas, out int replaced);
            return replaced;
        }

        private static double[] Run(double[] samples, int halfWindow, double nSigmas, out int replaced)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (halfWindow < 1) throw new ArgumentOutOfRangeException(nameof(halfWindow), halfWindow, "Half-window must be at least 1.");
            if (!(nSigmas >= 0)) throw new ArgumentOutOfRangeException(nameof(nSigmas), nSigmas, "nSigmas must be non-negative.");

            int n = samples.Length;
            var result = (double[])samples.Clone();
            var window = new double[(int)Math.Min(n, 2L * halfWindow + 1)];   // a window never holds more than the record
            replaced = 0;

            for (int i = 0; i < n; i++)
            {
                int lo = (int)Math.Max(0, (long)i - halfWindow);
                int hi = (int)Math.Min(n - 1, (long)i + halfWindow);
                int count = 0;
                for (int j = lo; j <= hi; j++) if (!double.IsNaN(samples[j])) window[count++] = samples[j];
                if (count == 0) continue;

                double median = Median(window, count);
                // MAD = median of |x - median| over the window.
                for (int j = 0; j < count; j++) window[j] = Math.Abs(window[j] - median);
                double mad = Median(window, count);
                double sigma = MadToSigma * mad;

                // When the window is essentially constant, sigma collapses to 0 and the threshold with
                // it — so any point that differs from the median is (correctly) flagged as an outlier.
                if (double.IsNaN(samples[i]) || Math.Abs(samples[i] - median) > nSigmas * sigma)
                {
                    result[i] = median;
                    replaced++;
                }
            }
            return result;
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
