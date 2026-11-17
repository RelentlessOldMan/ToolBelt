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
            // gave [∞, NaN, −∞] for [∞, 0, −∞]), or subnormal products were each rounded ([2^-537 ×3] ∗ [2^-538 ×3] gave zeros
            // for [0, ε, 2ε, ε, 0]): such an element is summed again from inputs scaled to a peak near 1 by a power of two
            // (exact) and scaled back, one rounding. An element whose overlap holds a non-finite input is left alone: its ∞ or
            // NaN is the answer (one such input anywhere turned the rescue off for every element).
            int ea = int.MinValue, eb = 0;
            for (int k = 0; k < result.Length; k++)
            {
                double r = result[k];
                bool nonFinite = double.IsInfinity(r) || double.IsNaN(r);
                if (!nonFinite && Math.Abs(r) >= TinyResult) continue;
                if (ea == int.MinValue)
                {
                    double pa = FinitePeak(a), pb = FinitePeak(b);
                    if (pa == 0 || pb == 0) { ea = int.MaxValue; continue; }
                    ea = Exponent(pa);
                    eb = Exponent(pb);
                }
                if (ea == int.MaxValue) continue;                           // every product is 0 or non-finite
                if (!nonFinite && ea + eb > TinyExponent) continue;         // products this large were not rounded as subnormals
                int from = Math.Max(0, k - (b.Count - 1)), to = Math.Min(a.Count - 1, k);
                double sum = 0;
                bool skip = false;
                for (int i = from; i <= to; i++)
                {
                    double x = a[i], y = b[k - i];
                    if (double.IsInfinity(x) || double.IsNaN(x) || double.IsInfinity(y) || double.IsNaN(y)) { skip = true; break; }
                    sum += ScaleB(x, -ea) * ScaleB(y, -eb);
                }
                if (!skip) result[k] = ScaleB(sum, ea + eb);
            }
            return result;
        }

        // Below 2^-969 a sum of subnormal products may have lost bits to rounding each one.
        private const double TinyResult = 1.0020841800044864E-292;          // 2^-969
        private const int TinyExponent = -969;
        private const double TwoTo64 = 18446744073709551616.0;

        // The exponent e of x's leading bit, so x·2^-e is in [1, 2).
        private static int Exponent(double x)
        {
            int e = 0;
            while (x >= TwoTo64) { x /= TwoTo64; e += 64; }
            while (x >= 2) { x *= 0.5; e++; }
            while (x < 1 / TwoTo64) { x *= TwoTo64; e -= 64; }
            while (x < 1) { x *= 2; e--; }
            return e;
        }

        // a + b − 1 overflowed int for two long virtual lists (OverflowException, not an argument error).
        private static int ResultLength(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            long length = (long)a.Count + b.Count - 1;
            if (length > 0x7FFFFFC7) throw new ArgumentException($"The result would have {length} elements, more than an array holds.", nameof(b));
            return (int)length;
        }

        private static double FinitePeak(IReadOnlyList<double> x)
        {
            double peak = 0;
            for (int i = 0; i < x.Count; i++)
            {
                double v = Math.Abs(x[i]);
                if (v > peak && !double.IsInfinity(v)) peak = v;           // NaN fails v > peak
            }
            return peak;
        }

        // x·2^e in exact power-of-two steps (2^e alone overflows past 1023 and underflows past −1074); only the last step
        // can round, when the result is subnormal.
        private static double ScaleB(double x, int e)
        {
            for (; e > 1000; e -= 1000) x *= Math.Pow(2, 1000);
            for (; e < -1000; e += 1000) x *= Math.Pow(2, -1000);
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
