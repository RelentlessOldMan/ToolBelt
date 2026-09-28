// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Sliding-window median filter — rank-order smoothing that removes impulsive (spike) noise while
    /// preserving edges, where a linear smoother would blur them. At the ends the window shrinks to the
    /// samples available.
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
            var window = new List<double>(windowSize);

            for (int i = 0; i < n; i++)
            {
                window.Clear();
                int lo = Math.Max(0, i - half);
                int hi = Math.Min(n - 1, i + half);
                for (int j = lo; j <= hi; j++) window.Add(signal[j]);
                window.Sort();
                result[i] = window[window.Count / 2]; // odd count after shrink may be even at edges -> upper-middle
            }
            return result;
        }
    }
}
