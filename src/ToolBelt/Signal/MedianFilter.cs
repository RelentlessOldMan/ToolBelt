// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Sliding-window median filter — rank-order smoothing that removes impulsive (spike) noise while
    /// preserving edges, where a linear smoother would blur them. At the ends the window shrinks to the
    /// samples available (MATLAB's <c>medfilt1</c> 'truncate'); an even count there takes the mean of the middle pair.
    /// NaN samples (dropouts) are left out of each window, as in <see cref="Hampel"/>; a window of nothing but NaN gives NaN.
    /// </summary>
    public static class MedianFilter
    {
        public static double[] Apply(IReadOnlyList<double> signal, int windowSize)
        {
            if (signal is null) throw new ArgumentNullException(nameof(signal));
            if (windowSize < 1) throw new ArgumentOutOfRangeException(nameof(windowSize), windowSize, "Window size must be at least 1.");
            if ((windowSize & 1) == 0) throw new ArgumentException("Window size must be odd.", nameof(windowSize));

            int n = signal.Count;
            var result = new double[n];
            int half = windowSize / 2;
            // The window is kept sorted as it slides, one binary-searched removal and insertion per sample: re-sorting it for
            // every sample was O(n·w·log w), 16 s for 20000 samples with a 20001-sample window. NaN is never stored (a sort
            // put it first, so it acted as −∞).
            var window = new double[Math.Min(windowSize, n)];               // a window never holds more than the record
            int count = 0;
            for (int j = 0; j < n && j <= half; j++) Insert(window, ref count, signal[j]);

            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    long leaving = (long)i - half - 1, entering = (long)i + half;
                    if (leaving >= 0) Remove(window, ref count, signal[(int)leaving]);
                    if (entering < n) Insert(window, ref count, signal[(int)entering]);
                }
                if (count == 0) { result[i] = double.NaN; continue; }
                int mid = count / 2;
                result[i] = count % 2 != 0 ? window[mid] : Midpoint(window[mid - 1], window[mid]);
            }
            return result;
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

        // 0.5·(a + b) overflowed for two values near double.MaxValue; −∞ and +∞ give NaN either way.
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
