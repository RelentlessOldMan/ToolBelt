using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    /// <summary>Peak interpolation, envelope follower, group delay, multitaper PSD and cycle measurements.</summary>
    public sealed class SignalExtrasTests
    {
        // ---------- peak interpolation ----------

        public void Parabolic_And_Gaussian_AreExactOnTheirShapes()
        {
            double P(double x) => 5 - 2 * (x - 0.3) * (x - 0.3);
            var (po, pp) = PeakInterpolation.Parabolic(P(-1), P(0), P(1));
            Check.Close(0.3, po, 1e-12);
            Check.Close(5, pp, 1e-12);

            double G(double x) => 3 * Math.Exp(-(x + 0.2) * (x + 0.2) / 1.5);
            var (go, gp) = PeakInterpolation.Gaussian(G(-1), G(0), G(1));
            Check.Close(-0.2, go, 1e-12);
            Check.Close(3, gp, 1e-12);
            Check.Equal((0.0, 4.0), PeakInterpolation.Parabolic(4, 4, 4));             // flat: no offset
        }

        public void SpectralPeak_RefinesBeyondTheBinGrid()
        {
            const double fs = 1000, f0 = 123.37;
            const int n = 1000;
            double[] x = Enumerable.Range(0, n).Select(i => Math.Sin(2 * Math.PI * f0 * i / fs)).ToArray();
            double[] mag = Spectrum.Amplitude(Fft.Forward(Window.Apply(x, WindowType.Hann)));
            var raw = PeakInterpolation.SpectralPeak(mag, fs, n, PeakMethod.Parabolic, bin: Array.IndexOf(mag, mag.Max()));
            int k = (int)Math.Round(raw.Bin);
            double rawError = Math.Abs(k * fs / n - f0);
            var gauss = PeakInterpolation.SpectralPeak(mag, fs, n, PeakMethod.Gaussian);
            var parab = PeakInterpolation.SpectralPeak(mag, fs, n, PeakMethod.Parabolic);
            Check.True(Math.Abs(gauss.Frequency - f0) < 0.03, $"Gaussian error {Math.Abs(gauss.Frequency - f0)} Hz");
            Check.True(Math.Abs(parab.Frequency - f0) < 0.12, $"parabolic error {Math.Abs(parab.Frequency - f0)} Hz");
            Check.True(Math.Abs(gauss.Frequency - f0) < rawError / 5, "interpolation should beat the bin grid");
        }

        public void SpectralPeak_EdgeBinsAreNotExtrapolated()
        {
            var r = PeakInterpolation.SpectralPeak(new double[] { 9, 3, 1 }, 100, 4);
            Check.Equal(0.0, r.Bin);
            Check.Equal(9.0, r.Magnitude);
            Check.Throws<ArgumentOutOfRangeException>(() => PeakInterpolation.Gaussian(1, 0, 1));
        }

        // ---------- envelope follower ----------

        public void Envelope_TimeConstantsAreExact()
        {
            var f = new EnvelopeFollower(1000, attackSeconds: 0.01, releaseSeconds: 0.05);
            double v = 0;
            for (int i = 0; i < 10; i++) v = f.Next(1);                              // one attack time constant
            Check.Close(1 - Math.Exp(-1), v, 1e-12);
            f.Reset(1);
            for (int i = 0; i < 50; i++) v = f.Next(0);                              // one release time constant
            Check.Close(Math.Exp(-1), v, 1e-12);
            var instant = new EnvelopeFollower(1000, 0, 0);
            Check.Equal(3.0, instant.Next(-3));                                     // rectified, no smoothing
        }

        public void Envelope_TracksASineAmplitude()
        {
            const double fs = 48000;
            double[] x = Enumerable.Range(0, 48000).Select(i => 2 * Math.Sin(2 * Math.PI * 1000 * i / fs)).ToArray();
            double[] env = EnvelopeFollower.Process(x, fs, 0.0005, 0.1);
            double[] settled = env.Skip(24000).ToArray();
            Check.True(settled.Min() > 1.9 && settled.Max() <= 2.0 + 1e-12, $"envelope {settled.Min()}..{settled.Max()}");
        }

        // ---------- group delay ----------

        public void GroupDelay_OfAPureDelayIsConstant()
        {
            double[] f = { 10, 25, 30, 47.5, 80, 81, 200 };                          // uneven spacing
            double[] phase = f.Select(v => -2 * Math.PI * v * 0.0025 + 1.1).ToArray();
            foreach (double tau in PhaseUnwrap.GroupDelay(phase, f)) Check.Close(0.0025, tau, 1e-15);
            Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new double[] { 1, 2 }, new double[] { 5, 5 }));
            Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new double[] { 1 }, new double[] { 5 }));
        }

        // ---------- multitaper ----------

        private static double[] WhiteNoise(int n, int seed)
        {
            var rng = new DeterministicRandom(seed);
            return Enumerable.Range(0, n).Select(_ => rng.NextGaussian()).ToArray();
        }

        public void Multitaper_WhiteNoiseLevelAndParseval()
        {
            const double fs = 1000;
            double[] x = WhiteNoise(4096, 1);
            var (freq, psd) = Multitaper.Estimate(x, fs, tapers: 6);
            Check.Equal(2049, psd.Length);
            Check.Close(500, freq[freq.Length - 1], 1e-9);
            Check.Close(2.0 / fs, psd.Skip(1).Take(psd.Length - 2).Average(), 0.1 * 2 / fs);   // one-sided σ²·2/fs
            double integral = psd.Sum() * fs / x.Length;
            Check.Close(x.Average(v => v * v), integral, 0.1);
            var (_, welch) = WelchPsd.Estimate(x, fs, 256);
            Check.Close(welch.Average(), psd.Average(), 0.15 * welch.Average());
        }

        public void Multitaper_MoreTapersMeansLessVariance()
        {
            double[] x = WhiteNoise(4096, 2);
            double Cv(double[] p)
            {
                var inner = p.Skip(10).Take(p.Length - 20).ToArray();
                double m = inner.Average();
                return Math.Sqrt(inner.Average(v => (v - m) * (v - m))) / m;
            }
            double one = Cv(Multitaper.Estimate(x, 1, 1).Psd), eight = Cv(Multitaper.Estimate(x, 1, 8).Psd);
            Check.True(one > 0.8, $"single-taper CV {one} (a raw periodogram is ~1)");
            Check.True(eight < 0.5, $"8-taper CV {eight} (~1/√8 expected)");
        }

        public void Multitaper_FindsATone()
        {
            const double fs = 2000;
            double[] noise = WhiteNoise(2000, 3);
            double[] x = noise.Select((v, i) => 0.3 * v + Math.Sin(2 * Math.PI * 315 * i / fs)).ToArray();
            var (freq, psd) = Multitaper.Estimate(x, fs, 4);
            Check.Close(315, freq[Array.IndexOf(psd, psd.Max())], 1.5);
            Check.Throws<ArgumentOutOfRangeException>(() => Multitaper.Estimate(x, fs, 0));
        }

        // ---------- cycle measurements ----------

        // A 0/1 square wave whose rising edges are at the given times (seconds) with the given high time, sampled at fs.
        private static double[] Square(IReadOnlyList<double> rises, double high, double fs, double duration)
        {
            var x = new double[(int)(duration * fs)];
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / fs;
                foreach (double r in rises) if (t >= r && t < r + high) { x[i] = 1; break; }
            }
            return x;
        }

        public void Cycles_SquareWavePeriodAndDuty()
        {
            const double fs = 100000;
            var rises = Enumerable.Range(0, 21).Select(i => 0.001 + i * 0.010).ToArray();
            CycleStatistics s = CycleMeasurements.Measure(Square(rises, 0.003, fs, 0.215), fs);
            Check.Equal(20, s.Cycles.Count);
            Check.Close(0.010, s.MeanPeriod, 2e-5);
            Check.Close(100, s.MeanFrequency, 0.2);
            Check.Close(0.3, s.MeanDutyCycle, 0.003);
            Check.Close(1, s.Cycles[3].Maximum);
            Check.Close(0, s.Cycles[3].Minimum);
        }

        public void Cycles_SineAndJitter()
        {
            const double fs = 10000;
            double[] sine = Enumerable.Range(0, 10000).Select(i => Math.Sin(2 * Math.PI * 50 * i / fs)).ToArray();
            CycleStatistics s = CycleMeasurements.Measure(sine, fs);
            Check.Close(0.020, s.MeanPeriod, 1e-6);
            Check.Close(0.5, s.MeanDutyCycle, 1e-3);
            Check.True(s.PeriodStandardDeviation < 1e-6);

            // Periods alternating 10 ms ± 0.1 ms: peak-to-peak 0.2 ms, every consecutive difference ±0.2 ms.
            var rises = new List<double> { 0.001 };
            for (int i = 0; i < 30; i++) rises.Add(rises[i] + (i % 2 == 0 ? 0.0101 : 0.0099));
            CycleStatistics j = CycleMeasurements.Measure(Square(rises, 0.004, 1e6, 0.32), 1e6);
            Check.Close(0.0002, j.PeakToPeakJitter, 3e-6);
            Check.Close(0.0002, j.CycleToCycleJitter, 3e-6);
        }

        public void Cycles_HysteresisRejectsNoise()
        {
            const double fs = 20000;
            var rng = new DeterministicRandom(4);
            // A slow triangle wave: noise around the mid-level would cause chatter without hysteresis.
            double[] x = Enumerable.Range(0, 20000).Select(i =>
            {
                double ph = (i / fs * 10) % 1;                                      // 10 Hz
                return (ph < 0.5 ? 4 * ph - 1 : 3 - 4 * ph) + 0.05 * (rng.NextDouble() - 0.5);
            }).ToArray();
            Check.Equal(9, CycleMeasurements.Measure(x, fs).Cycles.Count);
            Check.True(CycleMeasurements.Measure(x, fs, hysteresis: 0).Cycles.Count > 9, "without hysteresis noise adds false cycles");
        }

        public void Cycles_FlatOrTinyInputs()
        {
            CycleStatistics flat = CycleMeasurements.Measure(new double[100], 1000);
            Check.Equal(0, flat.Cycles.Count);
            Check.True(double.IsNaN(flat.MeanPeriod));
            Check.Throws<ArgumentOutOfRangeException>(() => CycleMeasurements.Measure(new double[10], 0));
        }
    }
}
