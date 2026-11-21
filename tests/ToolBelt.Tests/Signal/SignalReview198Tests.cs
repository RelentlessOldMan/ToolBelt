using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 198 (signal analysis, twelfth pass): each failed on the code before its fix.
    public sealed class SignalReview198Tests
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

        // Sign of x*a - y*b, exactly.
        private static int Compare(double x, long a, double y, long b)
        {
            var (mx, ex) = Exact(x);
            var (my, ey) = Exact(y);
            int e = Math.Min(ex, ey);
            BigInteger l = mx * a * BigInteger.Pow(2, ex - e), r = my * b * BigInteger.Pow(2, ey - e);
            return l.CompareTo(r);
        }

        // v * 2^-n for v normal and n in (500, 1500): the first step is exact, the second rounds once.
        private static double Down(double v, int n) => v * Math.Pow(2, -500) * Math.Pow(2, -(n - 500));

        public void ConvolveSubnormalRescueRoundsOnce()
        {
            double p = Math.Pow(2, -500), s = Math.Pow(2, -550), c = Math.Pow(2, -24) + Math.Pow(2, -25) - Math.Pow(2, -76);
            double[] r = Convolution.Convolve(new[] { p, p, p }, new[] { c * s, s, -s });
            Check.Equal(double.Epsilon, r[2]);
        }

        public void ConvolveKeepsATermFarBelowCancellingPeaks()
        {
            double big = Math.Pow(2, 1000), small = 3 * Math.Pow(2, -1000);
            double[] b = { big, -big, big };
            Check.Equal(3.0, Convolution.Convolve(new[] { big, big, small }, b)[2]);
            Check.Equal(3.0, Convolution.CrossCorrelate(new[] { big, big, small }, b)[2]);
        }

        public void WelchAndMultitaperScaleBackRoundOnce()
        {
            var rng = new Random(198);
            int mismatches = 0;
            for (int trial = 0; trial < 40; trial++)
            {
                var x = new double[64];
                for (int i = 0; i < x.Length; i++) x[i] = Math.Sin(0.7 * i) + 1e-4 * (rng.NextDouble() - 0.5);
                var y = new double[x.Length];
                for (int i = 0; i < x.Length; i++) y[i] = x[i] * Math.Pow(2, -500);
                var wx = WelchPsd.Estimate(x, 2, 32).Psd;
                var wy = WelchPsd.Estimate(y, 2, 32).Psd;
                for (int k = 0; k < wx.Length; k++) if (wy[k] != Down(wx[k], 1000)) mismatches++;
                var mx = Multitaper.Estimate(x, 2, 3).Psd;
                var my = Multitaper.Estimate(y, 2, 3).Psd;
                for (int k = 0; k < mx.Length; k++) if (my[k] != Down(mx[k], 1000)) mismatches++;
            }
            Check.Equal(0, mismatches);
        }

        public void CycleJitterOfAPerfectClockIsZero()
        {
            double[] pattern = { -1, 2, 2, 2, 2, 2, -1, -1, -1, -1 };
            var x = new double[100000];
            for (int i = 0; i < x.Length; i++) x[i] = pattern[i % 10];
            var stats = CycleMeasurements.Measure(x, 1, 0);
            Check.True(stats.Cycles.Count > 9000);
            foreach (var cycle in stats.Cycles)
            {
                Check.Equal(10.0, cycle.Period);
                Check.Equal(stats.Cycles[0].HighTime, cycle.HighTime);
            }
            Check.Equal(0.0, stats.PeriodStandardDeviation);
            Check.Equal(0.0, stats.PeakToPeakJitter);
            Check.Equal(0.0, stats.CycleToCycleJitter);
        }

        public void CyclesFoundWhenTheLevelIsNearAnExtreme()
        {
            var x = new double[100];
            for (int i = 0; i < x.Length; i++) x[i] = i % 10 < 5 ? 1 : 0;
            var stats = CycleMeasurements.Measure(x, 1, 0.05);
            Check.True(stats.Cycles.Count >= 8);
            foreach (var cycle in stats.Cycles) Check.Equal(10.0, cycle.Period);
        }

        public void LinearTakesCoincidentInputPointsAsIs()
        {
            var rng = new Random(1981);
            int bad = 0;
            for (int l = 2; l <= 60; l++)
                for (int n = 2; n <= 400; n++)
                {
                    if ((n - 1) % (l - 1) != 0) continue;
                    var x = new double[l];
                    for (int i = 0; i < l; i++) x[i] = rng.NextDouble() * 2 - 1;
                    if (l > 2) x[0] = double.NaN;
                    var r = Resample.Linear(x, n);
                    int stride = (n - 1) / (l - 1);
                    for (int k = 1; k < l; k++) if (!r[k * stride].Equals(x[k])) bad++;
                }
            Check.Equal(0, bad);
        }

        public void ToRateTakesCoincidentInputPointsAsIs()
        {
            var x = new double[20];
            for (int i = 0; i < x.Length; i++) x[i] = Math.Sin(1.3 * i) + 0.1 * i;
            x[5] = double.NaN;
            int bad = 0;
            for (int up = 2; up <= 50; up++)
                for (int src = 1; src <= 50; src++)
                {
                    var r = Resample.ToRate(x, src, (double)src * up);
                    for (int k = 0; k < x.Length && (long)k * up < r.Length; k++) if (!r[k * up].Equals(x[k])) bad++;
                }
            Check.Equal(0, bad);
        }

        public void ToRateLengthRoundsTheExactRatio()
        {
            Check.Equal(1903, Resample.ToRate(new double[1269], 7.8, 11.7).Length);
            var rng = new Random(19801);
            for (int trial = 0; trial < 5000; trial++)
            {
                int n = rng.Next(1, 3000);
                double s = Math.Round(rng.NextDouble() * 100, 1) + 0.1;
                // A target rate putting N*t/s near a half, nudged a few ulps either way.
                int l0 = rng.Next(1, 2 * n);
                double t = (l0 + 0.5) * s / n;
                t = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(t) + rng.Next(-3, 4));
                int got = Resample.ToRate(new double[n], s, t).Length;
                // The documented length round(N*t/s), a half rounded up, at least 1: (2L - 1)s <= 2Nt < (2L + 1)s.
                long l = Math.Max(1, (long)got);
                bool ok = Compare(t, 2L * n, s, 2 * l + 1) < 0 && (l == 1 ? true : Compare(t, 2L * n, s, 2 * l - 1) >= 0);
                if (!ok) Check.True(false, $"n {n} s {s:R} t {t:R} gave {got}");
            }
        }

        public void SavitzkyGolayKeepsAllNegativeZero()
        {
            var zeros = new double[9];
            for (int i = 0; i < zeros.Length; i++) zeros[i] = -0.0;
            foreach (int order in new[] { 0, 2, 4 })
                foreach (double v in SavitzkyGolay.Smooth(zeros, 5, order)) Check.True(v == 0 && 1 / v < 0, $"order {order}: {v:R}");
        }

        private static long Ulps(double a, double b) => Math.Abs(BitConverter.DoubleToInt64Bits(a) - BitConverter.DoubleToInt64Bits(b));

        public void DbConversionsKeepLargeExactDecibels()
        {
            // Correctly rounded references (mpmath, 60 digits).
            var amplitude = new (double Db, double Ref)[]
            {
                (6001, 1.1220184543019635e+300), (-6001, 8.912509381337455e-301), (1234.5, 5.308844442309883e+61),
                (5555, 5.623413251903491e+277), (41, 112.20184543019634), (6159, 8.912509381337455e+307),
            };
            foreach (var (db, r) in amplitude) Check.True(Ulps(LevelConversions.DbToAmplitude(db), r) <= 2, $"{db}: {LevelConversions.DbToAmplitude(db):R}");
            var power = new (double Db, double Ref)[] { (3001, 1.2589254117941672e+300), (-3001, 7.943282347242815e-301), (777, 5.011872336272722e+77) };
            foreach (var (db, r) in power) Check.True(Ulps(LevelConversions.DbToPower(db), r) <= 2, $"{db}: {LevelConversions.DbToPower(db):R}");
        }

        public void HampelFlagsAFiniteSampleAgainstAnInfiniteMedian()
        {
            double inf = double.PositiveInfinity;
            double[] x = { inf, inf, 5, inf, inf };
            Check.Equal(inf, Hampel.Filter(x, 2)[2]);
            Check.Equal(1, Hampel.CountOutliers(x, 2));
        }

        public void CrossCorrelationPeakValueSurvivesScaling()
        {
            var (lag, value) = TimeDelayEstimate.CrossCorrelationPeak(new[] { 1e200, 1e-200 }, new[] { -1e200, 1e-200 });
            Check.Equal(1, lag);
            Check.Equal(1e200 * 1e-200, value);
            (lag, value) = TimeDelayEstimate.CrossCorrelationPeak(new[] { 1e300, 3 }, new[] { 2, -1e300 });
            Check.Equal(-1, lag);
            Check.Equal(6.0, value);
        }

        public void DecimateNearOverflowMatchesTheInRangeSum()
        {
            Check.Equal(double.MaxValue, Resample.Decimate(new[] { double.MaxValue, double.MaxValue, double.MaxValue }, 3)[0]);
            var rng = new Random(19802);
            for (int trial = 0; trial < 2000; trial++)
            {
                int factor = rng.Next(2, 12);
                var x = new double[factor * 3];
                for (int i = 0; i < x.Length; i++) x[i] = (1 + rng.NextDouble()) * Math.Pow(2, 1022) * (rng.Next(4) == 0 ? -1 : 1);
                var small = new double[x.Length];
                for (int i = 0; i < x.Length; i++) small[i] = x[i] * Math.Pow(2, -100);
                var got = Resample.Decimate(x, factor);
                var reference = Resample.Decimate(small, factor);
                for (int i = 0; i < got.Length; i++) Check.Equal(reference[i] * Math.Pow(2, 100), got[i]);
            }
        }

        public void NearestBinMatchesTheExactMidpoints()
        {
            Check.Equal(1101, FrequencyGrid.NearestBin(3099.659649122807, 2850, 8020));
            var rng = new Random(19803);
            for (int trial = 0; trial < 20000; trial++)
            {
                int n = rng.Next(2, 5000);
                double fs = Math.Round(rng.NextDouble() * 10000, 2) + 1;
                // Near a midpoint (b + 1/2)*fs/N, nudged a few ulps either way.
                int b0 = rng.Next(0, n / 2);
                double f = (b0 + 0.5) * fs / n;
                long bits = BitConverter.DoubleToInt64Bits(f) + rng.Next(-3, 4);
                f = BitConverter.Int64BitsToDouble(bits);
                int got = FrequencyGrid.NearestBin(f, n, fs);
                int max = n / 2;
                // got is nearest: f*2N >= (2got - 1)fs unless 0, and f*2N < (2got + 1)fs unless max.
                bool ok = (got == 0 || Compare(f, 2L * n, fs, 2L * got - 1) >= 0) && (got == max || Compare(f, 2L * n, fs, 2L * got + 1) < 0);
                if (!ok) Check.True(false, $"f {f:R} N {n} fs {fs:R} gave {got}");
            }
        }

        public void ParabolicSurvivesAnOverflowingDenominator()
        {
            var big = PeakInterpolation.Parabolic(-0.9e308, 0.8e308, -0.85e308);
            var quarter = PeakInterpolation.Parabolic(-0.9e308 / 4, 0.8e308 / 4, -0.85e308 / 4);
            Check.Equal(quarter.Offset, big.Offset);
            Check.Equal(quarter.Peak * 4, big.Peak);
        }

        // Each sample reads as itself the first time and as 1e9 after that.
        private sealed class Flipping : IReadOnlyList<double>
        {
            private readonly double[] _x;
            private readonly bool[] _read;
            public Flipping(double[] x) { _x = x; _read = new bool[x.Length]; }
            public double this[int index] { get { bool again = _read[index]; _read[index] = true; return again ? 1e9 : _x[index]; } }
            public int Count => _x.Length;
            public IEnumerator<double> GetEnumerator() { for (int i = 0; i < _x.Length; i++) yield return this[i]; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public void MedianFilterReadsAChangingListOnce()
        {
            var x = new double[50];
            for (int i = 0; i < x.Length; i++) x[i] = i + 1;
            var r = MedianFilter.Apply(new Flipping(x), 5);
            var reference = MedianFilter.Apply(x, 5);
            for (int i = 0; i < x.Length; i++) Check.Equal(reference[i], r[i]);
        }
    }
}
