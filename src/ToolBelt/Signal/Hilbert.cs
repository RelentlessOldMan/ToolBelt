// ToolBelt drop-in — self-contained; depends on ToolBelt.Signal.Fft and ToolBelt.Signal.PhaseUnwrap.
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
            // Fft refused it only after the copy below, and named its own parameter ("real").
            if ((samples.Length & (samples.Length - 1)) != 0 && 2L * samples.Length - 1 > 1 << 30)
                throw new ArgumentException($"A length-{samples.Length} signal that is not a power of two is too long for the FFT (2n − 1 ≤ 2^30); pad it to a power of two.", nameof(samples));

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
            // A subnormal signal is scaled up by 2^600 the same way: its spectrum lost bits ([3, 0, −3, 0, 5, 7, −2]·ε gave −2ε
            // for −1.475ε).
            int exponent = max > 1 ? (int)Math.Ceiling(Math.Log(max, 2)) : max > 0 && max < 3.054936363499605e-151 ? -600 : 0;   // 2^-500
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
            // The real part is the input itself: taken from the transform, its rounding scaled back by 2^1024 overflowed
            // ([double.MaxValue, 0, 0] had a real part of ∞, and so an envelope of ∞).
            for (int i = 0; i < n; i++) analytic[i] = new Complex(samples[i], analytic[i].Imaginary * upHalf * upRest);
            return analytic;
        }

        /// <summary>The amplitude envelope: the magnitude of the <see cref="Analytic"/> signal.</summary>
        public static double[] Envelope(double[] samples)
        {
            var analytic = Analytic(samples);
            var env = new double[analytic.Length];
            for (int i = 0; i < env.Length; i++) env[i] = Magnitude(analytic[i]);
            return env;
        }

        // |z| with an infinite part is ∞: Complex.Magnitude of (∞, NaN) is NaN on .NET Core and ∞ on .NET Framework.
        private static double Magnitude(Complex z) => double.IsInfinity(z.Real) || double.IsInfinity(z.Imaginary) ? double.PositiveInfinity : z.Magnitude;

        /// <summary>The instantaneous phase (radians, wrapped to (−π, π]) of the analytic signal.</summary>
        public static double[] InstantaneousPhase(double[] samples)
        {
            var analytic = Analytic(samples);
            var phase = new double[analytic.Length];
            // Rounding noise of −2e-16 in the imaginary part of a negative constant gave exactly −π, outside (−π, π].
            for (int i = 0; i < phase.Length; i++) { double p = analytic[i].Phase; phase[i] = p == -Math.PI ? Math.PI : p; }
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
