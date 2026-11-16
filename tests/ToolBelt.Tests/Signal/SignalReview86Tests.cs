using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 86 (signal analysis, fifth pass): each test failed before its fix.
    public class SignalReview86Tests
    {
        private const double Eps = double.Epsilon;

        private static double Down(double x) => x == 0 ? -Eps : BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x > 0 ? -1 : 1));

        private static bool SameBits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        // ---------- Fft ----------

        // The power-of-two path never scaled huge inputs down (Bluestein did): [1e308, −1e308, 1e308, −1e308] had a NaN DC.
        public void PowerOfTwoTransformsOfHugeInputsStayFinite()
        {
            var f = Fft.Forward(new[] { 1e308, -1e308, 1e308, -1e308 });
            Check.Equal(0.0, f[0].Real); Check.Equal(0.0, f[0].Imaginary);
            Check.Equal(0.0, f[1].Real); Check.Equal(0.0, f[1].Imaginary);
            Check.Equal(0.0, f[3].Real); Check.Equal(0.0, f[3].Imaginary);
            Check.True(double.IsPositiveInfinity(f[2].Real), f[2].ToString());
            // Against a naive DFT of the input scaled down by 2^600 (exact), over random huge inputs.
            var rng = new Random(8601);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = 1 << rng.Next(1, 7);
                var x = Enumerable.Range(0, n).Select(_ => (rng.NextDouble() * 2 - 1) * 1.7e308 / n).ToArray();
                var got = Fft.Forward(x);
                double peak = 0;
                for (int k = 0; k < n; k++)
                {
                    Complex sum = 0;
                    for (int j = 0; j < n; j++) sum += x[j] * Math.Pow(2, -600) * Complex.FromPolarCoordinates(1, -2 * Math.PI * j * k / n);
                    peak = Math.Max(peak, sum.Magnitude);
                    var want = sum * Math.Pow(2, 600);
                    Check.True(Math.Abs(got[k].Real - want.Real) <= 1e-12 * 1.7e308 && Math.Abs(got[k].Imaginary - want.Imaginary) <= 1e-12 * 1.7e308,
                        $"trial {trial} bin {k}: {got[k]} vs {want}");
                }
            }
        }

        // Complex × double is a complex product: ∞ · (1/n + 0i) gave ∞ + NaN·i.
        public void InverseKeepsInfiniteOutputsReal()
        {
            var r = Fft.Inverse(new[] { new Complex(double.PositiveInfinity, 0) });
            Check.True(double.IsPositiveInfinity(r[0].Real), r[0].ToString());
            Check.Equal(0.0, r[0].Imaginary);
        }

        // ---------- PulseMeasurements ----------

        // Each high-reference crossing without a low one before it searched back to sample 0: O(n²).
        public void RiseTimeOverManyPartialEdgesIsLinear()
        {
            const int pairs = 400000;
            var s = new double[2 * pairs + pairs + 1000];
            for (int i = 0; i < pairs; i++) { s[2 * i] = 0.5; s[2 * i + 1] = 1; }
            for (int i = 3 * pairs; i < s.Length; i++) s[i] = 1;
            var watch = Stopwatch.StartNew();
            double rise = PulseMeasurements.RiseTime(s, 1);
            watch.Stop();
            Check.Close(0.8, rise, 1e-9);
            Check.True(watch.Elapsed.TotalSeconds < 2, watch.Elapsed.ToString());
        }

        // ---------- EnvelopeFollower ----------

        // Value + (g·level − g·Value) overflows once g > 0.5.
        public void EnvelopeFollowerOverflowFallbackStaysFinite()
        {
            var f = new EnvelopeFollower(1000, 0.0005, 1);                    // attack coefficient 1 − e^-2 ≈ 0.86
            f.Reset(-double.MaxValue);
            double v = f.Next(double.MaxValue);
            Check.False(double.IsInfinity(v), v.ToString("R"));
            Check.True(v > 0 && v < double.MaxValue, v.ToString("R"));
        }

        // ---------- Quantizer ----------

        // A subnormal step was rounded (6ε for 6.25ε), and so was every code; the top code's center passed max.
        public void QuantizerOfASubnormalRangeIsExact()
        {
            var q = new Quantizer(4, 0, 100 * Eps);
            foreach (int k in new[] { 6, 12, 48, 90, 0, 1, 99, 100, 7, 25 })
                Check.Equal(Math.Min(15, k * 16 / 100), q.Code(k * Eps), "k = " + k);
            var top = new Quantizer(4, 0, 122 * Eps);
            Check.True(top.Quantize(122 * Eps) <= 122 * Eps, top.Quantize(122 * Eps).ToString("R"));
            var rng = new Random(8602);
            for (int trial = 0; trial < 3000; trial++)
            {
                int bits = rng.Next(1, 8), lo = rng.Next(-1000, 1000), width = rng.Next(1, 5000), k = rng.Next(lo - 10, lo + width + 10);
                var qq = new Quantizer(bits, lo * Eps, (lo + width) * Eps);
                long want = (long)Math.Floor((double)((long)(k - lo) << bits) / width);
                want = Math.Max(0, Math.Min((1 << bits) - 1, want));
                Check.Equal((int)want, qq.Code(k * Eps), $"bits {bits} [{lo}, {lo + width}]ε at {k}ε");
                double c = qq.Quantize(k * Eps);
                Check.True(c >= lo * Eps && c <= (lo + width) * Eps, $"trial {trial}: {c:R}");
            }
        }

        // The quotient (value − min) / step was rounded before the floor: an ulp below 0.25 took code 5 at 3 bits over [−1, 1].
        public void QuantizerCodesAreExactAtBinEdges()
        {
            Check.Equal(4, new Quantizer(3, -1, 1).Code(Down(0.25)));
            var rng = new Random(8603);
            for (int trial = 0; trial < 20000; trial++)
            {
                int bits = rng.Next(1, 31);
                double min = Math.Round((rng.NextDouble() * 2 - 1) * 100, rng.Next(0, 4)), max = min + Math.Round(rng.NextDouble() * 100, rng.Next(0, 4)) + 1;
                var q = new Quantizer(bits, min, max);
                long c = rng.Next(1, 1 << Math.Min(bits, 20));
                double edge = min + c * ((max - min) / (1L << bits));
                foreach (double v in new[] { Down(edge), edge, -Down(-edge) })
                {
                    // Exact reference: (v − min)·2^bits against c·(max − min), in integers scaled by 2^1074.
                    var lhs = (Exact(v) - Exact(min)) << bits;
                    var range = Exact(max) - Exact(min);
                    long want = (long)BigInteger.Divide(lhs, range);
                    if (lhs.Sign < 0 && !(lhs % range).IsZero) want--;
                    want = Math.Max(0, Math.Min((1L << bits) - 1, want));
                    Check.Equal((int)want, q.Code(v), $"bits {bits} [{min:R}, {max:R}] at {v:R}");
                }
            }
        }

        private static BigInteger Exact(double x)
        {
            long b = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((b >> 52) & 0x7FF);
            long m = b & 0xFFFFFFFFFFFFFL;
            var r = e == 0 ? new BigInteger(m) : new BigInteger(m | 1L << 52) << (e - 1);
            return b < 0 ? -r : r;
        }

        // ---------- Spectrum ----------

        public void AmplitudeOfAHugeBinFits()
        {
            var a = Spectrum.Amplitude(new[] { new Complex(double.MaxValue, double.MaxValue), Complex.Zero });
            Check.False(double.IsInfinity(a[0]), "a bin of (Max, Max) has an amplitude of about 1.27e308");
            Check.Close(Math.Sqrt(2) * (double.MaxValue / 2), a[0], 1e293);
        }

        // ---------- MedianFilter and Hampel ----------

        // −0 and +0 compare equal, so a leaving −0 could remove a +0 still in the window: the median of {−1, +0, 1} was −0.
        public void MedianSignOfZeroFollowsTheWindow()
        {
            var r = MedianFilter.Apply(new[] { -0.0, -1, 0.0, 1, 0.0, 0.0 }, 3);
            Check.True(SameBits(0.0, r[2]), r[2].ToString("R"));
            var rng = new Random(8604);
            double[] values = { -0.0, 0.0, -1, 1 };
            for (int trial = 0; trial < 5000; trial++)
            {
                var x = Enumerable.Range(0, rng.Next(1, 12)).Select(_ => values[rng.Next(4)]).ToArray();
                int w = 2 * rng.Next(0, 4) + 1;
                var got = MedianFilter.Apply(x, w);
                for (int i = 0; i < x.Length; i++)
                {
                    int lo = Math.Max(0, i - w / 2), hi = Math.Min(x.Length - 1, i + w / 2);
                    var win = x.Skip(lo).Take(hi - lo + 1).ToList();
                    // The median's bits are decided only when the window is odd and its middle is one of the zeros.
                    if (win.Count % 2 == 0) continue;
                    var sorted = win.OrderBy(v => v).ThenBy(v => BitConverter.DoubleToInt64Bits(v) < 0 ? 0 : 1).ToList();
                    double m = sorted[sorted.Count / 2];
                    Check.True(SameBits(m, got[i]), $"trial {trial} at {i}: {got[i]:R}, want {m:R} in [{string.Join(", ", x.Select(v => v.ToString("R")))}] w {w}");
                }
            }
            // Hampel keeps the same sorted window and replaces a NaN by its median, so the median's bits show there.
            double[] withGaps = { -0.0, 0.0, -1, 1, double.NaN };
            for (int trial = 0; trial < 5000; trial++)
            {
                var x = Enumerable.Range(0, rng.Next(1, 14)).Select(_ => withGaps[rng.Next(5)]).ToArray();
                int h = rng.Next(1, 4);
                var got = Hampel.Filter(x, h);
                for (int i = 0; i < x.Length; i++)
                {
                    if (!double.IsNaN(x[i])) continue;
                    var win = x.Skip(Math.Max(0, i - h)).Take(Math.Min(x.Length - 1, i + h) - Math.Max(0, i - h) + 1).Where(v => !double.IsNaN(v)).ToList();
                    if (win.Count % 2 == 0) continue;
                    var sorted = win.OrderBy(v => v).ThenBy(v => BitConverter.DoubleToInt64Bits(v) < 0 ? 0 : 1).ToList();
                    double m = sorted[sorted.Count / 2];
                    Check.True(SameBits(m, got[i]), $"Hampel trial {trial} at {i}: {got[i]:R}, want {m:R} in [{string.Join(", ", x.Select(v => v.ToString("R")))}] h {h}");
                }
            }
        }

        // ---------- Hilbert ----------

        // A subnormal signal was not scaled up: [3, 0, −3, 0, 5, 7, −2]·ε gave −2ε at index 2 for −1.475ε.
        public void HilbertOfASubnormalSignalRoundsOnce()
        {
            var x = new[] { 3.0, 0, -3, 0, 5, 7, -2 };
            var got = Hilbert.Analytic(x.Select(v => v * Eps).ToArray());
            var big = Hilbert.Analytic(x.Select(v => v * Math.Pow(2, 1074 - 600)).ToArray());
            for (int i = 0; i < x.Length; i++)
            {
                Check.Equal(x[i] * Eps, got[i].Real);
                Check.Equal(big[i].Imaginary * Math.Pow(2, -774) * Math.Pow(2, -774), got[i].Imaginary, "at " + i);   // rounded once
            }
            Check.Equal(-Eps, got[2].Imaginary);
        }

        // The header named only Fft, but Analytic's phase calls PhaseUnwrap: copying the named files didn't compile.
        public void EverySignalFileNamesTheSignalTypesItUses()
        {
            string dir = FindSignalSource();
            var files = Directory.GetFiles(dir, "*.cs");
            var names = files.Select(f => Path.GetFileNameWithoutExtension(f)!).ToArray();
            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                string header = lines[0];
                string self = Path.GetFileNameWithoutExtension(file);
                string code = string.Join("\n", lines.Skip(1).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
                foreach (var other in names)
                    if (other != self && Regex.IsMatch(code, @"(?<![\w.])" + other + @"\.\w+\("))
                        Check.True(header.Contains(other), $"{self}.cs uses {other} but its header says: {header}");
            }
        }

        private static string FindSignalSource()
        {
            // The .NET Framework runner is built outside the repo, so the working directory is searched too.
            foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                {
                    string p = Path.Combine(d.FullName, "src", "ToolBelt", "Signal");
                    if (Directory.Exists(p)) return p;
                }
            throw new InvalidOperationException("src/ToolBelt/Signal not found above " + AppContext.BaseDirectory + " or " + Environment.CurrentDirectory);
        }

        // ---------- PhaseUnwrap ----------

        // delta + π was rounded before the floor: a jump just below 5π landed below −π ([0, 15.707963267948964] gave −π−).
        public void UnwrappedJumpsStayInsideMinusPiToPi()
        {
            var r = PhaseUnwrap.Unwrap(new[] { 0, 15.707963267948964 });
            Check.Close(15.707963267948964 - 4 * Math.PI, r[1], 1e-15);
            Check.True(r[1] > 0, r[1].ToString("R"));
            var rng = new Random(8605);
            for (int trial = 0; trial < 20000; trial++)
            {
                double jump = (2 * rng.Next(-50, 50) + 1) * Math.PI;
                for (int u = rng.Next(-6, 7); u != 0; u -= Math.Sign(u)) jump = u > 0 ? -Down(-jump) : Down(jump);
                var w = PhaseUnwrap.Unwrap(new[] { 0, jump });
                // Exact reference: the whole turns t of 2π (the double) leaving jump − t·2π in [−π, π], with π also the double;
                // a remainder of exactly ±π takes the jump's sign, as NumPy's does.
                BigInteger j = Exact(jump), pi = Exact(Math.PI);
                long t = (long)Math.Round(jump / (2 * Math.PI));
                while (j - t * 2 * pi > pi) t++;
                while (j - t * 2 * pi < -pi) t--;
                if (j - t * 2 * pi == pi && jump < 0) t++;
                else if (j - t * 2 * pi == -pi && jump > 0) t--;
                Check.Equal(t, (long)Math.Round((jump - w[1]) / (2 * Math.PI)), $"jump {jump:R} unwrapped to {w[1]:R}");
            }
        }

        // ---------- Window ----------

        // Each entry was divided by the length before the sum: [ε, ε] had coherent gain 0.
        public void CoherentGainOfASubnormalWindow()
        {
            Check.Equal(Eps, Window.CoherentGain(new[] { Eps, Eps }));
            Check.Equal(1e-320, Window.CoherentGain(Enumerable.Repeat(1e-320, 1000).ToArray()));
            Check.Equal(double.MaxValue, Window.CoherentGain(new[] { double.MaxValue, double.MaxValue }));
        }

        // ---------- CycleMeasurements ----------

        public void ShortCapturesAreCheckedForNonFiniteSamples()
        {
            Check.Equal("samples", Check.Throws<ArgumentException>(() => CycleMeasurements.Measure(new[] { double.NaN, double.NaN }, 1)).ParamName);
            Check.Equal("samples", Check.Throws<ArgumentException>(() => CycleMeasurements.Measure(new[] { double.PositiveInfinity }, 1)).ParamName);
            Check.Equal(0, CycleMeasurements.Measure(new[] { 1.0, 2 }, 1).Cycles.Count);
        }

        // ---------- Resample ----------

        // a·(1 − t) + b·t rippled a constant signal by an ulp.
        public void ResamplingAConstantSignalIsExact()
        {
            var rng = new Random(8606);
            for (int trial = 0; trial < 1000; trial++)
            {
                double c = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-10, 10));
                var x = Enumerable.Repeat(c, rng.Next(2, 40)).ToArray();
                foreach (double v in Resample.Linear(x, rng.Next(2, 100))) Check.Equal(c, v, "Linear, trial " + trial);
                foreach (double v in Resample.ToRate(x, 1000, 1000 * (0.3 + rng.NextDouble() * 3))) Check.Equal(c, v, "ToRate, trial " + trial);
            }
        }
    }
}
