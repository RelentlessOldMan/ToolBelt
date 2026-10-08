using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 2 (signal analysis).
    public sealed class SignalReviewTests
    {
        public void SavitzkyGolay_StaysAccurateAtHighOrders()
        {
            // References: a least-squares fit on a scaled abscissa, checked against 60-digit mpmath. (SciPy 1.12's
            // savgol_coeffs itself loses these: its unscaled Vandermonde is truncated by lstsq's rcond.)
            var x = Enumerable.Range(0, 300).Select(i => Math.Sin(0.05 * i) + 0.1 * Math.Cos(1.3 * i)).ToArray();
            int[] idx = { 0, 3, 20, 150, 280, 299 };
            double[] ref41x14 = { 0.10448571713446819, 0.11150665280144867, 0.8491173027124501, 0.949531432159813, 0.9866959229439382, 0.7605457976601078 };
            double[] ref201x10 = { 0.030663622363622856, 0.15803132946992704, 0.841698094304051, 0.9395594493600443, 0.989196260503752, 0.6823226243354462 };
            var a = SavitzkyGolay.Smooth(x, 41, 14);
            var b = SavitzkyGolay.Smooth(x, 201, 10);
            for (int k = 0; k < idx.Length; k++)
            {
                Check.Close(ref41x14[k], a[idx[k]], 1e-10, $"(41,14)[{idx[k]}]");
                Check.Close(ref201x10[k], b[idx[k]], 1e-10, $"(201,10)[{idx[k]}]");
            }
            // A degree-10 polynomial passes through a 201-point, order-10 smoother unchanged.
            var poly = Enumerable.Range(0, 300).Select(i => { double t = i / 300.0 - 0.4; return Enumerable.Range(0, 11).Sum(p => Math.Pow(t, p) * (p % 3 - 1)); }).ToArray();
            var sp = SavitzkyGolay.Smooth(poly, 201, 10);
            for (int i = 0; i < poly.Length; i++) Check.Close(poly[i], sp[i], 1e-10, $"poly[{i}]");
        }

        public void Quantizer_ClampsHugeAndInfiniteInputsToTheRightEnd()
        {
            Check.Equal((1 << 24) - 1, new Quantizer(24).Code(300));
            Check.Equal((1 << 16) - 1, new Quantizer(16).Code(1e5));
            Check.Equal((1 << 16) - 1, new Quantizer(16).Code(double.PositiveInfinity));
            Check.Equal(0, new Quantizer(16).Code(double.NegativeInfinity));
            Check.Close(1 - 1.0 / (1 << 16), new Quantizer(16).Quantize(double.PositiveInfinity), 1e-15);
            Check.Throws<ArgumentOutOfRangeException>(() => new Quantizer(16).Code(double.NaN));
        }

        public void FrequencyGrid_NearestBinClampsHugeFrequenciesAndRejectsBadRates()
        {
            Check.Equal(512, FrequencyGrid.NearestBin(1e12, 1024, 1));
            Check.Equal(512, FrequencyGrid.NearestBin(double.PositiveInfinity, 1024, 1000));
            Check.Equal(0, FrequencyGrid.NearestBin(-1e12, 1024, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => FrequencyGrid.NearestBin(double.NaN, 1024, 1000));
            Check.Throws<ArgumentOutOfRangeException>(() => FrequencyGrid.NearestBin(10, 1024, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => FrequencyGrid.Resolution(1024, double.PositiveInfinity));
        }

        public void Resample_ToRateRejectsBadRatesAndOversizedOutputs()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Resample.ToRate(new double[1000], 1, 1e7));
            Check.Throws<ArgumentOutOfRangeException>(() => Resample.ToRate(new double[10], double.NaN, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => Resample.ToRate(new double[10], 1, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => Resample.ToRate(new double[10], 1, double.PositiveInfinity));
        }

        public void Resample_ToRateProducesTheTargetSpacing()
        {
            // A 1 kHz ramp of its own timestamps, resampled to 2 kHz, must read the 2 kHz timestamps.
            var x = Enumerable.Range(0, 10).Select(i => i / 1000.0).ToArray();
            var y = Resample.ToRate(x, 1000, 2000);
            Check.Equal(20, y.Length);
            for (int k = 0; k < y.Length; k++) Check.Close(Math.Min(k * 0.0005, 0.009), y[k], 1e-15, $"[{k}]");
        }

        public void ZeroCrossing_SkipsNaNInsteadOfThrowing()
        {
            Check.Equal(0, ZeroCrossing.Find(new[] { 1.0, double.NaN, -1 }).Count);
            var c = ZeroCrossing.Find(new[] { 1.0, -1, double.NaN, 1, -1 });
            Check.Equal(2, c.Count);
            Check.Close(0.5, c[0], 1e-15);
            Check.Close(3.5, c[1], 1e-15);
        }

        public void LevelConversions_SilenceIsMinusInfinityAndRmsDoesNotOverflow()
        {
            Check.Equal(double.NegativeInfinity, LevelConversions.DbFs(new double[100]));
            Check.Close(1e200, LevelConversions.Rms(new[] { 1e200, -1e200 }), 1e186);
            Check.Close(1e-200, LevelConversions.Rms(new[] { 1e-200, 1e-200 }), 1e-214);
        }

        public void EnvelopeFollower_RejectsNonFiniteInsteadOfPoisoningState()
        {
            var f = new EnvelopeFollower(1000, 0.001, 0.01);
            f.Next(1);
            double before = f.Value;
            Check.Throws<ArgumentOutOfRangeException>(() => f.Next(double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => f.Next(double.PositiveInfinity));
            Check.Equal(before, f.Value);
            Check.True(f.Next(1) > before);
            Check.Throws<ArgumentOutOfRangeException>(() => f.Reset(double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new EnvelopeFollower(1000, double.PositiveInfinity, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new EnvelopeFollower(1000, 0, double.PositiveInfinity));
        }

        public void CycleMeasurements_RejectsNaNSamples()
        {
            var square = Enumerable.Range(0, 200).Select(i => i % 20 < 10 ? 1.0 : -1.0).ToArray();
            Check.Equal(8, CycleMeasurements.Measure(square, 1).Cycles.Count);  // starts high: 9 rising edges
            square[55] = double.NaN;
            Check.Throws<ArgumentException>(() => CycleMeasurements.Measure(square, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => CycleMeasurements.Measure(new[] { 0.0, 1, 0, 1 }, 1, level: double.NaN));
        }

        public void CycleMeasurements_RuntPulsesDoNotStartCycles()
        {
            // A runt that crosses the level but never clears the hysteresis band is not an edge.
            var s = new[] { -1, -1, 0.05, -1, -1, 1, 1, -1, -1, 1, 1, -1, -1, 1, 1, -1 };
            var stats = CycleMeasurements.Measure(s, 1);
            Check.Equal(2, stats.Cycles.Count);
            foreach (var c in stats.Cycles) Check.Close(4, c.Period, 1e-12);
            Check.Close(0.5, stats.MeanDutyCycle, 1e-12);
            // A level so close to the top that nothing clears the band finds no cycles rather than NaN duty cycles.
            var near = CycleMeasurements.Measure(Enumerable.Range(0, 100).Select(i => Math.Sin(i * 0.3)).ToArray(), 1, level: 0.95);
            Check.False(near.Cycles.Any(c => double.IsNaN(c.HighTime)));
        }

        public void Hampel_ReplacesNaNAndCountsConsistently()
        {
            var r = Hampel.Filter(new[] { 1.0, 1, 1, double.NaN, 1, 1, 1 }, 2);
            Check.True(r.All(v => v == 1), "a NaN dropout is replaced by the local median");
            Check.Equal(1, Hampel.CountOutliers(new[] { 1.0, 1, 1, double.NaN, 1, 1, 1 }, 2));
            Check.Equal(1.0, Hampel.Filter(new[] { 1.0, 1, 1, double.PositiveInfinity, 1, 1, 1 }, 2)[3]);
            Check.Throws<ArgumentOutOfRangeException>(() => Hampel.Filter(new[] { 1.0, 1, 1, 100, 1, 1, 1 }, 2, double.NaN));
        }

        public void HampelAndMedian_HugeWindowsOnShortInput()
        {
            var x = new double[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3 };
            Check.Equal(10, Hampel.Filter(x, 1 << 30).Length);
            Check.Equal(10, Hampel.Filter(x, int.MaxValue).Length);
            Check.Equal(10, MedianFilter.Apply(x, int.MaxValue).Length);
            Check.Equal(3.5, MedianFilter.Apply(x, int.MaxValue)[0]);   // the whole record: median of 10 values
        }

        public void MedianFilter_EvenEdgeWindowsAverageTheMiddlePair()
        {
            var y = MedianFilter.Apply(new double[] { 1, 2, 3, 4, 5, 6, 7 }, 5);
            Check.True(y.SequenceEqual(new[] { 2, 2.5, 3, 4, 5, 5.5, 6 }), string.Join(",", y));
        }

        public void WelchPsd_RejectsDegenerateWindowsAndNaNParameters()
        {
            var x = Enumerable.Range(0, 64).Select(i => Math.Sin(i)).ToArray();
            Check.Throws<ArgumentException>(() => WelchPsd.Estimate(x, 1, 2));
            Check.Throws<ArgumentOutOfRangeException>(() => WelchPsd.Estimate(x, double.NaN, 16));
            Check.Throws<ArgumentOutOfRangeException>(() => WelchPsd.Estimate(x, 1, 16, double.NaN));
            Check.Equal(2, WelchPsd.Estimate(x, 1, 2, window: WindowType.Rectangular).Psd.Length);
        }

        public void TimeDelay_GccPhatIsScaleInvariantAndTiesPreferZeroLag()
        {
            var a = new double[100];
            for (int i = 0; i < 100; i++) a[i] = 1e-9 * Math.Exp(-Math.Pow((i - 40) / 8.0, 2)) * Math.Sin(0.7 * i);
            var b = new double[100];
            for (int i = 3; i < 100; i++) b[i] = a[i - 3];
            Check.Equal(3, TimeDelayEstimate.CrossCorrelationLag(a, b));
            Check.Equal(3, TimeDelayEstimate.GccPhat(a, b));
            Check.Equal(0, TimeDelayEstimate.CrossCorrelationLag(new double[10], new double[10]));
            Check.Equal(0, TimeDelayEstimate.GccPhat(new double[10], new double[10]));
            Check.Throws<ArgumentOutOfRangeException>(() => TimeDelayEstimate.GccPhat(a, b, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => TimeDelayEstimate.GccPhat(a, b, double.NaN));
        }

        public void TimeDelay_RejectsNaNInput()
        {
            var x = Enumerable.Range(0, 32).Select(i => Math.Sin(i * 0.4)).ToArray();
            var y = (double[])x.Clone();
            y[7] = double.NaN;
            Check.Throws<ArgumentException>(() => TimeDelayEstimate.CrossCorrelationPeak(x, y));
            Check.Throws<ArgumentException>(() => TimeDelayEstimate.GccPhat(y, x));
        }

        public void PeakInterpolation_IgnoresNaNAndDoesNotInterpolateMinima()
        {
            var p = PeakInterpolation.SpectralPeak(new[] { double.NaN, 1, 5, 1, 0.1 }, 1, 8);
            Check.Close(2, p.Bin, 1e-12);
            Check.Close(5, p.Magnitude, 1e-12);
            Check.Throws<ArgumentException>(() => PeakInterpolation.SpectralPeak(new[] { double.NaN, double.NaN }, 1, 2));
            var m = PeakInterpolation.SpectralPeak(new[] { 1, 0.5, 1.2, 3 }, 1, 6, PeakMethod.Parabolic, bin: 1);
            Check.Equal(1.0, m.Bin);
            Check.Equal(0.5, m.Magnitude);
            Check.Equal("left", ((ArgumentException)Check.Throws<ArgumentOutOfRangeException>(() => PeakInterpolation.Gaussian(-1, 1, 1))).ParamName);
        }

        public void PulseMeasurements_ValidatesReferenceLevels()
        {
            var step = Enumerable.Range(0, 20).Select(i => Math.Min(1, Math.Max(0, (i - 5) / 5.0))).ToArray();
            Check.Close(4, PulseMeasurements.RiseTime(step, 1), 1e-12);
            Check.Throws<ArgumentOutOfRangeException>(() => PulseMeasurements.RiseTime(step, 1, 10, 90));
            Check.Throws<ArgumentException>(() => PulseMeasurements.RiseTime(step, 1, 0.9, 0.1));
            Check.Throws<ArgumentException>(() => PulseMeasurements.FallTime(step, 1, 0.9, 0.1));
            Check.Throws<ArgumentOutOfRangeException>(() => PulseMeasurements.RiseTime(step, double.NaN));
        }

        public void NaNRatesAndFloorsAreRejected()
        {
            var x = new[] { 1.0, 0, -1, 0 };
            Check.Throws<ArgumentOutOfRangeException>(() => Goertzel.Estimate(x, 1, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => Hilbert.InstantaneousFrequency(x, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => Spectrum.AmplitudeDb(Fft.Forward(x), double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => PulseMeasurements.PulseWidth(x, double.NaN));
        }

        public void Fft_LongTransformsStayAtRoundingAccuracy()
        {
            var rng = new Random(5);
            foreach (int n in new[] { 1 << 20, 1000003 })
            {
                var x = new Complex[n];
                for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
                var back = Fft.Inverse(Fft.Forward(x));
                double err = 0;
                for (int i = 0; i < n; i++) err = Math.Max(err, (back[i] - x[i]).Magnitude);
                Check.True(err < 2e-14, $"n = {n}: round-trip error {err:E2}");
            }
            // A bin-centred tone leaks nothing measurable into the other bins.
            int m = 1 << 20;
            var tone = new double[m];
            for (int i = 0; i < m; i++) tone[i] = Math.Cos(2 * Math.PI * 12345.0 * i / m);
            var amp = Spectrum.Amplitude(Fft.Forward(tone));
            double leak = 0;
            for (int k = 0; k < amp.Length; k++) if (k != 12345) leak = Math.Max(leak, amp[k]);
            Check.True(leak < 1e-12, $"leakage {leak:E2}");
        }

        public void PhaseUnwrap_SmallToleranceNeverWidensAJump()
        {
            var y = PhaseUnwrap.Unwrap(new[] { 0, 0.6 * Math.PI }, 0.5 * Math.PI);
            Check.Close(0.6 * Math.PI, y[1], 1e-15);
            var z = PhaseUnwrap.Unwrap(new[] { 0, 1.2 * Math.PI }, 0.5 * Math.PI);
            Check.Close(-0.8 * Math.PI, z[1], 1e-15);
        }
    }
}
