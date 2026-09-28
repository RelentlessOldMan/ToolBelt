using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class WelchPsdTests
    {
        public void FrequencyAxis_SpansZeroToNyquist()
        {
            var x = new double[2048];
            var (f, psd) = WelchPsd.Estimate(x, 1000, 256);
            Check.Equal(129, f.Length);          // segLen/2 + 1
            Check.Equal(129, psd.Length);
            Check.Close(0.0, f[0], 1e-12);
            Check.Close(500.0, f[128], 1e-9);    // Nyquist = fs/2
        }

        // Parseval-style: the PSD of a sine integrates to its power (A^2/2).
        public void IntegratedPsd_EqualsSignalPower()
        {
            const int n = 8192;
            const double fs = 1024, tone = 128, amp = 1.0;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = amp * Math.Sin(2 * Math.PI * tone * i / fs);

            var (f, psd) = WelchPsd.Estimate(x, fs, 1024, overlap: 0.5, window: WindowType.Hann);
            double df = f[1] - f[0];
            double total = 0;
            foreach (var p in psd) total += p * df;   // rectangular integration
            Check.Close(amp * amp / 2.0, total, 0.02); // expected power 0.5
        }

        public void PeakLandsAtToneFrequency()
        {
            const int n = 8192;
            const double fs = 1000, tone = 125;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Sin(2 * Math.PI * tone * i / fs);
            var (f, psd) = WelchPsd.Estimate(x, fs, 1000);

            int peak = 0;
            for (int i = 1; i < psd.Length; i++) if (psd[i] > psd[peak]) peak = i;
            Check.Close(tone, f[peak], f[1] - f[0]); // within one bin
        }

        public void AveragingReducesVariance()
        {
            // More segments (smaller segment length over the same record) => smoother estimate.
            var rng = new Random(7);
            const int n = 16384;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = rng.NextDouble() * 2 - 1;

            var (_, few) = WelchPsd.Estimate(x, 1000, 4096, overlap: 0.5);
            var (_, many) = WelchPsd.Estimate(x, 1000, 256, overlap: 0.5);
            Check.True(StdDev(many) < StdDev(few), "more segments should lower PSD variance");
        }

        private static double StdDev(double[] v)
        {
            double mean = 0;
            foreach (var x in v) mean += x;
            mean /= v.Length;
            double s = 0;
            foreach (var x in v) s += (x - mean) * (x - mean);
            return Math.Sqrt(s / v.Length);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => WelchPsd.Estimate(null!, 1000, 256));
            Check.Throws<ArgumentOutOfRangeException>(() => WelchPsd.Estimate(new double[100], 0, 64));
            Check.Throws<ArgumentOutOfRangeException>(() => WelchPsd.Estimate(new double[100], 1000, 1));
            Check.Throws<ArgumentException>(() => WelchPsd.Estimate(new double[100], 1000, 256)); // segment > signal
            Check.Throws<ArgumentOutOfRangeException>(() => WelchPsd.Estimate(new double[100], 1000, 64, overlap: 1.0));
        }
    }
}
