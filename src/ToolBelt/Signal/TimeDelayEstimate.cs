// ToolBelt drop-in — self-contained; depends on ToolBelt.Signal.Fft.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Estimates the lag (in samples) that best aligns two signals — the core of arrival-time and
    /// echo-delay measurement. Plain cross-correlation for clean signals; GCC-PHAT when a sharp,
    /// noise-robust peak matters more than amplitude fidelity.
    /// </summary>
    public static class TimeDelayEstimate
    {
        /// <summary>
        /// The integer lag k (in samples) maximizing the cross-correlation of <paramref name="reference"/>
        /// and <paramref name="delayed"/>. A positive result means <paramref name="delayed"/> lags
        /// <paramref name="reference"/> by k samples.
        /// </summary>
        public static int CrossCorrelationLag(double[] reference, double[] delayed)
        {
            var (lag, _) = CrossCorrelationPeak(reference, delayed);
            return lag;
        }

        /// <summary>The lag and the correlation value at that lag. Lags tied at the peak go to the one nearest zero.</summary>
        public static (int Lag, double Value) CrossCorrelationPeak(double[] reference, double[] delayed)
        {
            if (reference is null) throw new ArgumentNullException(nameof(reference));
            if (delayed is null) throw new ArgumentNullException(nameof(delayed));
            if (reference.Length == 0) throw new ArgumentException("Both signals must be non-empty.", nameof(reference));
            if (delayed.Length == 0) throw new ArgumentException("Both signals must be non-empty.", nameof(delayed));
            CheckFinite(reference, nameof(reference));
            CheckFinite(delayed, nameof(delayed));

            // Linear cross-correlation via circular convolution with padding.
            int m = PaddedLength((long)reference.Length + delayed.Length - 1, nameof(delayed));

            // Each input is scaled to a peak of 1 first: the products overflowed (NaN, so lag 0 with value −∞) from about
            // 1e154 and underflowed (every lag 0, so lag 0) below about 1e-165. The value is scaled back at the end.
            double sa = MaxAbs(reference), sb = MaxAbs(delayed);
            var fa = Fft.Forward(Pad(reference, m, sa));
            var fb = Fft.Forward(Pad(delayed, m, sb));
            var prod = new Complex[m];
            // conj(A)·B places lag k = sum_l delayed[l+k]·reference[l], so a right-shifted (later)
            // delayed copy peaks at a positive lag — the intuitive sign.
            for (int i = 0; i < m; i++) prod[i] = Complex.Conjugate(fa[i]) * fb[i];
            var corr = Fft.Inverse(prod);

            var (lag, value) = FindPeak(corr, reference.Length - 1, delayed.Length - 1);
            bool raw;
            (lag, value, raw) = Refine(corr, reference, delayed, sa, sb, lag, value);
            if (raw) return (lag, value);
            // The scales' product first: value·sa overflowed for a 1e308 reference against a 1e-300 copy (∞ for 1e9). Where the
            // product itself overflows or goes subnormal, both scales are large or both small and the order doesn't matter.
            double scale = sa * sb;
            return (lag, !double.IsInfinity(scale) && scale >= 2.2250738585072014e-308 ? value * scale : value * sa * sb);
        }

        /// <summary>
        /// GCC-PHAT lag: cross-correlation with the magnitude whitened away, leaving only phase. Far
        /// sharper and more robust than plain correlation for reverberant or coloured signals. Requires
        /// the two signals to be the same length. Frequencies whose cross-power is below <paramref name="epsilon"/> times the
        /// strongest one are left out rather than whitened (their phase is noise), so the result does not depend on scale.
        /// </summary>
        public static int GccPhat(double[] a, double[] b, double epsilon = 1e-12)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            if (a.Length != b.Length) throw new ArgumentException("GCC-PHAT requires equal-length signals.", nameof(b));
            if (a.Length == 0) throw new ArgumentException("Signals must be non-empty.", nameof(a));
            if (!(epsilon >= 0 && epsilon < 1)) throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon, "Epsilon is a relative threshold in [0, 1).");
            CheckFinite(a, nameof(a));
            CheckFinite(b, nameof(b));

            int len = a.Length;
            int m = PaddedLength(2L * len, nameof(a));

            var fa = Fft.Forward(Pad(a, m, MaxAbs(a)));                 // scaled, as above: PHAT itself is scale-free
            var fb = Fft.Forward(Pad(b, m, MaxAbs(b)));
            var cross = new Complex[m];
            double peak = 0;
            for (int i = 0; i < m; i++)
            {
                cross[i] = Complex.Conjugate(fa[i]) * fb[i]; // positive lag => b is a later copy of a
                peak = Math.Max(peak, cross[i].Magnitude);
            }
            double threshold = epsilon * peak;
            for (int i = 0; i < m; i++)
            {
                double mag = cross[i].Magnitude;
                cross[i] = mag == 0 || mag < threshold ? Complex.Zero : cross[i] / mag;
            }
            var corr = Fft.Inverse(cross);

            // Peak search over lags [-(len-1), len-1], stored circularly.
            return FindPeak(corr, len - 1, len - 1).Lag;
        }

        private static void CheckFinite(double[] x, string name)
        {
            for (int i = 0; i < x.Length; i++)
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i])) throw new ArgumentException($"{name}[{i}] is {x[i]}; samples must be finite.", name);
        }

        // The power of two at or above `length`, which must stay a usable array size.
        private static int PaddedLength(long length, string paramName)
        {
            if (length > 1 << 30) throw new ArgumentException("The signals are too long to correlate (the padded transform would exceed 2^30 points).", paramName);
            int m = 1;
            while (m < length) m <<= 1;
            return m;
        }

        // A power of two at or above the peak, so dividing by it is exact: dividing by the peak itself rounded, and an exact
        // tie between two lags went to the far one once a peak was past 1e140 or under 1e-140 (2^497, 2^-608).
        private static double MaxAbs(double[] x)
        {
            double max = 0;
            foreach (double v in x) max = Math.Max(max, Math.Abs(v));
            if (max == 0) return 1;
            double p = 1;
            while (p < max && p < 8.98846567431158E307) p *= 2;              // 2^1023 at most
            while (p / 2 >= max) p /= 2;
            return p;
        }

        private static double[] Pad(double[] x, int length, double scale)
        {
            var padded = new double[length];
            for (int i = 0; i < x.Length; i++) padded[i] = x[i] / scale;
            return padded;
        }

        // The correlation array holds lag L at index L for L>=0 and lag L at index m+L for L<0.
        // Valid lags span [-maxNegative, maxPositive] = [-(reference-1), delayed-1]. Ties (all-zero input, say) go to the
        // lag nearest zero.
        // The FFT's rounding broke exact ties against the documented "nearest zero" rule ([−1] against
        // [−1, −1, 1, 2, 1, 2, −1] gave lag 1 at 1.0000000000000002 though lags 0 and 1 are both 1). Lags within the FFT's
        // error of the peak are summed directly and compared again. Past 64 such lags the FFT's pick stood, so a template
        // against a periodic train (or [1] against 200 ones) gave a lag among the ties by rounding noise ((7, 1.0000000000000002)
        // for (0, 1)): every near-tie is summed while the sums' total length stays within a budget, and past it the near-tie
        // nearest zero is taken, which the FFT cannot tell from the peak.
        // Where no product or sum can overflow or underflow the sums are of the samples themselves (raw: the value needs no
        // scaling back; dividing by the peaks first rounded the 14 of an integer correlation to 13.999999999999998).
        private static (int Lag, double Value, bool Raw) Refine(Complex[] corr, double[] reference, double[] delayed, double sa, double sb, int lag, double value)
        {
            // corr is of the scaled samples, so the tolerance is too.
            double norms = Math.Sqrt(SumSquares(reference, sa)) * Math.Sqrt(SumSquares(delayed, sb));
            double tolerance = 1e-12 * norms * Math.Max(1, Math.Log(corr.Length, 2));
            int m = corr.Length;
            var candidates = new List<int>();
            long work = 0, budget = Math.Max(64L * Math.Min(reference.Length, delayed.Length), 16L * m * (long)Math.Max(1, Math.Log(m, 2)));
            int nearest = 0;
            bool any = false;
            for (int k = -(reference.Length - 1); k <= delayed.Length - 1; k++)
            {
                if (corr[k >= 0 ? k : m + k].Real < value - tolerance) continue;
                candidates.Add(k);
                work += Math.Min(reference.Length, delayed.Length - k) - Math.Max(0, -k);
                if (!any || Math.Abs(k) < Math.Abs(nearest)) nearest = k;
                any = true;
            }
            const double Low = 1e-140, High = 1e140;
            bool raw = sa > Low && sa < High && sb > Low && sb < High;
            if (raw) sa = sb = 1;
            if (!any) return (lag, value, false);
            // The scaled sums lost the products far below the scales' product: [1e200, 1e-200] against [-1e200, 1e-200] peaks
            // at lag 1 with 1e200·1e-200 = 1, which scaled by 1/(sa·sb) was 1e-400, and 0 came back. A sum whose rounding bound
            // is not small beside it is redone exactly.
            (int, double, bool) Finish(int k, (double Sum, double Bound) s)
            {
                if (s.Bound <= Math.Abs(s.Sum) * 1e-12) return (k, s.Sum, raw);
                var r = Decompose(reference);
                var d = Decompose(delayed);
                int e = MinExponent(r) + MinExponent(d);
                return (k, ToDouble(ExactDot(r, d, k, e), e), true);
            }
            if (work > budget) return Finish(nearest, Direct(reference, delayed, sa, sb, nearest));
            int bestLag = 0;
            double best = double.NegativeInfinity, bestBound = 0;
            var sums = new (double Sum, double Bound)[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                int k = candidates[i];
                sums[i] = Direct(reference, delayed, sa, sb, k);
                if (sums[i].Sum > best || (sums[i].Sum == best && Math.Abs(k) < Math.Abs(bestLag))) { best = sums[i].Sum; bestBound = sums[i].Bound; bestLag = k; }
            }
            // The direct sums round as well, so they broke exact ties too ([-2, 0, -1, -2, 2, 2] against
            // [-2e100, -2e100, -1e100, 2e100] gave lag -2, though lags -1 and -2 are equal): the sums within their rounding
            // bounds of the best are compared exactly, while their length stays within the budget.
            var close = new List<int>();
            long exactWork = 0;
            for (int i = 0; i < candidates.Count; i++)
                if (sums[i].Sum >= best - (bestBound + sums[i].Bound))
                {
                    close.Add(i);
                    exactWork += Math.Min(reference.Length, delayed.Length - candidates[i]) - Math.Max(0, -candidates[i]);
                }
            if (close.Count < 2 || exactWork > budget) return Finish(bestLag, (best, bestBound));
            var ra = Decompose(reference);
            var da = Decompose(delayed);
            int e0 = MinExponent(ra) + MinExponent(da);
            int pick = -1;
            BigInteger pickSum = BigInteger.Zero;
            foreach (int i in close)
            {
                BigInteger s = ExactDot(ra, da, candidates[i], e0);
                if (pick < 0 || s > pickSum || (s == pickSum && Math.Abs(candidates[i]) < Math.Abs(candidates[pick]))) { pick = i; pickSum = s; }
            }
            return (candidates[pick], ToDouble(pickSum, e0), true);
        }

        // sum_l delayed[l + k]·reference[l] on the scaled samples, and a bound on its rounding error (each scaled sample
        // within an ulp or, below the normal range, the smallest subnormal; each product and partial sum within an ulp).
        private static (double Sum, double Bound) Direct(double[] reference, double[] delayed, double sa, double sb, int k)
        {
            double sum = 0, abs = 0;
            int from = Math.Max(0, -k), to = Math.Min(reference.Length, delayed.Length - k);
            for (int l = from; l < to; l++)
            {
                double p = reference[l] / sa * (delayed[l + k] / sb);
                sum += p;
                abs += Math.Abs(p);
            }
            double n = to - from;
            return (sum, 2.3e-16 * (n + 4) * abs + 4 * (n + 1) * double.Epsilon);
        }

        // Each sample as an integer mantissa times a power of two (zero has mantissa 0).
        private static (long Mantissa, int Exponent)[] Decompose(double[] x)
        {
            var r = new (long, int)[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                long bits = BitConverter.DoubleToInt64Bits(x[i]);
                int exp = (int)((bits >> 52) & 0x7FF);
                long man = bits & 0xFFFFFFFFFFFFFL;
                if (exp == 0) exp = 1; else man |= 1L << 52;
                r[i] = (bits < 0 ? -man : man, exp - 1075);
            }
            return r;
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

        private static int MinExponent((long Mantissa, int Exponent)[] x)
        {
            int min = int.MaxValue;
            foreach (var v in x) if (v.Mantissa != 0) min = Math.Min(min, v.Exponent);
            return min == int.MaxValue ? 0 : min;
        }

        // The exact correlation at lag k, in units of 2^e0.
        private static BigInteger ExactDot((long Mantissa, int Exponent)[] reference, (long Mantissa, int Exponent)[] delayed, int k, int e0)
        {
            BigInteger sum = BigInteger.Zero;
            int from = Math.Max(0, -k), to = Math.Min(reference.Length, delayed.Length - k);
            for (int l = from; l < to; l++)
            {
                var a = reference[l];
                var b = delayed[l + k];
                if (a.Mantissa == 0 || b.Mantissa == 0) continue;
                sum += (new BigInteger(a.Mantissa) * b.Mantissa) << (a.Exponent + b.Exponent - e0);
            }
            return sum;
        }

        private static double SumSquares(double[] x, double scale)
        {
            double sum = 0;
            foreach (double v in x) { double s = v / scale; sum += s * s; }
            return sum;
        }

        private static (int Lag, double Value) FindPeak(Complex[] corr, int maxNegative, int maxPositive)
        {
            int m = corr.Length;
            int bestLag = 0;
            double best = double.NegativeInfinity;
            for (int lag = -maxNegative; lag <= maxPositive; lag++)
            {
                int idx = lag >= 0 ? lag : m + lag;
                double v = corr[idx].Real;
                if (v > best || (v == best && Math.Abs(lag) < Math.Abs(bestLag))) { best = v; bestLag = lag; }
            }
            return (bestLag, best);
        }
    }
}
