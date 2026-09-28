// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Direct (time-domain) convolution and cross-correlation. These are the O(n·m) reference
    /// implementations — correct and simple; an FFT-based fast path would be graded against exactly these.
    /// </summary>
    public static class Convolution
    {
        /// <summary>Full linear convolution of <paramref name="a"/> and <paramref name="b"/> (length a+b-1).</summary>
        public static double[] Convolve(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Count == 0 || b.Count == 0) return Array.Empty<double>();

            var result = new double[a.Count + b.Count - 1];
            for (int i = 0; i < a.Count; i++)
            {
                double ai = a[i];
                for (int j = 0; j < b.Count; j++)
                    result[i + j] += ai * b[j];
            }
            return result;
        }

        /// <summary>
        /// Full cross-correlation of <paramref name="a"/> and <paramref name="b"/>. Element <c>k</c> is the
        /// overlap at lag <c>k - (b.Count - 1)</c>, so the center element is zero lag.
        /// </summary>
        public static double[] CrossCorrelate(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            // Cross-correlation is convolution with the second sequence reversed.
            var reversed = new double[b.Count];
            for (int i = 0; i < b.Count; i++) reversed[i] = b[b.Count - 1 - i];
            return Convolve(a, reversed);
        }
    }
}
