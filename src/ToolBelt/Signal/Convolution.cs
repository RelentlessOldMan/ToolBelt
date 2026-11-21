// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Numerics;

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
            // NaN is the answer (one such input anywhere turned the rescue off for every element). The scale is the overlap's
            // own peaks: with the whole inputs' peaks, [1, x, x, x] ∗ [y, y, y] (products of 0.4ε) was not rescued, because 1·y
            // is normal, and its third element came out 0 for ε.
            bool anyProduct = true;
            bool checkedPeaks = false;
            for (int k = 0; k < result.Length; k++)
            {
                double r = result[k];
                bool nonFinite = double.IsInfinity(r) || double.IsNaN(r);
                if (!nonFinite && Math.Abs(r) >= TinyResult) continue;
                if (!checkedPeaks)
                {
                    anyProduct = FinitePeak(a) != 0 && FinitePeak(b) != 0;
                    checkedPeaks = true;
                }
                if (!anyProduct) continue;                                  // every product is 0 or non-finite
                int from = Math.Max(0, k - (b.Count - 1)), to = Math.Min(a.Count - 1, k);
                double pa = 0, pb = 0;
                bool skip = false;
                for (int i = from; i <= to; i++)
                {
                    double x = a[i], y = b[k - i];
                    if (double.IsInfinity(x) || double.IsNaN(x) || double.IsInfinity(y) || double.IsNaN(y)) { skip = true; break; }
                    pa = Math.Max(pa, Math.Abs(x));
                    pb = Math.Max(pb, Math.Abs(y));
                }
                if (skip || pa == 0 || pb == 0) continue;
                int ea = Exponent(pa), eb = Exponent(pb);
                double sum = 0;
                bool lost = false;
                for (int i = from; i <= to; i++)
                {
                    double p = ScaleB(a[i], -ea) * ScaleB(b[k - i], -eb);
                    if (Math.Abs(p) < 2.2250738585072014E-308 && a[i] != 0 && b[k - i] != 0) lost = true;
                    sum += p;
                }
                // A product far below the overlap's peaks flushed to 0 against them, and when the big ones cancelled it was the
                // answer: [2^1000, 2^1000, 3·2^-1000] ∗ [2^1000, −2^1000, 2^1000] gave 0 for 3. Then the element is summed exactly.
                result[k] = lost ? ExactSum(a, b, k, from, to) : ScaleB(sum, ea + eb);
            }
            return result;
        }

        // sum a[i]·b[k − i] over i = from..to, exactly, rounded once.
        private static double ExactSum(IReadOnlyList<double> a, IReadOnlyList<double> b, int k, int from, int to)
        {
            var terms = new (BigInteger Mantissa, int Exponent)[to - from + 1];
            int e0 = int.MaxValue;
            for (int i = from; i <= to; i++)
            {
                var (ma, xa) = Decompose(a[i]);
                var (mb, xb) = Decompose(b[k - i]);
                terms[i - from] = (new BigInteger(ma) * mb, xa + xb);
                if (ma != 0 && mb != 0) e0 = Math.Min(e0, xa + xb);
            }
            BigInteger sum = BigInteger.Zero;
            foreach (var t in terms) if (!t.Mantissa.IsZero) sum += t.Mantissa << (t.Exponent - e0);
            return ToDouble(sum, e0);
        }

        // x as an integer mantissa times a power of two (zero has mantissa 0).
        private static (long Mantissa, int Exponent) Decompose(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            return (bits < 0 ? -man : man, exp - 1075);
        }

        // m·2^e rounded once to the nearest double (ties to even), subnormals included.
        private static double ToDouble(BigInteger m, int e)
        {
            if (m.IsZero) return 0;
            bool negative = m.Sign < 0;
            if (negative) m = -m;
            byte[] bytes = m.ToByteArray();
            int bits = (bytes.Length - 1) * 8;
            for (int t = bytes[bytes.Length - 1]; t != 0; t >>= 1) bits++;
            int top = bits + e - 1;                                         // the exponent of the leading bit
            if (top > 1023) return negative ? double.NegativeInfinity : double.PositiveInfinity;
            int ulp = Math.Max(top - 52, -1074), shift = ulp - e;
            BigInteger q;
            if (shift <= 0) q = m << -shift;
            else
            {
                q = m >> shift;
                BigInteger rem = m - (q << shift), half = BigInteger.One << (shift - 1);
                if (rem > half || (rem == half && !q.IsEven)) q++;
            }
            double r = (double)q * Math.Pow(2, ulp);                        // q ≤ 2^53: exact, or ∞ past the range
            return negative ? -r : r;
        }

        // Below 2^-969 a sum of subnormal products may have lost bits to rounding each one.
        private const double TinyResult = 1.0020841800044864E-292;          // 2^-969
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
            if (e < -1000)
            {
                // Down to the bottom of the normal range first, exactly, then the rest in one step: a first 2^-1000 that
                // landed subnormal rounded, and the last step rounded again (c·2^-1050 came out 2ε for 1ε).
                int biased = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
                int down = Math.Min(-e, Math.Max(0, biased - 1));          // x·2^-down stays at or above 2^-1022
                e += down;
                for (; down > 1000; down -= 1000) x *= Math.Pow(2, -1000);
                x *= Math.Pow(2, -down);
                return x * Math.Pow(2, Math.Max(e, -1075));                 // past 2^-1075 the result rounds to ±0 anyway
            }
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
