using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 70 (signal analysis, fourth pass): each test failed before its fix.
    public sealed class SignalReview4Tests
    {
        private static void Relative(double expected, double actual, double tolerance, string? message = null)
            => Check.True(Math.Abs(actual - expected) <= tolerance * Math.Abs(expected), $"{message}: expected {expected:R}, got {actual:R}");

        // A read-only list of n copies of one value, so huge lengths cost nothing.
        private sealed class Repeat : IReadOnlyList<double>
        {
            private readonly double _value;
            public Repeat(int count, double value) { Count = count; _value = value; }
            public int Count { get; }
            public double this[int index] => _value;
            public IEnumerator<double> GetEnumerator() { for (int i = 0; i < Count; i++) yield return _value; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public void Goertzel_LargeSamplesDoNotOverflow()
        {
            // Differential: the magnitude scales linearly with the samples.
            var rng = new Random(70);
            for (int trial = 0; trial < 20; trial++)
            {
                int n = 200 + rng.Next(800);
                double f = rng.Next(3) == 0 ? 0 : rng.NextDouble() * 500;
                var unit = Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 2 - 1 + (f == 0 ? 1 : 0)).ToArray();
                double reference = Goertzel.Estimate(unit, f, 1000).Magnitude;
                double scale = 1e300;
                var big = unit.Select(v => v * scale).ToArray();
                Relative(reference * scale, Goertzel.Estimate(big, f, 1000).Magnitude, 1e-9, $"trial {trial}");
            }
            var dc = Enumerable.Repeat(1e303, 1000).ToArray();
            Relative(1e306, Goertzel.Estimate(dc, 0, 1000).Magnitude, 1e-9, "DC");
        }

        public void WelchPsd_ExtremeAmplitudesAndRates()
        {
            var rng = new Random(71);
            var unit = Enumerable.Range(0, 256).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
            var reference = WelchPsd.Estimate(unit, 1, 64).Psd;
            foreach (var (amplitude, rate) in new[] { (1e155, 1e10), (1e-170, 1e-100), (1e150, 1.0), (1e-160, 1.0) })
            {
                var psd = WelchPsd.Estimate(unit.Select(v => v * amplitude).ToArray(), rate, 64).Psd;
                for (int k = 0; k < psd.Length; k++)
                    Relative(reference[k] * (amplitude / rate) * amplitude, psd[k], 1e-9, $"({amplitude}, {rate}) bin {k}");
            }
        }

        public void Multitaper_ExtremeAmplitudesAndRates()
        {
            var rng = new Random(72);
            var unit = Enumerable.Range(0, 128).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
            var reference = Multitaper.Estimate(unit, 1).Psd;
            foreach (var (amplitude, rate) in new[] { (1e155, 1e10), (1e-170, 1e-100) })
            {
                var psd = Multitaper.Estimate(unit.Select(v => v * amplitude).ToArray(), rate).Psd;
                for (int k = 0; k < psd.Length; k++)
                    Relative(reference[k] * (amplitude / rate) * amplitude, psd[k], 1e-9, $"({amplitude}, {rate}) bin {k}");
            }
        }

        public void Fft_Radix2SubnormalInputsKeepTheirBits()
        {
            // Differential: the transform of a subnormal input is the transform of the same input scaled into the normal
            // range, scaled back; only the final rounding to the subnormal grid (half an ε) may differ.
            double e = double.Epsilon;
            var rng = new Random(85);
            for (int trial = 0; trial < 20; trial++)
            {
                int n = 8 << rng.Next(5);
                var input = Enumerable.Range(0, n).Select(_ => (rng.Next(2001) - 1000) * e).ToArray();
                var spectrum = Fft.Forward(input);
                var scaled = Fft.Forward(input.Select(v => v * Math.Pow(2, 1000)).ToArray());
                for (int k = 0; k < n; k++)
                {
                    Check.Close(scaled[k].Real * Math.Pow(2, -1000), spectrum[k].Real, 0.51 * e, $"trial {trial} bin {k} re");
                    Check.Close(scaled[k].Imaginary * Math.Pow(2, -1000), spectrum[k].Imaginary, 0.51 * e, $"trial {trial} bin {k} im");
                }
            }
        }

        public void Fft_LengthIsCheckedBeforeAllocating()
        {
            // An input of 2^29 + 1 is itself 4-8 GB, so the check the three entry points now make first is exercised directly.
            var check = typeof(Fft).GetMethod("CheckLength", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Check.NotNull(check);
            var ex = Check.Throws<System.Reflection.TargetInvocationException>(() => check!.Invoke(null, new object[] { (1 << 29) + 1, "real" }));
            Check.Equal("real", (ex.InnerException as ArgumentException)?.ParamName);
            check!.Invoke(null, new object[] { 1 << 29, "real" });            // a power of two is fine
            check.Invoke(null, new object[] { (1 << 29) - 1, "real" });
        }

        public void Cycles_StatisticsAtExtremeRates()
        {
            // Square wave with alternating periods of 4 and 6 samples.
            var s = new List<double>();
            for (int c = 0; c < 10; c++) { int p = c % 2 == 0 ? 4 : 6; for (int i = 0; i < p; i++) s.Add(i < p / 2 ? 1 : -1); }
            s.Add(1);
            var reference = CycleMeasurements.Measure(s, 1);
            Check.True(reference.PeriodStandardDeviation > 0);
            foreach (double rate in new[] { 1e200, 1e-306, 1e300 })
            {
                var stats = CycleMeasurements.Measure(s, rate);
                Relative(reference.MeanPeriod / rate, stats.MeanPeriod, 1e-12, $"{rate} mean");
                Relative(reference.PeriodStandardDeviation / rate, stats.PeriodStandardDeviation, 1e-12, $"{rate} sd");
                Relative(reference.CycleToCycleJitter / rate, stats.CycleToCycleJitter, 1e-12, $"{rate} c2c");
                Relative(reference.PeakToPeakJitter / rate, stats.PeakToPeakJitter, 1e-12, $"{rate} p2p");
            }
        }

        public void Cycles_SubnormalCaptureMeasuresLikeAScaledOne()
        {
            double e = double.Epsilon;
            var rng = new Random(73);
            for (int trial = 0; trial < 20; trial++)
            {
                var unit = new List<double>();
                for (int c = 0; c < 6; c++)
                {
                    int p = 4 + rng.Next(6);
                    for (int i = 0; i < p; i++) unit.Add(i < p / 2 ? 1 + rng.Next(3) : -1 - rng.Next(3));
                }
                unit.Add(2);
                var tiny = unit.Select(v => v * e).ToList();
                var reference = CycleMeasurements.Measure(unit, 1);
                var stats = CycleMeasurements.Measure(tiny, 1);
                Check.Equal(reference.Cycles.Count, stats.Cycles.Count, $"trial {trial} count");
                Check.Close(reference.Level * e, stats.Level, e, $"trial {trial} level");
                for (int i = 0; i < stats.Cycles.Count; i++)
                {
                    Check.Close(reference.Cycles[i].Period, stats.Cycles[i].Period, 1e-12, $"trial {trial} cycle {i}");
                    Check.Equal(reference.Cycles[i].Maximum * e, stats.Cycles[i].Maximum);
                }
            }
            // ±ε: the halved differences round to 0, which gave 0/0 crossings and no cycles.
            var pm = Enumerable.Range(0, 21).Select(i => (i / 2) % 2 == 0 ? e : -e).ToList();
            Check.True(CycleMeasurements.Measure(pm, 1).Cycles.Count > 0);
        }

        public void Pulse_RiseTimeSkipsAPartialFirstEdge()
        {
            // Starts at 50% (mid-edge), goes high, falls, then a full rising edge 0 → 1 over 4 samples.
            var s = new double[] { 0.5, 1, 1, 1, 0, 0, 0, 0, 0.25, 0.5, 0.75, 1, 1, 1, 1 };
            Check.Close(3.2, PulseMeasurements.RiseTime(s, 1), 1e-9);
            var f = s.Select(v => 1 - v).ToArray();
            Check.Close(3.2, PulseMeasurements.FallTime(f, 1), 1e-9);
        }

        public void Pulse_SubnormalRecordMeasuresLikeAScaledOne()
        {
            double e = double.Epsilon;
            var rng = new Random(74);
            for (int trial = 0; trial < 20; trial++)
            {
                var unit = new List<double>();
                for (int i = 0; i < 5; i++) unit.Add(0);
                int up = 2 + rng.Next(4);
                for (int i = 1; i <= up; i++) unit.Add(30.0 * i / up);
                for (int i = 0; i < 4 + rng.Next(5); i++) unit.Add(30);
                int down = 2 + rng.Next(4);
                for (int i = 1; i <= down; i++) unit.Add(30 - 30.0 * i / down);
                for (int i = 0; i < 5; i++) unit.Add(0);
                var u = unit.Select(v => Math.Round(v)).ToArray();
                var t = u.Select(v => v * e).ToArray();
                Check.Close(PulseMeasurements.RiseTime(u, 1), PulseMeasurements.RiseTime(t, 1), 1e-12, $"trial {trial} rise");
                Check.Close(PulseMeasurements.FallTime(u, 1), PulseMeasurements.FallTime(t, 1), 1e-12, $"trial {trial} fall");
                Check.Close(PulseMeasurements.PulseWidth(u, 1), PulseMeasurements.PulseWidth(t, 1), 1e-12, $"trial {trial} width");
                Check.Close(PulseMeasurements.DutyCycle(u), PulseMeasurements.DutyCycle(t), 1e-12, $"trial {trial} duty");
                var (lo, hi) = PulseMeasurements.StateLevels(u);
                var (tl, th) = PulseMeasurements.StateLevels(t);
                Check.Close(lo * e, tl, e, $"trial {trial} low");
                Check.Close(hi * e, th, e, $"trial {trial} high");
            }
            Check.Equal("samples", Check.Throws<ArgumentNullException>(() => PulseMeasurements.RiseTime(null!, 1)).ParamName);
        }

        // NumPy's unwrap with discont (period 2π): correct where |Δ| ≥ discont, into [−π, π) with +π kept as +π.
        private static double[] NumpyUnwrap(double[] p, double discont)
        {
            var result = (double[])p.Clone();
            double correction = 0;
            for (int i = 1; i < p.Length; i++)
            {
                double d = p[i] - p[i - 1];
                double dd = ((d + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI;
                if (dd == -Math.PI && d > 0) dd = Math.PI;
                double c = dd - d;
                if (Math.Abs(d) < Math.Max(discont, Math.PI)) c = 0;
                correction += c;
                result[i] = p[i] + correction;
            }
            return result;
        }

        public void Unwrap_ToleranceAbovePiMatchesNumPy()
        {
            Check.Close(10 - 4 * Math.PI, PhaseUnwrap.Unwrap(new[] { 0.0, 10 }, 4)[1], 1e-12);
            var rng = new Random(75);
            for (int trial = 0; trial < 200; trial++)
            {
                var p = Enumerable.Range(0, 30).Select(_ => (rng.NextDouble() * 2 - 1) * 20).ToArray();
                double tol = Math.PI + rng.NextDouble() * 5;
                var expected = NumpyUnwrap(p, tol);
                var actual = PhaseUnwrap.Unwrap(p, tol);
                for (int i = 0; i < p.Length; i++) Check.Close(expected[i], actual[i], 1e-9, $"trial {trial} [{i}]");
            }
        }

        public void GroupDelay_HugeFrequencySpanDoesNotGiveZero()
        {
            var tau = PhaseUnwrap.GroupDelay(new[] { 0.0, -1, -2 }, new[] { -1e308, 0, 1e308 });
            foreach (double t in tau) Relative(1 / (2 * Math.PI) / 1e308, t, 1e-12);
            var tau2 = PhaseUnwrap.GroupDelay(new[] { 0.0, -1 }, new[] { 0, 1e308 });
            Relative(1 / (2 * Math.PI) / 1e308, tau2[0], 1e-12, "2π·Δf overflow");
        }

        public void Convolve_HugeValuesThatCancelAreRepresentable()
        {
            var r = Convolution.Convolve(new[] { 1e200, 1e200 }, new[] { 1e200, -1e200 });
            Check.True(double.IsPositiveInfinity(r[0]) && double.IsNegativeInfinity(r[2]));
            Check.Equal(0.0, r[1]);
            var rng = new Random(76);
            for (int trial = 0; trial < 20; trial++)
            {
                var a = Enumerable.Range(0, 1 + rng.Next(8)).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
                var b = Enumerable.Range(0, 1 + rng.Next(8)).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
                var reference = Convolution.Convolve(a, b);
                var big = Convolution.Convolve(a.Select(v => v * 1e160).ToArray(), b.Select(v => v * 1e147).ToArray());
                for (int k = 0; k < big.Length; k++) Relative(reference[k] * 1e160 * 1e147, big[k], 1e-9, $"trial {trial} [{k}]");
            }
        }

        public void Convolve_TooLongResultIsAnArgumentError()
        {
            var a = new Repeat(int.MaxValue, 1);
            var b = new Repeat(int.MaxValue, 1);
            Check.Equal("b", Check.Throws<ArgumentException>(() => Convolution.Convolve(a, b)).ParamName);
            Check.Equal("b", Check.Throws<ArgumentException>(() => Convolution.CrossCorrelate(a, b)).ParamName);
        }

        public void SavitzkyGolay_LargeConstantPassesThrough()
        {
            var data = Enumerable.Repeat(1.7e308, 9).ToArray();
            var smoothed = SavitzkyGolay.Smooth(data, 5, 2);
            foreach (double v in smoothed) Relative(1.7e308, v, 1e-12);
            var rng = new Random(77);
            var unit = Enumerable.Range(0, 30).Select(_ => rng.NextDouble()).ToArray();
            var reference = SavitzkyGolay.Smooth(unit, 7, 3);
            var big = SavitzkyGolay.Smooth(unit.Select(v => v * 1.5e308).ToArray(), 7, 3);
            for (int i = 0; i < big.Length; i++)
                if (Math.Abs(reference[i]) < 1.1) Check.Close(reference[i] * 1.5e308, big[i], 1e-9 * 1.5e308, $"[{i}]");
        }

        public void Quantizer_FullDoubleRange()
        {
            var q = new Quantizer(8, -1e308, 1e308);
            Check.Equal(0, q.Code(-1e308));
            Check.Equal(255, q.Code(1e308));
            Check.Equal(128, q.Code(1));
            Check.Equal(64, q.Code(-0.5e308));
            Check.Equal(192, q.Code(0.5e308));
            // Reconstruction too: the top bin's center of ±double.MaxValue at one bit is MaxValue / 2, not ∞.
            var one = new Quantizer(1, -double.MaxValue, double.MaxValue);
            Relative(double.MaxValue / 2, one.Quantize(double.MaxValue), 1e-15, "top");
            Relative(-double.MaxValue / 2, one.Quantize(-double.MaxValue), 1e-15, "bottom");
            var rng = new Random(84);
            var unit = new Quantizer(8, -1, 1);
            for (int i = 0; i < 100; i++)
            {
                double v = rng.NextDouble() * 2 - 1;
                Check.Equal(unit.Code(v), q.Code(v * 1e308), $"{v}");
                Relative(unit.Quantize(v) * 1e308, q.Quantize(v * 1e308), 1e-12, $"{v}");
            }
        }

        public void Resample_ToRatePastArrayMaxLengthIsAnArgumentError()
        {
            var samples = new double[] { 0, 1 };
            // Target length about 2·(0x7FFFFFC7 + 16)/2: past Array.MaxLength but within int.
            double target = (0x7FFFFFC7 + 20.0) / 2;
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Resample.ToRate(samples, 1, target));
            Check.Equal("targetRate", ex.ParamName);
        }

        public void Resample_DecimateRetriesNaNFromOverflow()
        {
            // MaxValue + MaxValue overflowed to ∞, and ∞ + (−∞) is NaN; the mean of the block is −∞.
            var d = Resample.Decimate(new[] { double.MaxValue, double.MaxValue, double.NegativeInfinity }, 3);
            Check.Equal(double.NegativeInfinity, d[0]);
        }

        public void Resample_LinearEndsAtTheLastInput()
        {
            for (int n = 2; n < 200; n++)
            {
                Check.Equal(1.0, Resample.Linear(new[] { 0.0, 1 }, n).Last(), $"n={n}");
                Check.Equal(0.3, Resample.Linear(new[] { 0.1, 0.7, 0.3 }, n).Last(), $"n={n}");
            }
            Check.Equal(1.0, Resample.Linear(new[] { double.NaN, 1 }, 50).Last());
        }

        private static double[] NaiveMedian(double[] s, int w)
        {
            int half = w / 2;
            var r = new double[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                var win = s.Skip(Math.Max(0, i - half)).Take(Math.Min(s.Length - 1, i + half) - Math.Max(0, i - half) + 1).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
                r[i] = win.Length == 0 ? double.NaN : win.Length % 2 == 1 ? win[win.Length / 2] : win[win.Length / 2 - 1] / 2 + win[win.Length / 2] / 2;
            }
            return r;
        }

        public void MedianFilter_SlidingWindowMatchesNaive()
        {
            var rng = new Random(78);
            for (int trial = 0; trial < 200; trial++)
            {
                var s = Enumerable.Range(0, 1 + rng.Next(40)).Select(_ => rng.Next(10) == 0 ? double.NaN : rng.Next(6) - 2.5).ToArray();
                int w = 1 + 2 * rng.Next(12);
                var expected = NaiveMedian(s, w);
                var actual = MedianFilter.Apply(s, w);
                for (int i = 0; i < s.Length; i++) Check.Close(expected[i], actual[i], 0, $"trial {trial} [{i}]");
            }
        }

        public void MedianFilter_WideWindowIsNotQuadratic()
        {
            var rng = new Random(79);
            var s = Enumerable.Range(0, 20000).Select(_ => rng.NextDouble()).ToArray();
            var sw = Stopwatch.StartNew();
            MedianFilter.Apply(s, 20001);
            Check.True(sw.Elapsed.TotalSeconds < 3, $"{sw.Elapsed.TotalSeconds} s");
        }

        private static double[] NaiveHampel(double[] s, int k, double n)
        {
            var r = (double[])s.Clone();
            for (int i = 0; i < s.Length; i++)
            {
                var win = s.Skip(Math.Max(0, i - k)).Take(Math.Min(s.Length - 1, i + k) - Math.Max(0, i - k) + 1).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
                if (win.Length == 0) continue;
                double Med(double[] x) => x.Length % 2 == 1 ? x[x.Length / 2] : x[x.Length / 2 - 1] / 2 + x[x.Length / 2] / 2;
                double med = Med(win);
                double mad = Med(win.Select(v => Math.Abs(v - med)).OrderBy(v => v).ToArray());
                if (double.IsNaN(s[i]) || Math.Abs(s[i] - med) > n * 1.4826 * mad) r[i] = med;
            }
            return r;
        }

        public void Hampel_SlidingWindowMatchesNaive()
        {
            var rng = new Random(80);
            for (int trial = 0; trial < 200; trial++)
            {
                var s = Enumerable.Range(0, 1 + rng.Next(40)).Select(_ => rng.Next(12) == 0 ? double.NaN : rng.Next(8) == 0 ? 50.0 * rng.Next(3) : rng.Next(6) - 2.5).ToArray();
                int k = 1 + rng.Next(8);
                double n = 1 + rng.NextDouble() * 2;
                var expected = NaiveHampel(s, k, n);
                var actual = Hampel.Filter(s, k, n);
                for (int i = 0; i < s.Length; i++) Check.Close(expected[i], actual[i], 1e-12, $"trial {trial} [{i}]");
            }
        }

        public void Hampel_WideWindowIsNotQuadratic()
        {
            var rng = new Random(81);
            var s = Enumerable.Range(0, 20000).Select(_ => rng.NextDouble()).ToArray();
            var sw = Stopwatch.StartNew();
            Hampel.Filter(s, 10000);
            Check.True(sw.Elapsed.TotalSeconds < 3, $"{sw.Elapsed.TotalSeconds} s");
        }

        public void TimeDelay_PeakValueDoesNotOverflowForOppositeScales()
        {
            var rng = new Random(82);
            var a = Enumerable.Range(0, 32).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
            var b = a.Select(v => v).ToArray();
            var reference = TimeDelayEstimate.CrossCorrelationPeak(a, b);
            var big = TimeDelayEstimate.CrossCorrelationPeak(a.Select(v => v * 1e308).ToArray(), b.Select(v => v * 1e-300).ToArray());
            Check.Equal(reference.Lag, big.Lag);
            Relative(reference.Value * 1e8, big.Value, 1e-9);
        }

        public void Hilbert_RealPartIsTheInput()
        {
            var analytic = Hilbert.Analytic(new[] { double.MaxValue, 0, 0 });
            Check.Equal(double.MaxValue, analytic[0].Real);
            Check.False(double.IsInfinity(Hilbert.Envelope(new[] { double.MaxValue, 0, 0 })[0]));
            var rng = new Random(83);
            var s = Enumerable.Range(0, 33).Select(_ => rng.NextDouble()).ToArray();
            var a = Hilbert.Analytic(s);
            for (int i = 0; i < s.Length; i++) Check.Equal(s[i], a[i].Real);
        }

        public void EnvelopeFollower_LongTimeConstantStillMoves()
        {
            // τ·fs = 1e17: the pole rounded to 1 and the envelope stayed at 0.
            var f = new EnvelopeFollower(1e17, 1, 1);
            double v = 0;
            for (int i = 0; i < 1000; i++) v = f.Next(1);
            Relative(1000 / 1e17, v, 1e-6);
            // A moderate one matches the closed form 1 − a^n.
            var g = new EnvelopeFollower(1000, 0.01, 0.01);
            double a = Math.Exp(-1 / (0.01 * 1000));
            for (int n = 1; n <= 50; n++) Check.Close(1 - Math.Pow(a, n), g.Next(1), 1e-12, $"n={n}");
        }

        public void SpectralPeak_UndefinedMethodIsRefused()
        {
            var mag = new[] { 0.1, 0.5, 1, 0.5, 0.1 };
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => PeakInterpolation.SpectralPeak(mag, 100, 8, (PeakMethod)99));
            Check.Equal("method", ex.ParamName);
        }
    }
}
