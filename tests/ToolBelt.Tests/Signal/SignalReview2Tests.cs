using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 31 (signal analysis, second pass).
    public sealed class SignalReview2Tests
    {
        private const double M = double.MaxValue;

        private static string ParamOf(Action action)
        {
            try { action(); }
            catch (ArgumentException ex) { return ex.ParamName ?? "(null)"; }
            catch (Exception ex) { return ex.GetType().Name; }
            return "(no throw)";
        }

        // ---------- PulseMeasurements ----------

        // A second-order step response (about 20% overshoot) up at sample 20 and down at sample 100.
        private static double[] RingingPulse()
        {
            static double Ring(double t) => t < 0 ? 0 : 1 - Math.Exp(-0.25 * t) * (Math.Cos(0.5 * t) + 0.5 * Math.Sin(0.5 * t));
            var p = new double[200];
            for (int i = 0; i < 200; i++) p[i] = Ring(i - 20) - Ring(i - 100);
            return p;
        }

        public void Pulse_RingingEdgesUseTheSettledStates()
        {
            var p = RingingPulse();
            var (lo, hi) = PulseMeasurements.StateLevels(p);
            Check.Close(0.0, lo, 0.02);
            Check.Close(1.0, hi, 0.02);                                               // was the 1.205 overshoot peak
            double rise = PulseMeasurements.RiseTime(p, 1);                           // threw
            double fall = PulseMeasurements.FallTime(p, 1);                           // was 75
            Check.True(rise > 2 && rise < 6, rise.ToString("R"));
            Check.True(fall > 2 && fall < 6, fall.ToString("R"));
        }

        public void Pulse_ReferencesPairOnTheSameEdge()
            => Check.Close(1.6, PulseMeasurements.RiseTime(new double[] { 0, 0, 0.2, 0, 0, 0, 0.5, 1, 1 }, 1), 1e-12);   // was 5.3

        public void Pulse_ZeroAndFullReferencesAreReachable()
        {
            var up = new double[] { 0, 0, 0, 0.25, 0.5, 0.75, 1, 1, 1 };
            Check.Close(4.0, PulseMeasurements.RiseTime(up, 1, 0.0, 1.0), 1e-12);    // threw
            Check.Close(4.0, PulseMeasurements.FallTime(Enumerable.Reverse(up).ToArray(), 1, 0.0, 1.0), 1e-12);
        }

        public void Pulse_NonFiniteSamplesAreRejectedWherever()
        {
            Check.Throws<ArgumentException>(() => PulseMeasurements.StateLevels(new[] { double.NaN, 0, 1, 1, 0 }));   // was (NaN, NaN)
            Check.Throws<ArgumentException>(() => PulseMeasurements.StateLevels(new[] { 0, double.NaN, 1, 1, 0 }));   // was (0, 1)
            Check.Throws<ArgumentException>(() => PulseMeasurements.DutyCycle(new[] { 0, 1, double.PositiveInfinity }));
        }

        public void Pulse_HugeLevelsDoNotOverflow()
        {
            Check.Close(0.5, PulseMeasurements.DutyCycle(new[] { 1e308, 1.7e308, 1.7e308, 1e308 }), 1e-12);        // was 0
            Check.Close(0.8, PulseMeasurements.RiseTime(new[] { -1e308, -1e308, 1e308, 1e308 }, 1), 1e-9);         // threw
            Check.Close(2.0, PulseMeasurements.PulseWidth(new[] { -M, -M, M, M, -M, -M }, 1), 1e-9);
        }

        // ---------- ZeroCrossing ----------

        public void ZeroCrossing_ARunOfZerosCountsOnce()
        {
            Check.Equal("0.5,4", string.Join(",", ZeroCrossing.Find(new double[] { 1, -1, 0, 0, 0, 0, 0, 1 })));      // 0.5,2,3,4,5,6
            Check.Equal(0, ZeroCrossing.Find(new double[] { 1, 0, 1 }).Count);                                           // a touch
            Check.Equal(0, ZeroCrossing.Find(new double[1000]).Count);                                                   // silence
            Check.Equal("1", string.Join(",", ZeroCrossing.Find(new double[] { 1, 0, -1 })));
            Check.Equal("0", string.Join(",", ZeroCrossing.Find(new double[] { 0, 1, 2 })));                            // sin(0) start
        }

        public void ZeroCrossing_HugeOrInfiniteSamples()
        {
            Check.Equal("0.5", string.Join(",", ZeroCrossing.Find(new[] { 1e308, -1e308 })));                           // was 0
            Check.Equal(0, ZeroCrossing.Find(new[] { double.PositiveInfinity, -1 }).Count);                            // was NaN
        }

        // ---------- MedianFilter / Hampel ----------

        public void Median_NaNIsLeftOut()
        {
            var y = MedianFilter.Apply(new[] { 1.0, 2, 3, double.NaN, 5, 6, 7 }, 3);
            Check.Equal(2.5, y[2]);                                                   // was 2
            Check.Equal(4.0, y[3]);                                                   // was 3
            Check.Equal(5.5, y[4]);                                                   // was 5
            Check.Equal(1.0, MedianFilter.Apply(new[] { 1.0, double.NaN, 3, 4, 5 }, 3)[0]);   // was NaN
            Check.True(double.IsNaN(MedianFilter.Apply(new[] { double.NaN, double.NaN, double.NaN }, 3)[1]));
        }

        public void Median_EvenPairNearMaxValue()
        {
            Check.Equal(M, MedianFilter.Apply(new[] { M, M, M }, 3)[0]);             // was ∞
            Check.Close(1.55e308, Hampel.Filter(new[] { double.NaN, 1.5e308, 1.6e308 }, 2)[0], 1e294);   // was ∞
        }

        // ---------- Window ----------

        public void Window_DegenerateInputs()
        {
            Check.Throws<ArgumentException>(() => Window.EquivalentNoiseBandwidth(Window.Create(WindowType.Hann, 2)));   // NaN
            Check.Equal(M, Window.CoherentGain(new[] { M, M }));                                                         // ∞
            Check.Close(1.5, Window.EquivalentNoiseBandwidth(Window.Create(WindowType.Hann, 1001)), 1e-2);
            Check.Equal("type", ParamOf(() => Window.Create((WindowType)99, 1)));                                        // [1]
            Check.Equal("window", ParamOf(() => WelchPsd.Estimate(new double[16], 1, 8, window: (WindowType)99)));      // "type"
        }

        // ---------- SavitzkyGolay ----------

        public void SavitzkyGolay_LargeWindowIsNotQuadraticInMemory()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var y = SavitzkyGolay.Smooth(new double[6001], 6001, 2);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 100_000_000, $"{allocated / 1_000_000} MB");     // ~288 MB of kept rows
            Check.Equal(0.0, y.Max());
        }

        public void SavitzkyGolay_EdgesStillFitThePolynomial()
        {
            double[] x = Enumerable.Range(0, 40).Select(i => 0.5 * i * i - 3 * i + 1.0).ToArray();
            var y = SavitzkyGolay.Smooth(x, 11, 2);
            for (int i = 0; i < x.Length; i++) Check.Close(x[i], y[i], 1e-9);
        }

        // ---------- TimeDelayEstimate ----------

        private static (double[] A, double[] B) Delayed(double scale)
        {
            var rng = new Random(7);
            double[] a = Enumerable.Range(0, 64).Select(_ => rng.NextDouble() - 0.5).ToArray();
            double[] b = new double[64];
            for (int i = 5; i < 64; i++) b[i] = a[i - 5];
            return (a.Select(v => v * scale).ToArray(), b.Select(v => v * scale).ToArray());
        }

        public void Delay_IsScaleFree()
        {
            foreach (double s in new[] { 1e-200, 1e-170, 1, 1e154, 1e300 })
            {
                var (a, b) = Delayed(s);
                Check.Equal(5, TimeDelayEstimate.GccPhat(a, b));                     // 0 from 1e154 up and 1e-165 down
                Check.Equal(5, TimeDelayEstimate.CrossCorrelationLag(a, b));
            }
            var (a1, b1) = Delayed(1);
            var (a2, b2) = Delayed(1e100);
            Check.Close(TimeDelayEstimate.CrossCorrelationPeak(a1, b1).Value * 1e200, TimeDelayEstimate.CrossCorrelationPeak(a2, b2).Value, 1e188);
        }

        public void Delay_EmptyInputNamesTheParameter()
            => Check.Equal("reference", ParamOf(() => TimeDelayEstimate.CrossCorrelationPeak(Array.Empty<double>(), new double[] { 1 })));

        // ---------- Resample ----------

        public void Resample_Overflows()
        {
            Check.Equal("1", string.Join(",", Resample.ToRate(new double[] { 1, 2, 3 }, 1e300, 1e-10)));   // IndexOutOfRange
            Check.Equal("1", string.Join(",", Resample.ToRate(new double[] { 1, 2, 3 }, 1, 5e-324)));
            Check.Equal(1e308, Resample.Decimate(new[] { 1e308, 1e308 }, 2)[0]);                            // ∞
        }

        // ---------- Quantizer / CycleMeasurements ----------

        public void Quantizer_RangeMustBeRepresentable()
        {
            Check.Equal("min", ParamOf(() => new Quantizer(8, double.NegativeInfinity, 1)));
            Check.Equal("max", ParamOf(() => new Quantizer(8, 0, double.PositiveInfinity)));
            Check.Equal("max", ParamOf(() => new Quantizer(8, -1e308, 1e308)));                          // step ∞
            Check.Equal("max", ParamOf(() => new Quantizer(8, 0, 5e-324)));                               // step 0
            Check.Equal(255, new Quantizer(8, -1e307, 1e307).Code(1e307));
        }

        public void Cycles_HugeLevelsDoNotOverflow()
        {
            double[] square = Enumerable.Range(0, 100).Select(i => (i / 5) % 2 == 0 ? 1e308 : 1.7e308).ToArray();
            Check.Equal(9, CycleMeasurements.Measure(square, 1).Cycles.Count);       // 0
            double[] bipolar = Enumerable.Range(0, 100).Select(i => (i / 5) % 2 == 0 ? -1e308 : 1e308).ToArray();
            Check.Equal(9, CycleMeasurements.Measure(bipolar, 1).Cycles.Count);
            Check.Equal(9, CycleMeasurements.Measure(bipolar, 1, hysteresis: 0).Cycles.Count);
        }

        // ---------- PhaseUnwrap ----------

        public void Unwrap_AnOverflowingJumpIsReported()
        {
            Check.Equal("phase", ParamOf(() => PhaseUnwrap.Unwrap(new[] { -1.7e308, 1.7e308, 0, 1 })));   // −∞ from there on
            Check.Equal("frequencies", ParamOf(() => PhaseUnwrap.GroupDelay(new double[] { 0, 1 }, new double[] { 1, 2, 3 })));
        }

        // ---------- argument checks and scaling ----------

        public void Arguments_NonFiniteAreRejected()
        {
            Check.Equal("targetFrequency", ParamOf(() => Goertzel.Estimate(new double[] { 1, 2 }, double.NaN, 100)));
            Check.Equal("targetFrequency", ParamOf(() => Goertzel.Estimate(new double[] { 1, 2 }, double.PositiveInfinity, 100)));
            Check.Equal("ratio", ParamOf(() => LevelConversions.AmplitudeToDb(double.NaN)));
            Check.Equal("ratio", ParamOf(() => LevelConversions.PowerToDb(double.NaN)));
            Check.Equal("sampleRate", ParamOf(() => PeakInterpolation.SpectralPeak(new double[] { 0, 1, 0 }, double.PositiveInfinity, 4)));
            Check.Equal("floor", ParamOf(() => Spectrum.AmplitudeDb(new Complex[4], double.PositiveInfinity)));
            Check.Equal("sampleRate", ParamOf(() => Multitaper.Estimate(new double[16], double.PositiveInfinity, 2)));
        }

        public void Scaling_HugeRatesAndValues()
        {
            Check.Equal(5e307, FrequencyGrid.BinFrequency(2, 4, 1e308));             // ∞
            Check.Equal(5e307, FrequencyGrid.BinFrequencies(4, 1e308)[2]);
            var psd = WelchPsd.Estimate(Enumerable.Range(1, 8).Select(i => (double)i).ToArray(), 1e308, 4).Psd;
            Check.True(psd[0] > 0, "underflowed to 0");
            var x = Fft.Inverse(new[] { new Complex(1e308, 0), new Complex(1e308, 0) });
            Check.Equal(1e308, x[0].Real);                                           // ∞ + NaN·i
            Check.Equal(0.0, x[1].Real);
            Check.Close(0.25, Fft.Inverse(new[] { new Complex(1, 0), new Complex(0, 0), new Complex(0, 0), new Complex(0, 0) })[3].Real, 1e-15);
        }
    }
}
