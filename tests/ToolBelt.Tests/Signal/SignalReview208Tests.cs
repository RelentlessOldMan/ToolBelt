using System;
using System.Diagnostics;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 208 (signal analysis, thirteenth pass): each failed on the code before its fix.
    public sealed class SignalReview208Tests
    {
        // A double as an exact fraction num * 2^exp.
        private static (BigInteger Num, int Exp) Exact(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            return (bits < 0 ? -man : man, exp - 1075);
        }

        // The neighbouring double towards +inf (dir 1) or -inf (dir -1); finite inputs only.
        private static double Step(double v, int dir)
        {
            if (v == 0) return dir > 0 ? double.Epsilon : -double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(v);
            return BitConverter.Int64BitsToDouble((v > 0) == (dir > 0) ? bits + 1 : bits - 1);
        }

        // |r*q - num*2^e| scaled by 2^-low, exactly.
        private static BigInteger Distance(double r, BigInteger num, int e, long q, int low)
        {
            var (mr, er) = Exact(r);
            return BigInteger.Abs(mr * q * BigInteger.Pow(2, er - low) - num * BigInteger.Pow(2, e - low));
        }

        // Whether r is num*2^e/q correctly rounded (to nearest, ties to even). q > 0.
        private static bool IsNearest(double r, BigInteger num, int e, long q)
        {
            if (double.IsInfinity(r) || double.IsNaN(r)) return false;
            double lo = Step(r, -1), hi = Step(r, 1);
            int low = Math.Min(e, -1075);
            BigInteger d = Distance(r, num, e, q, low);
            foreach (double other in new[] { lo, hi })
            {
                if (double.IsInfinity(other)) continue;
                BigInteger o = Distance(other, num, e, q, low);
                if (o < d) return false;
                if (o == d && (BitConverter.DoubleToInt64Bits(r) & 1) != 0) return false;
            }
            return true;
        }

        // A random double of magnitude about 2^lo to 2^hi, either sign; subnormal when lo is below -1022.
        private static double RandomMagnitude(Random rng, int lo, int hi)
        {
            double m = 1 + rng.NextDouble();
            int k = rng.Next(lo, hi);
            double v = m * Math.Pow(2, k / 2) * Math.Pow(2, k - k / 2);          // each factor normal
            return rng.Next(2) == 0 ? v : -v;
        }

        // ---------- Spectrum.Amplitude: |X|/N rounded once ----------

        // It multiplied by a rounded 1/N or 2/N: |X| = 3 at N = 10 gave 0.30000000000000004, and 5eps at N = 10, an exact tie
        // at 0.5eps, gave eps instead of 0.
        public void Spectrum_Amplitude_IsCorrectlyRounded()
        {
            var ten = new Complex[10];
            ten[0] = 3;
            Check.Equal(0.3, Spectrum.Amplitude(ten)[0]);
            ten[0] = 5 * double.Epsilon;
            Check.Equal(0.0, Spectrum.Amplitude(ten)[0]);

            var rng = new Random(208);
            int bad = 0;
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(1, 70);
                var fft = new Complex[n];
                for (int k = 0; k < n; k++)
                {
                    int kind = rng.Next(3);
                    fft[k] = kind == 0 ? rng.Next(-1000000, 1000000) : kind == 1 ? RandomMagnitude(rng, -1074, -1000) : RandomMagnitude(rng, -40, 40);
                }
                double[] amp = Spectrum.Amplitude(fft);
                for (int k = 0; k < amp.Length; k++)
                {
                    bool edge = k == 0 || (n % 2 == 0 && k == n / 2);
                    var (num, e) = Exact(Math.Abs(fft[k].Real));
                    if (!IsNearest(amp[k], edge ? num : 2 * num, e, n)) bad++;
                }
            }
            Check.Equal(0, bad);
        }

        // ---------- MedianFilter and Hampel: the midpoint of two values rounded once ----------

        // Halving each value first rounded a subnormal's last bit when the other was 2^-1000 or more: (2^21 + 1)eps and
        // 2^-1000 gave a midpoint one ulp low.
        public void MedianFilter_Midpoint_IsCorrectlyRounded()
        {
            var rng = new Random(2081);
            int bad = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                double a = RandomMagnitude(rng, -1074, -1022), b = RandomMagnitude(rng, -1074, rng.Next(2) == 0 ? -900 : 1023);
                if (trial % 3 == 0) a = Math.Abs(a);
                if (trial == 0) { a = (Math.Pow(2, 21) + 1) * double.Epsilon; b = Math.Pow(2, -1000); }
                double m = MedianFilter.Apply(new[] { a, b }, 3)[0];
                var (na, ea) = Exact(a);
                var (nb, eb) = Exact(b);
                int e = Math.Min(ea, eb);
                BigInteger sum = na * BigInteger.Pow(2, ea - e) + nb * BigInteger.Pow(2, eb - e);
                if (!IsNearest(m, sum, e - 1, 1)) bad++;
            }
            Check.Equal(0, bad);
        }

        public void Hampel_Midpoint_IsCorrectlyRounded()
        {
            var rng = new Random(2082);
            int bad = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                double a = RandomMagnitude(rng, -1074, -1022), b = Math.Abs(RandomMagnitude(rng, -1060, -900));
                if (trial == 0) { a = (Math.Pow(2, 21) + 1) * double.Epsilon; b = Math.Pow(2, -1000); }
                // The NaN is replaced by the median of its neighbours, a and b (the 1.0 keeps the largest sample at 1, so
                // no rescale happens).
                double m = Hampel.Filter(new[] { a, double.NaN, b, 1.0 }, 1)[1];
                var (na, ea) = Exact(a);
                var (nb, eb) = Exact(b);
                int e = Math.Min(ea, eb);
                BigInteger sum = na * BigInteger.Pow(2, ea - e) + nb * BigInteger.Pow(2, eb - e);
                if (!IsNearest(m, sum, e - 1, 1)) bad++;
            }
            Check.Equal(0, bad);
        }

        // ---------- WelchPsd: each segment scaled by its own peak ----------

        // One scale from the whole signal's peak underflowed a quiet segment beside a loud one.
        public void WelchPsd_QuietSegmentBesideALoudOne_KeepsItsDensity()
        {
            var (_, psd) = WelchPsd.Estimate(new[] { 1, 1, 1e-200, -1e-200 }, 1e-100, 2, 0, WindowType.Rectangular);
            Check.True(Math.Abs(psd[1] / 1e-300 - 1) < 1e-12, "Nyquist " + psd[1]);
            Check.True(Math.Abs(psd[0] / 1e100 - 1) < 1e-12, "DC " + psd[0]);
            var (_, big) = WelchPsd.Estimate(new[] { 1e300, 1e300, 1e140, -1e140 }, 1, 2, 0, WindowType.Rectangular);
            Check.True(Math.Abs(big[1] / 1e280 - 1) < 1e-12, "Nyquist " + big[1]);
        }

        // Differential: segments that do not overlap average to the mean of the one-segment estimates, each of which has its
        // own peak.
        public void WelchPsd_SegmentsOfVeryDifferentScale_AverageTheirEstimates()
        {
            var rng = new Random(2083);
            int checkedBins = 0, bad = 0;
            for (int trial = 0; trial < 400; trial++)
            {
                int len = rng.Next(2, 17), segs = rng.Next(2, 6);
                WindowType window = rng.Next(2) == 0 ? WindowType.Rectangular : WindowType.Hann;
                if (window == WindowType.Hann && len < 3) len = 3;
                // Half the trials are pairs [c, c] or [c, -c], whose transforms are exact and zero in one bin: a loud
                // segment then leaves that bin to the quiet ones.
                bool pairs = trial % 2 == 0;
                if (pairs) { len = 2; window = WindowType.Rectangular; }
                double fs = Math.Pow(10, rng.Next(-100, 101));
                var x = new double[len * segs];
                for (int s = 0; s < segs; s++)
                {
                    double scale = Math.Pow(10, rng.Next(-150, 151));
                    double sign = rng.Next(2) == 0 ? 1 : -1;
                    for (int i = 0; i < len; i++) x[s * len + i] = pairs ? (i == 0 ? scale : sign * scale) : (rng.NextDouble() * 2 - 1) * scale;
                }
                var (_, psd) = WelchPsd.Estimate(x, fs, len, 0, window);
                var each = new double[segs][];
                for (int s = 0; s < segs; s++)
                {
                    var one = new double[len];
                    Array.Copy(x, s * len, one, 0, len);
                    each[s] = WelchPsd.Estimate(one, fs, len, 0, window).Psd;
                }
                for (int k = 0; k < psd.Length; k++)
                {
                    double sum = 0;
                    for (int s = 0; s < segs; s++) sum += each[s][k];
                    double expected = sum / segs;
                    if (!(expected > 1e-290 && expected < 1e290)) continue;
                    checkedBins++;
                    if (!(Math.Abs(psd[k] / expected - 1) < 1e-9)) bad++;
                }
            }
            Check.True(checkedBins > 1000, "bins checked " + checkedBins);
            Check.Equal(0, bad);
        }

        // ---------- TimeDelayEstimate.CrossCorrelationPeak: the exact tie-break's budget ----------

        // The exact tie-break ran up to the main budget of terms, each far dearer than a flop: 30 s here.
        public void CrossCorrelationPeak_ManyExactTies_IsFast()
        {
            var reference = new double[640];
            var delayed = new double[524288];
            for (int i = 0; i < reference.Length; i++) reference[i] = 1;
            for (int i = 0; i < delayed.Length; i++) delayed[i] = 1;
            var watch = Stopwatch.StartNew();
            var (lag, value) = TimeDelayEstimate.CrossCorrelationPeak(reference, delayed);
            watch.Stop();
            Check.Equal(0, lag);
            Check.Equal(640.0, value);
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(10), "took " + watch.Elapsed);
        }
    }
}
