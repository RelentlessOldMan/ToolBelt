using System;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class SpectrumTests
    {
        public void Amplitude_ReadsToneAmplitude()
        {
            const int n = 64;
            const int bin = 7;
            const double amp = 3.5;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = amp * Math.Cos(2 * Math.PI * bin * i / n);

            var spectrum = Spectrum.Amplitude(Fft.Forward(x));
            Check.Equal(n / 2 + 1, spectrum.Length);
            Check.Close(amp, spectrum[bin], 1e-9);            // reads the physical amplitude
            Check.Close(0.0, spectrum[bin + 1], 1e-9);
            Check.Close(0.0, spectrum[0], 1e-9);
        }

        public void Amplitude_DcAndNyquistNotDoubled()
        {
            const int n = 8;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = 5.0; // constant -> pure DC of amplitude 5
            var spectrum = Spectrum.Amplitude(Fft.Forward(x));
            Check.Close(5.0, spectrum[0], 1e-9);              // DC scaled by 1/N, not 2/N

            // Alternating +/-1 is the Nyquist tone; its one-sided amplitude is 1 (edge scaling).
            for (int i = 0; i < n; i++) x[i] = (i % 2 == 0) ? 1.0 : -1.0;
            var nyq = Spectrum.Amplitude(Fft.Forward(x));
            Check.Close(1.0, nyq[n / 2], 1e-9);
        }

        public void Power_IsAmplitudeSquared()
        {
            const int n = 32;
            var rng = new Random(11);
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = rng.NextDouble();
            var fft = Fft.Forward(x);
            var amp = Spectrum.Amplitude(fft);
            var pow = Spectrum.Power(fft);
            for (int i = 0; i < amp.Length; i++) Check.Close(amp[i] * amp[i], pow[i], 1e-12, $"[{i}]");
        }

        public void AmplitudeDb_MatchesLog()
        {
            const int n = 64;
            const int bin = 4;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Cos(2 * Math.PI * bin * i / n);
            var db = Spectrum.AmplitudeDb(Fft.Forward(x));
            Check.Close(0.0, db[bin], 1e-9);                  // amplitude 1 -> 0 dB
            Check.Close(20 * Math.Log10(1e-12), db[0], 1e-6); // silent DC hits the floor
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Spectrum.Amplitude(null!));
            Check.Throws<ArgumentException>(() => Spectrum.Amplitude(Array.Empty<Complex>()));
            Check.Throws<ArgumentOutOfRangeException>(() => Spectrum.AmplitudeDb(new[] { Complex.One }, 0));
        }
    }
}
