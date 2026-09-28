// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using ToolBelt.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Savitzky-Golay smoothing: fits a low-order polynomial over a sliding window and takes its value at
    /// the point. Unlike a moving average it preserves peak height and width instead of flattening them, so
    /// it is what you want for a noisy measurement trace. Polynomials up to the chosen order pass through
    /// unchanged. Windows near the ends shift to stay in range.
    /// </summary>
    public static class SavitzkyGolay
    {
        public static double[] Smooth(IReadOnlyList<double> data, int windowSize, int polynomialOrder)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (windowSize < 3 || (windowSize & 1) == 0)
                throw new ArgumentException("Window size must be an odd number of at least 3.", nameof(windowSize));
            if (polynomialOrder < 0 || polynomialOrder >= windowSize)
                throw new ArgumentOutOfRangeException(nameof(polynomialOrder), polynomialOrder, "Order must be in [0, windowSize).");
            if (data.Count < windowSize)
                throw new ArgumentException("Data length must be at least the window size.", nameof(data));

            int n = data.Count;
            int half = windowSize / 2;
            var window = new double[windowSize];
            var xs = new double[windowSize];
            var result = new double[n];

            for (int i = 0; i < n; i++)
            {
                // Center the window on i, shifting it inward at the ends so it always has windowSize points.
                int start = i - half;
                if (start < 0) start = 0;
                if (start > n - windowSize) start = n - windowSize;

                for (int k = 0; k < windowSize; k++)
                {
                    xs[k] = k;
                    window[k] = data[start + k];
                }
                Polynomial fit = Polynomial.Fit(xs, window, polynomialOrder);
                result[i] = fit.Evaluate(i - start);
            }
            return result;
        }
    }
}
