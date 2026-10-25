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
            var window = new List<double>(Math.Min(windowSize, n));        // a window never holds more than the record

            for (int i = 0; i < n; i++)
            {
                window.Clear();
                int lo = (int)Math.Max(0, (long)i - half);
                int hi = (int)Math.Min(n - 1, (long)i + half);
                for (int j = lo; j <= hi; j++)
                    if (!double.IsNaN(signal[j])) window.Add(signal[j]);       // Sort put NaN first, so it acted as −∞
                if (window.Count == 0) { result[i] = double.NaN; continue; }
                window.Sort();
                int mid = window.Count / 2;
                result[i] = window.Count % 2 != 0 ? window[mid] : Midpoint(window[mid - 1], window[mid]);
            }
            return result;
        }

        // 0.5·(a + b) overflowed for two values near double.MaxValue; −∞ and +∞ give NaN either way.
        private static double Midpoint(double a, double b) => double.IsInfinity(a) || double.IsInfinity(b) ? 0.5 * (a + b) : 0.5 * a + 0.5 * b;
    }
}
