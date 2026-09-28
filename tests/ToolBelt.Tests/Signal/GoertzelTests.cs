using System;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class GoertzelTests
    {
        public void PureToneAtBin()
        {
            const int n = 64;
            const double fs = 64, amplitude = 2.0;
            const int bin = 8;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = amplitude * Math.Cos(2 * Math.PI * bin * i / n);

            var (mag, _) = Goertzel.Estimate(x, bin * fs / n, fs);
            Check.Close(amplitude * n / 2.0, mag, 1e-6); // A·N/2 at an exact bin
        }

        public void PhaseDiffersByQuarterTurnBetweenCosineAndSine()
        {
            const int n = 64;
            const double fs = 64;
            const int bin = 8;
            var cos = new double[n];
            var sin = new double[n];
            for (int i = 0; i < n; i++)
            {
                cos[i] = Math.Cos(2 * Math.PI * bin * i / n);
                sin[i] = Math.Sin(2 * Math.PI * bin * i / n);
            }
            double pc = Goertzel.Estimate(cos, bin * fs / n, fs).Phase;
            double ps = Goertzel.Estimate(sin, bin * fs / n, fs).Phase;

            double diff = pc - ps;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;
            Check.Close(Math.PI / 2, Math.Abs(diff), 1e-6); // sine lags cosine by a quarter turn
        }

        public void AbsentFrequencyIsNearZero()
        {
            const int n = 64;
            const double fs = 64;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Cos(2 * Math.PI * 8 * i / n);

            var (mag, _) = Goertzel.Estimate(x, 13 * fs / n, fs); // orthogonal bin
            Check.True(mag < 1e-6, $"absent frequency magnitude {mag} should be ~0");
        }

        // Differential: Goertzel magnitude equals a direct DFT-bin computation for random signals.
        public void Property_MatchesDirectDft()
        {
            var rng = new Random(40);
            const int n = 48;
            const double fs = 48;
            for (int t = 0; t < 200; t++)
            {
                var x = new double[n];
                for (int i = 0; i < n; i++) x[i] = rng.NextDouble() * 2 - 1;
                int k = rng.Next(1, n / 2);

                double re = 0, im = 0;
                for (int i = 0; i < n; i++)
                {
                    double angle = 2 * Math.PI * k * i / n;
                    re += x[i] * Math.Cos(angle);
                    im -= x[i] * Math.Sin(angle);
                }
                double dftMag = Math.Sqrt(re * re + im * im);

                var (mag, _) = Goertzel.Estimate(x, k * fs / n, fs);
                Check.Close(dftMag, mag, 1e-6, $"t{t}: bin {k}");
            }
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => Goertzel.Estimate(Array.Empty<double>(), 1, 8));
            Check.Throws<ArgumentOutOfRangeException>(() => Goertzel.Estimate(new double[] { 1 }, 1, 0));
            Check.Throws<ArgumentNullException>(() => Goertzel.Estimate(null!, 1, 8));
        }
    }
}
