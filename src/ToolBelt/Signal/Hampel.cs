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
            // ∞ made the threshold ∞·0 = NaN in a constant window, so a point the documented rule flags was kept.
            if (!(nSigmas >= 0) || double.IsInfinity(nSigmas)) throw new ArgumentOutOfRangeException(nameof(nSigmas), nSigmas, "nSigmas must be non-negative and finite.");

            int n = samples.Length;
            var result = (double[])samples.Clone();
            // On the subnormal grid the medians and the threshold rounded (2.9652ε to 3ε), so [−2, −2, 0, 3, 0, 0, 2]·ε
            // missed the outlier it has at scale 1. Tiny data is worked on scaled by 2^600 (exact), as Fft does.
            double largest = 0;
            foreach (double v in samples) if (!double.IsNaN(v) && !double.IsInfinity(v)) largest = Math.Max(largest, Math.Abs(v));
            double up = largest > 0 && largest < 3.054936363499605e-151 ? Math.Pow(2, 600) : 1;   // 2^-500
            if (up != 1)
            {
                var scaled = new double[n];
                for (int k = 0; k < n; k++) scaled[k] = samples[k] * up;
                samples = scaled;
            }
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
                // When the window is essentially constant, sigma collapses to 0 and the threshold with
                // it — so any point that differs from the median is (correctly) flagged as an outlier.
                // A NaN whose median is NaN too (a window of −∞ and +∞) is left as it is, and not counted: CountOutliers
                // reported a replacement that changed nothing.
                if (double.IsNaN(samples[i]) ? !double.IsNaN(median) : Outlier(samples[i], median, mad, nSigmas))
                {
                    result[i] = up == 1 ? median : median / up;
                    replaced++;
                }
            }
            return result;
        }

        // |x − median| > nσ·1.4826·MAD. Near double.MaxValue the threshold overflowed to ∞ (nothing flagged) or, with nσ = 0,
        // to 0·∞ = NaN; the deviation could overflow too. Then both sides are compared in quarters, which is exact there.
        private static bool Outlier(double x, double median, double mad, double nSigmas)
        {
            double deviation = Math.Abs(x - median);
            if (nSigmas == 0) return deviation > 0;                              // a threshold of 0 even where MAD is ∞
            double threshold = nSigmas * (MadToSigma * mad);
            if (!double.IsInfinity(deviation) && !double.IsInfinity(threshold) && !double.IsNaN(threshold)) return deviation > threshold;
            if (double.IsInfinity(x) || double.IsInfinity(median) || double.IsInfinity(mad)) return deviation > threshold;
            return Math.Abs(x / 4 - median / 4) > nSigmas * (MadToSigma * (mad / 4));
        }

        // MAD = median of |x − median| over the sorted window. The deviations of the values below the median grow leftward
        // from the middle and those above it rightward, so the middle rank of the two runs is found by binary search: merging
        // them outward was O(w) a sample, 292 s for 400000 samples with a half-window of 100000.
        // An infinite median made the deviations of the values equal to it ∞ − ∞ = NaN, so the MAD of [∞, ∞, 5, ∞, ∞] was NaN
        // and the 5, infinitely far from the median, was never flagged. A value equal to the median deviates by 0.
        private static double Mad(double[] sorted, int count, double median, ref double[]? scratch)
        {
            if (double.IsInfinity(median))
            {
                scratch ??= new double[sorted.Length];
                for (int j = 0; j < count; j++) scratch[j] = sorted[j] == median ? 0 : Math.Abs(sorted[j] - median);
                Array.Sort(scratch, 0, count);
                return count % 2 != 0 ? scratch[count / 2] : Midpoint(scratch[count / 2 - 1], scratch[count / 2]);
            }
            double current = KthDeviation(sorted, count, median, count / 2);
            if (count % 2 != 0) return current;
            return Midpoint(KthDeviation(sorted, count, median, count / 2 - 1), current);   // 0.5·(a + b) overflowed near double.MaxValue
        }

        // The k-th smallest (from 0) of the deviations: the left run is |sorted[left − i] − median| for i = 0..left and the
        // right run |sorted[right + j] − median|, both ascending. Taking i from the left and k + 1 − i from the right, the
        // smallest i whose next left deviation is not below the last right one taken splits the k + 1 smallest.
        private static double KthDeviation(double[] sorted, int count, double median, int k)
        {
            int left = count / 2 - 1, right = count / 2;
            int a = left + 1, b = count - right;
            int lo = Math.Max(0, k + 1 - b), hi = Math.Min(k + 1, a);
            while (lo < hi)
            {
                int i = lo + (hi - lo) / 2, j = k + 1 - i;
                if (Math.Abs(sorted[left - i] - median) < Math.Abs(sorted[right + j - 1] - median)) lo = i + 1; else hi = i;
            }
            int fromLeft = lo, fromRight = k + 1 - lo;
            double last = double.NegativeInfinity;
            if (fromLeft > 0) last = Math.Abs(sorted[left - (fromLeft - 1)] - median);
            if (fromRight > 0) last = Math.Max(last, Math.Abs(sorted[right + fromRight - 1] - median));
            return last;
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

        // The first index whose value is not below x, with −0 ordered before +0: equal under <, a leaving −0 could remove
        // a +0 that was still in the window (or the reverse), and a later median had the wrong sign.
        private static int LowerBound(double[] window, int count, double x)
        {
            int lo = 0, hi = count;
            bool negativeZero = x == 0 && BitConverter.DoubleToInt64Bits(x) < 0;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                double w = window[mid];
                if (w < x || (w == 0 && x == 0 && !negativeZero && BitConverter.DoubleToInt64Bits(w) < 0)) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // The midpoint of two sorted values: halves first, so the sum can't overflow, except where both are tiny, where the
        // halves rounded on their own ([ε, ε] had the median 0, [3ε ×4] the median 4ε) and the sum is scaled by 2^54 instead.
        // Scaled wherever the scaled values fit, not only where both are tiny: a subnormal beside 2^-1000 lost its half's
        // rounding bit ((2^21 + 1)ε and 2^-1000 gave a midpoint one ulp low). Scaled, the sum rounds once, and when the
        // midpoint is subnormal the sum is exact, so only the scale-back rounds.
        private static double Midpoint(double a, double b)
        {
            if (double.IsInfinity(a) || double.IsInfinity(b)) return 0.5 * (a + b);
            const double Up = 18014398509481984.0, Big = 9.9792015476736e291;      // 2^54, 2^970
            if (Math.Abs(a) < Big && Math.Abs(b) < Big) return (a * Up + b * Up) * 0.5 / Up;
            return 0.5 * a + 0.5 * b;
        }
    }
}
