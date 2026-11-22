// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// One-sided spectra derived from a full FFT of a real signal. "One-sided" folds the negative
    /// frequencies onto the positive half, so a pure cosine of amplitude A reads A in the amplitude
    /// spectrum (rather than being split into two half-height mirror images).
    /// </summary>
    public static class Spectrum
    {
        /// <summary>
        /// One-sided amplitude spectrum, length <c>N/2 + 1</c>. Scaled so a coherent tone at a bin center
        /// reads its physical amplitude: DC and Nyquist by |X[k]|/N, all interior bins by 2·|X[k]|/N.
        /// </summary>
        public static double[] Amplitude(Complex[] fft)
        {
            if (fft is null) throw new ArgumentNullException(nameof(fft));
            if (fft.Length == 0) throw new ArgumentException("Spectrum is empty.", nameof(fft));

            int n = fft.Length;
            int bins = n / 2 + 1;
            var result = new double[bins];
            for (int k = 0; k < bins; k++)
            {
                bool edge = k == 0 || (n % 2 == 0 && k == n / 2);
                // Divided by N or N/2 (both exact), so the magnitude rounds once: multiplying by a rounded 1/N gave
                // 0.30000000000000004 for |X| = 3 at N = 10, and an exact tie among subnormals rounded the wrong way.
                double divisor = edge ? n : n * 0.5;
                double magnitude = Magnitude(fft[k]);
                // A bin of (Max, Max) has a magnitude past double.MaxValue but an amplitude that fits: halve the parts first.
                if (double.IsInfinity(magnitude) && !double.IsInfinity(fft[k].Real) && !double.IsInfinity(fft[k].Imaginary))
                    result[k] = new Complex(fft[k].Real / 2, fft[k].Imaginary / 2).Magnitude / (divisor * 0.5);
                else result[k] = magnitude / divisor;
            }
            return result;
        }

        /// <summary>One-sided power spectrum: the square of <see cref="Amplitude"/>, length <c>N/2 + 1</c>.</summary>
        public static double[] Power(Complex[] fft)
        {
            var amp = Amplitude(fft);
            for (int i = 0; i < amp.Length; i++) amp[i] *= amp[i];
            return amp;
        }

        /// <summary>
        /// One-sided amplitude spectrum in decibels: <c>20·log10(amplitude)</c>, with a floor so that
        /// silent bins map to a finite value instead of −∞.
        /// </summary>
        public static double[] AmplitudeDb(Complex[] fft, double floor = 1e-12)
        {
            if (!(floor > 0) || double.IsInfinity(floor)) throw new ArgumentOutOfRangeException(nameof(floor), floor, "Floor must be positive and finite.");
            var amp = Amplitude(fft);
            for (int i = 0; i < amp.Length; i++)
                amp[i] = 20.0 * Math.Log10(Math.Max(amp[i], floor));
            return amp;
        }

        // |z| with an infinite part is ∞: Complex.Magnitude of (∞, NaN) is NaN on .NET Core and ∞ on .NET Framework, so
        // the same spectrum read differently on the two.
        private static double Magnitude(Complex z) => double.IsInfinity(z.Real) || double.IsInfinity(z.Imaginary) ? double.PositiveInfinity : z.Magnitude;
    }
}
