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

            var result = new double[ResultLength(a, b)];
            for (int i = 0; i < a.Count; i++)
            {
                double ai = a[i];
                for (int j = 0; j < b.Count; j++)
                    result[i + j] += ai * b[j];
            }

            // Products or partial sums overflowed where the true element is representable ([1e200, 1e200] ∗ [1e200, −1e200]
            // gave [∞, NaN, −∞] for [∞, 0, −∞]): each non-finite element is summed again from inputs scaled to a peak of at
            // most 1 by a power of two (exact) and scaled back. Non-finite inputs are left alone: their ∞ or NaN is the answer.
            int ea = int.MinValue, eb = 0;
            for (int k = 0; k < result.Length; k++)
            {
                if (!double.IsInfinity(result[k]) && !double.IsNaN(result[k])) continue;
                if (ea == int.MinValue)
                {
                    double pa = Peak(a), pb = Peak(b);
                    if (double.IsInfinity(pa) || double.IsNaN(pa) || double.IsInfinity(pb) || double.IsNaN(pb)) break;
                    ea = pa > 1 ? (int)Math.Ceiling(Math.Log(pa, 2)) : 0;
                    eb = pb > 1 ? (int)Math.Ceiling(Math.Log(pb, 2)) : 0;
                }
                double da = Math.Pow(2, -ea), db = Math.Pow(2, -eb), sum = 0;
                int from = Math.Max(0, k - (b.Count - 1)), to = Math.Min(a.Count - 1, k);
                for (int i = from; i <= to; i++) sum += a[i] * da * (b[k - i] * db);
                result[k] = ScaleB(sum, ea + eb);
            }
            return result;
        }

        // a + b − 1 overflowed int for two long virtual lists (OverflowException, not an argument error).
        private static int ResultLength(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            long length = (long)a.Count + b.Count - 1;
            if (length > 0x7FFFFFC7) throw new ArgumentException($"The result would have {length} elements, more than an array holds.", nameof(b));
            return (int)length;
        }

        private static double Peak(IReadOnlyList<double> x)
        {
            double peak = 0;
            for (int i = 0; i < x.Count; i++) peak = Math.Max(peak, Math.Abs(x[i]));
            return peak;
        }

        // x·2^e in exact power-of-two steps: 2^e alone overflows past 1023.
        private static double ScaleB(double x, int e)
        {
            for (; e > 1000; e -= 1000) x *= Math.Pow(2, 1000);
            return x * Math.Pow(2, e);
        }

        /// <summary>
        /// Full cross-correlation of <paramref name="a"/> and <paramref name="b"/> (NumPy's <c>correlate(a, b, 'full')</c>).
        /// Element <c>k</c> is the overlap at lag <c>k - (b.Count - 1)</c>, so zero lag is element <c>b.Count - 1</c> (the
        /// center element only when the lengths are equal).
        /// </summary>
        public static double[] CrossCorrelate(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Count == 0 || b.Count == 0) return Array.Empty<double>();
            ResultLength(a, b);                                             // before the reversed copy is allocated
            // Cross-correlation is convolution with the second sequence reversed.
            var reversed = new double[b.Count];
            for (int i = 0; i < b.Count; i++) reversed[i] = b[b.Count - 1 - i];
            return Convolve(a, reversed);
        }
    }
}
