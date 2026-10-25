using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 39 (signal analysis, third pass): each test failed before its fix.
    public sealed class SignalReview3Tests
    {
        private static double Chebyshev(int order, double t)
        {
            double a = 1, b = t;
            if (order == 0) return a;
            for (int j = 1; j < order; j++) { double c = 2 * t * b - a; a = b; b = c; }
            return b;
        }

        private static double MaxError(double[] expected, double[] actual)
            => expected.Select((e, i) => Math.Abs(e - actual[i])).Max();

        public void SavitzkyGolay_HighOrdersPassPolynomialsThrough()
        {
            foreach (var (w, order, length) in new[] { (41, 40, 41), (41, 24, 60), (101, 50, 101), (101, 60, 111), (201, 40, 230) })
            {
                double c = (length - 1) / 2.0;
                double[] data = Enumerable.Range(0, length).Select(i => Chebyshev(order, (i - c) / c)).ToArray();
                double error = MaxError(data, SavitzkyGolay.Smooth(data, w, order));
                Check.True(error < 1e-9, $"({w}, {order}): {error}");
            }

            // Order w − 1 fits every window exactly: the identity.
            var random = new Random(1);
            double[] noise = Enumerable.Range(0, 60).Select(_ => random.NextDouble()).ToArray();
            Check.True(MaxError(noise, SavitzkyGolay.Smooth(noise, 51, 50)) < 1e-9, "(51, 50) identity");
            // Ordinary orders are unchanged.
            Check.Close(17.0 / 35, SavitzkyGolay.Smooth(new double[] { 0, 0, 1, 0, 0 }, 5, 2)[2], 1e-14);
        }

        public void Fft_NonPowerOfTwoLargeInputsStayFinite()
        {
            var x = Fft.Forward(new double[] { 1e308, 0, 0 });
            foreach (var c in x) Check.Close(1e308, c.Real, 1e294);

            var y = Fft.Inverse(new[] { new Complex(3e307, 0), new Complex(3e307, 0), new Complex(3e307, 0) });
            Check.Close(3e307, y[0].Real, 1e293);
            Check.True(Math.Abs(y[1].Real) < 1e293 && Math.Abs(y[2].Real) < 1e293, "zeros");

            var random = new Random(2);
            double[] big = Enumerable.Range(0, 1000).Select(_ => (random.NextDouble() * 2 - 1) * 1e304).ToArray();
            var back = Fft.Inverse(Fft.Forward(big));
            for (int i = 0; i < big.Length; i++) Check.Close(big[i], back[i].Real, 1e291);
        }

        public void Hilbert_LargeSignalsAndNonFiniteSamples()
        {
            foreach (int n in new[] { 7, 1000, 1024 })
            {
                double[] tone = Enumerable.Range(0, n).Select(i => 1e306 * Math.Cos(2 * Math.PI * 10 * i / n)).ToArray();
                double[] env = Hilbert.Envelope(tone);
                Check.Close(1e306, env[n / 2], 1e294, "n = " + n);
            }

            var e = Check.Throws<ArgumentException>(() => Hilbert.InstantaneousFrequency(new[] { 1, double.NaN, 1, 2 }, 1));
            Check.Equal("samples", e.ParamName);
            Check.Equal("samples", Check.Throws<ArgumentException>(() => Hilbert.Analytic(new[] { 1, double.PositiveInfinity })).ParamName);
        }

        public void PulseMeasurements_DenormalRange()
        {
            double eps = double.Epsilon;
            Check.Equal((0.0, eps), PulseMeasurements.StateLevels(new[] { 0, eps }));
            var (low, high) = PulseMeasurements.StateLevels(new[] { -eps, eps });
            Check.True(low == -eps && high == eps, $"{low}, {high}");
            PulseMeasurements.DutyCycle(new[] { 3 * eps, 4 * eps, 3 * eps, 4 * eps });
        }

        public void Resample_LinearKeepsExactPoints()
        {
            double[] r = Resample.Linear(new[] { 1, double.NaN, 3 }, 3);
            Check.Equal(1.0, r[0]);
            Check.True(double.IsNaN(r[1]), "NaN");
            Check.Equal(3.0, r[2]);
            double[] r2 = Resample.Linear(new[] { 1, double.PositiveInfinity }, 2);
            Check.Equal(1.0, r2[0]);
            Check.Equal(double.PositiveInfinity, r2[1]);
        }

        public void Multitaper_HugeRate()
        {
            double[] x = Enumerable.Range(0, 64).Select(i => 1e10 * Math.Sin(2 * Math.PI * 8 * i / 64)).ToArray();
            var mt = Multitaper.Estimate(x, 1e308, 2);
            Check.True(mt.Psd.Max() > 0, "multitaper density underflowed to 0");
        }

        public void PeakInterpolation_HugeRate()
        {

            var mag = new double[513];
            mag[99] = 0.5; mag[100] = 1; mag[101] = 0.5;
            var peak = PeakInterpolation.SpectralPeak(mag, 1.5e308, 1024);
            Check.Close(FrequencyGrid.BinFrequency(100, 1024, 1.5e308), peak.Frequency, 1e294);
        }

        public void NearestBin_HugeFrequency()
        {

            Check.Equal(4, FrequencyGrid.NearestBin(2.5e307, 16, 1e308));
            Check.Equal(8, FrequencyGrid.NearestBin(double.PositiveInfinity, 16, 1e308));
        }

        public void DbFs_ExtremeRatios()
        {

            Check.Close(-8000, LevelConversions.DbFs(new[] { 1e-300 }, 1e100), 1e-9);
            Check.Close(6200, LevelConversions.DbFs(new[] { 1e300 }, 1e-10), 1e-9);
            Check.Close(-20, LevelConversions.DbFs(new[] { 0.1, -0.1 }), 1e-12);
        }

        public void PeakInterpolation_NaNVertexIsNotReturned()
        {
            var peak = PeakInterpolation.SpectralPeak(new[] { 0, double.PositiveInfinity, double.PositiveInfinity, 0 }, 1, 6,
                PeakMethod.Parabolic);
            Check.Equal(1.0, peak.Bin);
            Check.Equal(double.PositiveInfinity, peak.Magnitude);
        }

        public void Window_ApplyEmpty()
        {
            Check.Equal(0, Window.Apply(Array.Empty<double>().ToList(), WindowType.Hann).Length);
        }

        public void CycleMeasurements_BandEdgeAtTheExtreme()
        {
            // 0/1 square wave: period 100, 30% high, 1000 samples at 1 kHz.
            double[] square = Enumerable.Range(0, 1000).Select(i => i % 100 < 30 ? 1.0 : 0.0).ToArray();
            Check.Equal(8, CycleMeasurements.Measure(square, 1000).Cycles.Count);
            Check.Equal(8, CycleMeasurements.Measure(square, 1000, level: 0.9).Cycles.Count);
            Check.Equal(8, CycleMeasurements.Measure(square, 1000, level: 0.8, hysteresis: 0.2).Cycles.Count);
            Check.Equal(8, CycleMeasurements.Measure(square, 1000, level: 0.1).Cycles.Count);
        }
    }
}
