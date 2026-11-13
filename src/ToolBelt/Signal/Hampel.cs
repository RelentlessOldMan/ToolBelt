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

            // The window is kept sorted as it slides (one binary-searched removal and insertion per sample), and the MAD is
            // read from it in one merge: copying and sorting it twice for every sample took 30 s for 20000 samples with a
            // half-window of 10000. NaN is never stored.
            int count = 0;
            double[]? scratch = null;
            for (int j = 0; j < n && j <= halfWindow; j++) Insert(window, ref count, samples[j]);
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    long leaving = (long)i - halfWindow - 1, entering = (long)i + halfWindow;
                    if (leaving >= 0) Remove(window, ref count, samples[(int)leaving]);
                    if (entering < n) Insert(window, ref count, samples[(int)entering]);
                }
                if (count == 0) continue;

                int mid = count / 2;
                double median = count % 2 != 0 ? window[mid] : Midpoint(window[mid - 1], window[mid]);
                double mad = Mad(window, count, median, ref scratch);
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

        // MAD = median of |x − median| over the sorted window. The deviations of the values below the median grow leftward
        // from the middle and those above it rightward, so merging the two runs outward reaches the middle rank in O(w).
        // An infinite median makes the deviations NaN, which don't order; they are sorted as before.
        private static double Mad(double[] sorted, int count, double median, ref double[]? scratch)
        {
            if (double.IsInfinity(median))
            {
                scratch ??= new double[sorted.Length];
                for (int j = 0; j < count; j++) scratch[j] = Math.Abs(sorted[j] - median);
                Array.Sort(scratch, 0, count);
                return count % 2 != 0 ? scratch[count / 2] : Midpoint(scratch[count / 2 - 1], scratch[count / 2]);
            }
            int left = count / 2 - 1, right = count / 2;
            double previous = 0, current = 0;
            for (int rank = 0; rank <= count / 2; rank++)
            {
                previous = current;
                bool takeLeft = left >= 0 && (right >= count || Math.Abs(sorted[left] - median) <= Math.Abs(sorted[right] - median));
                current = takeLeft ? Math.Abs(sorted[left--] - median) : Math.Abs(sorted[right++] - median);
            }
            return count % 2 != 0 ? current : Midpoint(previous, current);   // 0.5·(a + b) overflowed near double.MaxValue
        }

        private static void Insert(double[] window, ref int count, double x)
        {
            if (double.IsNaN(x)) return;
            int at = LowerBound(window, count, x);
            Array.Copy(window, at, window, at + 1, count - at);
            window[at] = x;
            count++;
        }

        private static void Remove(double[] window, ref int count, double x)
        {
            if (double.IsNaN(x)) return;
            int at = LowerBound(window, count, x);                         // x is there: it went in when it entered
            Array.Copy(window, at + 1, window, at, count - at - 1);
            count--;
        }

        // The first index whose value is not below x.
        private static int LowerBound(double[] window, int count, double x)
        {
            int lo = 0, hi = count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (window[mid] < x) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // The midpoint of two sorted values: halves first, so the sum can't overflow, except where both are tiny, where the
        // halves rounded on their own ([ε, ε] had the median 0, [3ε ×4] the median 4ε) and the sum is scaled by 2^54 instead.
        private static double Midpoint(double a, double b)
        {
            if (double.IsInfinity(a) || double.IsInfinity(b)) return 0.5 * (a + b);
            const double Up = 18014398509481984.0, Tiny = 9.332636185032189e-302;    // 2^54, 2^-1000
            if (Math.Abs(a) < Tiny && Math.Abs(b) < Tiny) return (a * Up + b * Up) * 0.5 / Up;
            return 0.5 * a + 0.5 * b;
        }
    }
}
