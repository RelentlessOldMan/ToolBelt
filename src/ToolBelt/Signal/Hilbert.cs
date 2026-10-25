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
        /// imaginary part is its Hilbert transform. Its magnitude is the envelope, its angle the phase. Samples must be
        /// finite.
        /// </summary>
        public static Complex[] Analytic(double[] samples)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Length == 0) return Array.Empty<Complex>();

            // One NaN or ∞ made every output NaN, and InstantaneousFrequency then blamed a "phase" argument.
            double max = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double v = samples[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException($"samples[{i}] is {v}; samples must be finite.", nameof(samples));
                max = Math.Max(max, Math.Abs(v));
            }

            // The spectrum is up to n times the input, so a large (representable) signal overflowed to NaN: scale it to at
            // most 1 by a power of two (exact) and scale the analytic signal back.
            int exponent = max > 1 ? (int)Math.Ceiling(Math.Log(max, 2)) : 0;
            double down = Math.Pow(2, -exponent), upHalf = Math.Pow(2, exponent / 2), upRest = Math.Pow(2, exponent - exponent / 2);
            int n = samples.Length;
            var scaled = new double[n];
            for (int i = 0; i < n; i++) scaled[i] = samples[i] * down;
            var spectrum = Fft.Forward(scaled);

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
            var analytic = Fft.Inverse(spectrum);
            if (exponent != 0)
                for (int i = 0; i < n; i++) analytic[i] = analytic[i] * upHalf * upRest;
            return analytic;
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
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            var phase = PhaseUnwrap.Unwrap(InstantaneousPhase(samples));
            if (phase.Length < 2) return Array.Empty<double>();
            var freq = new double[phase.Length - 1];
            double scale = sampleRate / (2.0 * Math.PI);
            for (int i = 0; i < freq.Length; i++) freq[i] = (phase[i + 1] - phase[i]) * scale;
            return freq;
        }
    }
}
