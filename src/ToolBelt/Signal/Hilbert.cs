// ToolBelt drop-in — self-contained; depends on ToolBelt.Signal.Fft.
using System;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// The analytic signal and its by-products — amplitude envelope and instantaneous phase/frequency —
    /// computed via the FFT-domain Hilbert transform. The clean way to demodulate an AM/FM signal or to
    /// track the energy contour of an arbitrary waveform.
    /// </summary>
    public static class Hilbert
    {
        /// <summary>
        /// The analytic signal x + i·H{x}: a complex signal whose real part is the input and whose
        /// imaginary part is its Hilbert transform. Its magnitude is the envelope, its angle the phase.
        /// </summary>
        public static Complex[] Analytic(double[] samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Length == 0) return Array.Empty<Complex>();

            int n = samples.Length;
            var spectrum = Fft.Forward(samples);

            // Zero the negative frequencies and double the positive ones (leave DC and Nyquist alone).
            int half = n / 2;
            for (int k = 0; k < n; k++)
            {
                double factor;
                if (k == 0 || (n % 2 == 0 && k == half)) factor = 1.0;
                else if (k < half || (n % 2 != 0 && k <= half)) factor = 2.0;
                else factor = 0.0;
                spectrum[k] *= factor;
            }
            return Fft.Inverse(spectrum);
        }

        /// <summary>The amplitude envelope: the magnitude of the <see cref="Analytic"/> signal.</summary>
        public static double[] Envelope(double[] samples)
        {
            var analytic = Analytic(samples);
            var env = new double[analytic.Length];
            for (int i = 0; i < env.Length; i++) env[i] = analytic[i].Magnitude;
            return env;
        }

        /// <summary>The instantaneous phase (radians, wrapped to (−π, π]) of the analytic signal.</summary>
        public static double[] InstantaneousPhase(double[] samples)
        {
            var analytic = Analytic(samples);
            var phase = new double[analytic.Length];
            for (int i = 0; i < phase.Length; i++) phase[i] = analytic[i].Phase;
            return phase;
        }

        /// <summary>
        /// Instantaneous frequency in Hz: the unwrapped-phase derivative scaled by the sample rate.
        /// Returns one fewer value than the input (a first difference).
        /// </summary>
        public static double[] InstantaneousFrequency(double[] samples, double sampleRate)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            var phase = PhaseUnwrap.Unwrap(InstantaneousPhase(samples));
            if (phase.Length < 2) return Array.Empty<double>();
            var freq = new double[phase.Length - 1];
            double scale = sampleRate / (2.0 * Math.PI);
            for (int i = 0; i < freq.Length; i++) freq[i] = (phase[i + 1] - phase[i]) * scale;
            return freq;
        }
    }
}
