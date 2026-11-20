using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 187 (signal analysis, eleventh pass): each failed on the code before its fix.
    public sealed class SignalReview187Tests
    {
        // A double as an exact fraction num / 2^shift.
        private static (BigInteger Num, int Shift) Exact(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            BigInteger n = man;
            if (bits < 0) n = -n;
            int e = exp - 1075;   // v = n * 2^e
            return e >= 0 ? (n << e, 0) : (n, -e);
        }

        private static BigInteger Scaled(double v, int shift)
        {
            var (n, s) = Exact(v);
            return n << (shift - s);
        }

        // Near-flat peaks, the centre the largest: left - 2c + right cancelled, 55,921 ulps off the exact vertex. Graded
        // against the exact rational offset (left - right) / (2 ((left - c) + (right - c))).
        public void Parabolic_NearFlatPeaks_MatchTheExactVertex()
        {
            var rng = new Random(187);
            int bad = 0;
            string first = "";
            for (int trial = 0; trial < 20000; trial++)
            {
                double c = Math.Pow(2, rng.Next(-900, 900)) * (1 + rng.NextDouble());
                double rel = Math.Pow(10, -1 - 14 * rng.NextDouble());
                double l = c * (1 - rel * rng.NextDouble()), r = c * (1 - rel * rng.NextDouble());
                if (!(l <= c && r <= c) || (l == c && r == c)) continue;
                double got = PeakInterpolation.Parabolic(l, c, r).Offset;
                const int sh = 1200 + 1075;
                BigInteger L = Scaled(l, sh), C = Scaled(c, sh), R = Scaled(r, sh);
                BigInteger num = L - R, den = 2 * ((L - C) + (R - C));
                // |got - num/den| <= 4 ulps of the exact value: |got*den - num| * 2^52 <= 4 * |num| (up to the ulp's binade).
                var (gn, gs) = Exact(got);
                BigInteger lhs = BigInteger.Abs(gn * den - (num << gs)) << 52, rhs = 4 * BigInteger.Abs(num) << gs;
                if (lhs > rhs && bad++ == 0) first = $"({l:R}, {c:R}, {r:R}): offset {got:R}";
            }
            Check.Equal(0, bad, first);
        }

        // The parabola was fitted to logs of size 700, which cancel: the offset for (1.906..., ...) was -0.000352 and the
        // same values times 2^1000 gave -0.002262. Scaling by a power of two leaves the ratios, and so the offset, unchanged.
        public void Gaussian_Offset_DoesNotDependOnScale()
        {
            double l = 1.906076194650735, c = 1.9060761946747435, r = 1.9060761946507012;
            double o = PeakInterpolation.Gaussian(l, c, r).Offset;
            double big = Math.Pow(2, 1000);
            Check.Equal(o, PeakInterpolation.Gaussian(l * big, c * big, r * big).Offset);
            Check.True(Math.Abs(o + 0.000351) < 0.000001, "offset " + o.ToString("R"));
            var rng = new Random(1870);
            int bad = 0;
            string first = "";
            for (int trial = 0; trial < 20000; trial++)
            {
                double cc = 1 + rng.NextDouble();
                double rel = Math.Pow(10, -2 - 12 * rng.NextDouble());
                double ll = cc * (1 - rel * rng.NextDouble()), rr = cc * (1 - rel * rng.NextDouble());
                double k = Math.Pow(2, rng.Next(-1000, 1000));
                double want = PeakInterpolation.Gaussian(ll, cc, rr).Offset;
                double got = PeakInterpolation.Gaussian(ll * k, cc * k, rr * k).Offset;
                if (got != want && bad++ == 0) first = $"({ll:R}, {cc:R}, {rr:R}) x {k:R}: {got:R}, unscaled {want:R}";
            }
            Check.Equal(0, bad, first);
        }

        // The height was exp(ln c): a flat 1e300 came back 9.999999999999763e299, double.Epsilon came back 0, and SpectralPeak
        // on [1, 7, 7, 7, 1] reported 6.999999999999999.
        public void Gaussian_Height_IsExactOnFlatPeaksAndAccurateNearThem()
        {
            Check.Equal(1e300, PeakInterpolation.Gaussian(1e300, 1e300, 1e300).Peak);
            Check.Equal(double.Epsilon, PeakInterpolation.Gaussian(double.Epsilon, double.Epsilon, double.Epsilon).Peak);
            var rng = new Random(1871);
            double worst = 0;
            string at = "";
            for (int trial = 0; trial < 20000; trial++)
            {
                double c = Math.Pow(2, rng.Next(-1020, 1020)) * (1 + rng.NextDouble());
                if (double.IsInfinity(c)) continue;
                Check.Equal(c, PeakInterpolation.Gaussian(c, c, c).Peak);
                // A Gaussian h exp(-a (x - d)^2) sampled at -1, 0, 1, scaled so that it is exact in ratios.
                double a = 1e-6 + rng.NextDouble(), d = rng.NextDouble() - 0.5;
                double gl = Math.Exp(-a * (-1 - d) * (-1 - d) + a * d * d), gr = Math.Exp(-a * (1 - d) * (1 - d) + a * d * d);
                double l = c * gl, r = c * gr;
                if (l == 0 || r == 0) continue;
                var (o, p) = PeakInterpolation.Gaussian(l, c, r);
                // Reference in the unscaled ratios: the vertex of the log parabola through ln gl, 0, ln gr.
                double ul = Math.Log(l / c), ur = Math.Log(r / c);
                double delta = 0.5 * (ul - ur) / (ul + ur);
                double wantP = c * Math.Exp(-0.25 * (ul - ur) * delta);
                double e = Math.Abs(p / wantP - 1);
                if (e > worst) { worst = e; at = $"c {c:R}: peak {p:R}, reference {wantP:R}"; }
            }
            Check.True(worst <= 4e-16, "height " + worst.ToString("E2") + " off at " + at);
        }

        // A subnormal rms / fullScale went to log10 with its digits lost: 1e-300 against 1.4e23 read -6466.124 dB for
        // -6462.923. Graded against the difference of the logs, which is accurate to well under 1e-9 dB here.
        public void DbFs_SubnormalRatio_KeepsItsDigits()
        {
            var rng = new Random(1872);
            double worst = 0;
            string at = "";
            for (int trial = 0; trial < 5000; trial++)
            {
                double v = Math.Pow(10, -300 * rng.NextDouble()) * (1 + rng.NextDouble());
                double fs = Math.Pow(10, 300 * rng.NextDouble());
                double rms = LevelConversions.Rms(new[] { v });
                double want = 20 * (Math.Log10(rms) - Math.Log10(fs));
                double got = LevelConversions.DbFs(new[] { v }, fs);
                double e = Math.Abs(got - want);
                if (e > worst) { worst = e; at = $"{v:R} against {fs:R}: {got:R} dB, reference {want:R}"; }
            }
            Check.True(worst <= 1e-9, "DbFs " + worst.ToString("E2") + " dB off at " + at);
        }

        // The direct sums that refine the FFT's pick round too, so exact ties broke against "nearest zero":
        // [-2, 0, -1, -2, 2, 2] against [-2e100, -2e100, -1e100, 2e100] gave lag -2 where lags -1 and -2 are equal. Graded
        // against the correlation of the exact values in BigInteger, on both the raw and the scaled path.
        public void CrossCorrelationPeak_ExactTies_GoToTheLagNearestZero()
        {
            Check.Equal(-1, TimeDelayEstimate.CrossCorrelationPeak(new double[] { -2, 0, -1, -2, 2, 2 }, new[] { -2e100, -2e100, -1e100, 2e100 }).Lag);
            var rng = new Random(1873);
            double[] scales = { 1, 1e100, 3e-90, 1e200, 7e-250, 0.1 };
            int bad = 0;
            string first = "";
            for (int trial = 0; trial < 20000; trial++)
            {
                double sa = scales[rng.Next(scales.Length)], sb = scales[rng.Next(scales.Length)];
                double[] a = Enumerable.Range(0, 1 + rng.Next(8)).Select(_ => (rng.Next(5) - 2) * sa).ToArray();
                double[] b = Enumerable.Range(0, 1 + rng.Next(8)).Select(_ => (rng.Next(5) - 2) * sb).ToArray();
                if (a.All(v => v == 0) || b.All(v => v == 0)) continue;
                const int sh = 1200 + 1075;
                int wantLag = 0;
                BigInteger best = 0;
                bool any = false;
                for (int k = -(a.Length - 1); k <= b.Length - 1; k++)
                {
                    BigInteger sum = 0;
                    for (int l = Math.Max(0, -k); l < Math.Min(a.Length, b.Length - k); l++) sum += Scaled(a[l], sh) * Scaled(b[l + k], sh);
                    if (!any || sum > best || (sum == best && Math.Abs(k) < Math.Abs(wantLag))) { best = sum; wantLag = k; any = true; }
                }
                int got = TimeDelayEstimate.CrossCorrelationPeak(a, b).Lag;
                if (got != wantLag && bad++ == 0) first = $"[{string.Join(", ", a)}] against [{string.Join(", ", b)}]: lag {got}, exact {wantLag}";
            }
            Check.Equal(0, bad, first);
        }

        // Halves went to even: 5 samples halved gave 2 and 7 gave 4. The rates are chosen so N * target / source is exact.
        public void ToRate_HalfLengths_RoundUp()
        {
            Check.Equal(3, Resample.ToRate(new double[5], 2, 1).Length);
            Check.Equal(13, Resample.ToRate(new double[10], 4, 5).Length);
            int bad = 0;
            string first = "";
            for (int n = 1; n <= 200; n++)
                foreach (int s in new[] { 1, 2, 4, 8 })
                    for (int t = 1; t <= 9; t++)
                    {
                        int want = Math.Max(1, (2 * n * t + s) / (2 * s));
                        int got = Resample.ToRate(new double[n], s, t).Length;
                        if (got != want && bad++ == 0) first = $"n={n} {s} -> {t}: length {got}, want {want}";
                    }
            Check.Equal(0, bad, first);
        }

        // Ties went to the even bin (2.5 to 2, 3.5 to 4). A frequency exactly between two centres goes to the higher one.
        public void NearestBin_ExactTies_GoToTheHigherBin()
        {
            int bad = 0;
            string first = "";
            foreach (int n in new[] { 2, 4, 16, 64, 1024 })
                foreach (double fs in new[] { 1.0, 8.0, 48000.0, 0.25 })
                    for (int b = 0; b < n / 2; b++)
                    {
                        double f = (b + 0.5) * fs / n;
                        int got = FrequencyGrid.NearestBin(f, n, fs);
                        if (got != b + 1 && bad++ == 0) first = $"f={f:R} N={n} fs={fs:R}: bin {got}, want {b + 1}";
                    }
            Check.Equal(0, bad, first);
        }

        // w[n] and w[N-1-n] differed in the last bits at almost every length from 4 on (cos x against cos(2 pi - x)).
        public void Windows_AreSymmetricBitForBit()
        {
            int bad = 0;
            string first = "";
            foreach (WindowType t in Enum.GetValues(typeof(WindowType)))
                for (int n = 1; n <= 2000; n++)
                {
                    var w = Window.Create(t, n);
                    for (int i = 0; i < n / 2; i++)
                        if (BitConverter.DoubleToInt64Bits(w[i]) != BitConverter.DoubleToInt64Bits(w[n - 1 - i]))
                        {
                            if (bad++ == 0) first = $"{t} N={n}: w[{i}] = {w[i]:R}, w[{n - 1 - i}] = {w[n - 1 - i]:R}";
                            break;
                        }
                }
            Check.Equal(0, bad, first);
        }
    }
}
