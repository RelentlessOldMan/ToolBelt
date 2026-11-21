using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 115 (signal analysis, seventh pass): each test failed before its fix, but the guards.
    public class SignalReview115Tests
    {
        // ---------- Goertzel ----------

        // Only an overflowing 2π·f/fs was reduced mod fs: 1e17 Hz at 1 Hz, a multiple of the rate, gave 1.14 for DC's 8.
        public void GoertzelFrequenciesFarAboveTheRateAreTheirAlias()
        {
            var ones = Enumerable.Repeat(1.0, 8).ToArray();
            foreach (var (f, fs) in new[] { (1e17, 1.0), (Math.Pow(2, 60), 4.0), (1e300, 1.0) })
            {
                var (mag, phase) = Goertzel.Estimate(ones, f, fs);
                Check.True(Math.Abs(mag - 8) < 1e-12 && Math.Abs(phase) < 1e-12, $"{f} Hz at {fs}: ({mag}, {phase})");
            }
            var rng = new Random(115);
            for (int trial = 0; trial < 200; trial++)
            {
                var s = Enumerable.Range(0, rng.Next(1, 40)).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
                double fs = Math.Pow(10, rng.NextDouble() * 6 - 3);
                double f = Math.Pow(10, 14 + rng.NextDouble() * 280) * (rng.Next(2) == 0 ? 1 : -1);
                var want = Goertzel.Estimate(s, f % fs, fs);
                var got = Goertzel.Estimate(s, f, fs);
                Check.True(Math.Abs(got.Magnitude - want.Magnitude) <= 1e-12 * (1 + want.Magnitude), $"trial {trial}: {got} vs {want}");
            }
        }

        // ---------- TimeDelayEstimate ----------

        // Past 64 lags tied at the peak the FFT's pick stood: [1] against 200 ones gave (7, 1.0000000000000002), not (0, 1).
        public void CrossCorrelationManyTiesGoToTheLagNearestZero()
        {
            Check.Equal((0, 1.0), TimeDelayEstimate.CrossCorrelationPeak(new[] { 1.0 }, Enumerable.Repeat(1.0, 200).ToArray()));
            var period = new[] { 0.0, 1, 2, 1 };
            var train = Enumerable.Range(0, 400).Select(i => period[i % 4]).ToArray();
            Check.Equal((0, 6.0), TimeDelayEstimate.CrossCorrelationPeak(period, train));
            // Ties too many to sum (13000 lags of 2000 samples): the one nearest zero, its value summed exactly.
            var comb = Enumerable.Range(0, 2000).Select(i => i % 2 == 0 ? 1.0 : 0).ToArray();
            var combs = Enumerable.Range(0, 30000).Select(i => i % 2 == 0 ? 1.0 : 0).ToArray();
            Check.Equal((0, 1000.0), TimeDelayEstimate.CrossCorrelationPeak(comb, combs));

            // Against exact integer correlation over periodic trains: the largest value, ties to the lag nearest zero
            // (the negative one first, as the search runs).
            var rng = new Random(1151);
            for (int trial = 0; trial < 150; trial++)
            {
                var tpl = Enumerable.Range(0, rng.Next(1, 6)).Select(_ => (double)rng.Next(-3, 4)).ToArray();
                if (tpl.All(v => v == 0)) tpl[0] = 1;
                int reps = rng.Next(20, 120), lead = rng.Next(0, 5);
                var delayed = Enumerable.Range(0, lead + reps * tpl.Length).Select(i => i < lead ? 0.0 : tpl[(i - lead) % tpl.Length]).ToArray();
                bool swap = rng.Next(2) == 0;
                double[] r = swap ? delayed : tpl, d = swap ? tpl : delayed;
                int bestLag = 0;
                long best = long.MinValue;
                for (int k = -(r.Length - 1); k <= d.Length - 1; k++)
                {
                    long sum = 0;
                    for (int l = Math.Max(0, -k); l < Math.Min(r.Length, d.Length - k); l++) sum += (long)r[l] * (long)d[l + k];
                    if (sum > best || (sum == best && Math.Abs(k) < Math.Abs(bestLag))) { best = sum; bestLag = k; }
                }
                Check.Equal((bestLag, (double)best), TimeDelayEstimate.CrossCorrelationPeak(r, d), $"trial {trial}");
            }
        }

        // ---------- CycleMeasurements ----------

        // 1/Period lost a representable frequency where the period in seconds overflowed: 10 samples at 1e-308 Hz gave 0.
        public void CycleFrequenciesSurviveExtremeRates()
        {
            var square = Enumerable.Range(0, 60).Select(i => i % 10 < 5 ? 0.0 : 1.0).ToArray();
            foreach (double fs in new[] { 1e-308, 1e-300, 1.0, 1e300 })
            {
                var stats = CycleMeasurements.Measure(square, fs);
                Check.True(stats.Cycles.Count >= 4, stats.Cycles.Count.ToString());
                foreach (var c in stats.Cycles) Check.Equal(fs / 10, c.Frequency, $"fs {fs}");
                Check.Equal(fs / 10, stats.MeanFrequency, $"fs {fs}");
            }
            Check.True(double.IsNaN(CycleMeasurements.Measure(new double[] { 0, 0, 0, 0 }, 1).MeanFrequency), "no cycles");
        }

        // ---------- Fft ----------

        // Bluestein's result was rounded to the subnormal grid and then multiplied by 1/N: −0 for −0.58ε at N = 6.
        public void FftInverseOfSubnormalsIsRoundedOnceAtEveryLength()
        {
            double eps = double.Epsilon, up = Math.Pow(2, 600);
            Check.Equal(-eps, Fft.Inverse(new Complex[] { 0, 7 * eps, 0, 0, 0, 0 })[2].Real);
            var rng = new Random(1152);
            int checkedValues = 0;
            foreach (int n in new[] { 3, 5, 6, 7, 10, 12, 15, 8, 16 })
                for (int trial = 0; trial < 60; trial++)
                {
                    var x = Enumerable.Range(0, n).Select(_ => new Complex(rng.Next(-20, 21) * eps, rng.Next(-20, 21) * eps)).ToArray();
                    var got = Fft.Inverse(x);
                    // The same transform on samples scaled up by 2^600 (exact), its result in units of ε: rounded once to the
                    // nearest integer it is the answer, except right at a halfway point, where the reference's own rounding decides.
                    var scaled = Fft.Inverse(x.Select(c => new Complex(c.Real * up, c.Imaginary * up)).ToArray());
                    for (int i = 0; i < n; i++)
                        foreach (var (g, s) in new[] { (got[i].Real, scaled[i].Real), (got[i].Imaginary, scaled[i].Imaginary) })
                        {
                            double units = s * Math.Pow(2, -600 + 1074);   // s·2^-600 in units of ε, without the subnormal rounding
                            if (Math.Abs(Math.Abs(units - Math.Floor(units)) - 0.5) < 1e-6) continue;
                            Check.Equal(Math.Round(units) * eps, g, $"n {n} trial {trial} i {i}");
                            checkedValues++;
                        }
                }
            Check.True(checkedValues > 1000, checkedValues.ToString());
        }

        // ---------- Hampel ----------

        // The MAD merged the window's deviations outward to the middle rank, O(w) a sample: 292 s for 400000 samples with a
        // half-window of 100000.
        public void HampelLargeWindowsTakeNearLinearTime()
        {
            var rng = new Random(1153);
            var x = Enumerable.Range(0, 100000).Select(_ => rng.NextDouble()).ToArray();
            for (int i = 0; i < x.Length; i += 997) x[i] = 50;
            var sw = Stopwatch.StartNew();
            int outliers = Hampel.CountOutliers(x, 25000);
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(6), $"{sw.Elapsed.TotalSeconds:F1} s");
            Check.Equal((x.Length + 996) / 997, outliers);
        }

        // Guard for the binary search: against a brute-force Hampel filter (window copied, sorted, medians taken), with
        // duplicates, signed zeros, infinities and NaN.
        public void HampelMatchesABruteForceFilter()
        {
            var rng = new Random(1154);
            var pool = new[] { 0.0, -0.0, 1, 1, 2, 3, -4, 7, 100, -100, 0.5, double.PositiveInfinity, double.NegativeInfinity, double.NaN };
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(1, 50), hw = rng.Next(1, 30);
                bool allowInf = rng.Next(4) == 0;
                var x = Enumerable.Range(0, n).Select(_ =>
                {
                    double v = pool[rng.Next(pool.Length)];
                    return !allowInf && double.IsInfinity(v) ? rng.Next(-9, 10) : v;
                }).ToArray();
                double sigmas = new[] { 0.0, 1, 3, 0.25 }[rng.Next(4)];
                var want = NaiveHampel(x, hw, sigmas);
                var got = Hampel.Filter(x, hw, sigmas);
                for (int i = 0; i < n; i++)
                    Check.True(want[i].Equals(got[i]) && (want[i] != 0 || 1 / want[i] == 1 / got[i]),
                        $"trial {trial} i {i}: {got[i]} for {want[i]} in [{string.Join(", ", x)}] hw {hw} nσ {sigmas}");
            }
        }

        private static double[] NaiveHampel(double[] x, int hw, double sigmas)
        {
            var result = (double[])x.Clone();
            for (int i = 0; i < x.Length; i++)
            {
                var w = new List<double>();
                for (int j = Math.Max(0, i - hw); j <= Math.Min(x.Length - 1, i + hw); j++) if (!double.IsNaN(x[j])) w.Add(x[j]);
                if (w.Count == 0) continue;
                w.Sort((a, b) => a < b ? -1 : a > b ? 1 : Negative(b) - Negative(a));   // −0 before +0, as the filter keeps them
                double median = Median(w);
                var dev = w.Select(v => v == median ? 0 : Math.Abs(v - median)).ToList();  // a value equal to an infinite median deviates by 0 (round 198)
                dev.Sort((a, b) => a.CompareTo(b));
                double mad = Median(dev);
                double d = Math.Abs(x[i] - median);
                bool outlier = double.IsNaN(x[i]) || (sigmas == 0 ? d > 0 : d > sigmas * (1.4826 * mad));
                if (outlier) result[i] = median;
            }
            return result;
        }

        private static int Negative(double v) => BitConverter.DoubleToInt64Bits(v) < 0 ? 1 : 0;

        private static double Median(List<double> s)
        {
            int c = s.Count;
            if (c % 2 != 0) return s[c / 2];
            double a = s[c / 2 - 1], b = s[c / 2];
            return double.IsInfinity(a) || double.IsInfinity(b) ? 0.5 * (a + b) : 0.5 * a + 0.5 * b;
        }

        // ---------- SavitzkyGolay ----------

        // Each edge position built and summed its own weight row, O(w²·m) for the two edges: 19.9 s at w = 40001.
        public void SavitzkyGolayWideWindowEdgesTakeLinearTime()
        {
            var rng = new Random(1155);
            var x = Enumerable.Range(0, 30001).Select(_ => rng.NextDouble()).ToArray();
            var sw = Stopwatch.StartNew();
            var y = SavitzkyGolay.Smooth(x, 30001, 3);
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"{sw.Elapsed.TotalSeconds:F1} s");
            Check.True(y.All(v => !double.IsNaN(v) && Math.Abs(v - 0.5) < 0.2), "smoothed values out of range");
        }

        // Guard for the projected edges: against a least-squares polynomial fit of each window by normal equations (fine at
        // these small orders), and polynomials passing through at extreme scales.
        public void SavitzkyGolayEdgesMatchAPolynomialFit()
        {
            var rng = new Random(1156);
            for (int trial = 0; trial < 300; trial++)
            {
                int w = 2 * rng.Next(1, 11) + 1, order = rng.Next(0, Math.Min(5, w));
                int n = w + rng.Next(0, 15);
                double scale = new[] { 1.0, 1e300, 1e-300, 1e-310 }[rng.Next(4)];
                var x = Enumerable.Range(0, n).Select(_ => (rng.NextDouble() * 2 - 1) * scale).ToArray();
                var got = SavitzkyGolay.Smooth(x, w, order);
                int half = w / 2;
                for (int i = 0; i < n; i++)
                {
                    int start = Math.Min(Math.Max(i - half, 0), n - w);
                    double want = FitAt(x, start, w, order, i - start, scale);
                    Check.True(Math.Abs(got[i] - want) <= 1e-9 * scale + 4 * double.Epsilon, $"trial {trial} i {i}: {got[i]} vs {want}");
                }
            }
            foreach (double scale in new[] { 1e300, 1e-300, 1.7e308 / 30 })
            {
                var p = Enumerable.Range(0, 30).Select(i => scale * (1 + 0.1 * i - 0.002 * i * i)).ToArray();
                var y = SavitzkyGolay.Smooth(p, 11, 2);
                for (int i = 0; i < p.Length; i++) Check.True(Math.Abs(y[i] - p[i]) <= 1e-12 * Math.Abs(p[i]), $"{scale} i {i}: {y[i]} vs {p[i]}");
            }
        }

        // The fitted polynomial's value at window position e, by normal equations on t in [−1, 1] with the data divided by scale.
        private static double FitAt(double[] x, int start, int w, int order, int e, double scale)
        {
            int m = order + 1, half = w / 2;
            var a = new double[m, m + 1];
            for (int k = 0; k < w; k++)
            {
                double t = (k - half) / (double)half, y = x[start + k] / scale;
                var pw = new double[2 * m];
                pw[0] = 1;
                for (int j = 1; j < 2 * m; j++) pw[j] = pw[j - 1] * t;
                for (int r = 0; r < m; r++)
                {
                    for (int c = 0; c < m; c++) a[r, c] += pw[r + c];
                    a[r, m] += pw[r] * y;
                }
            }
            for (int c = 0; c < m; c++)
            {
                int piv = c;
                for (int r = c + 1; r < m; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                for (int k = 0; k <= m; k++) { var tmp = a[c, k]; a[c, k] = a[piv, k]; a[piv, k] = tmp; }
                for (int r = 0; r < m; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c] / a[c, c];
                    for (int k = c; k <= m; k++) a[r, k] -= f * a[c, k];
                }
            }
            double te = (e - half) / (double)half, v = 0, p2 = 1;
            for (int j = 0; j < m; j++) { v += a[j, m] / a[j, j] * p2; p2 *= te; }
            return v * scale;
        }
    }
}
