// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// The Goertzel algorithm: magnitude and phase of a single frequency, far cheaper than a full transform
    /// when only one or a few bins are wanted. The right tool for tracking one known carrier in a live feed.
    /// </summary>
    public static class Goertzel
    {
        /// <summary>
        /// Estimates the complex response at <paramref name="targetFrequency"/> (Hz). For a pure cosine of
        /// amplitude A at a frequency landing on an exact DFT bin, the returned magnitude is A·N/2 (A·N at DC
        /// and Nyquist). The phase is referenced to the end of the analysis window (a linear offset versus an
        /// n=0 reference), so use phase <i>differences</i> between signals rather than the absolute value.
        /// The recursion's rounding error grows with N and as the frequency nears 0 or Nyquist (about 1e-6
        /// relative at bin 1 of 2²⁰ samples); use the FFT for very long, low-frequency records.
        /// </summary>
        public static (double Magnitude, double Phase) Estimate(
            IReadOnlyList<double> samples, double targetFrequency, double sampleRate)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Count == 0) throw new ArgumentException("At least one sample is required.", nameof(samples));
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");

            if (double.IsNaN(targetFrequency) || double.IsInfinity(targetFrequency))
                throw new ArgumentOutOfRangeException(nameof(targetFrequency), targetFrequency, "Target frequency must be finite.");
            double omega = 2.0 * Math.PI * targetFrequency / sampleRate;
            if (double.IsInfinity(omega)) omega = 2.0 * Math.PI * (targetFrequency / sampleRate);   // 2π·f overflowed first
            double cosine = Math.Cos(omega);
            double coeff = 2.0 * cosine;

            double s0, s1 = 0, s2 = 0;
            for (int n = 0; n < samples.Count; n++)
            {
                s0 = samples[n] + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }

            double real = s1 - s2 * cosine;
            double imag = s2 * Math.Sin(omega);
            double magnitude = Math.Sqrt(real * real + imag * imag);
            if (double.IsInfinity(magnitude) || (magnitude < 1e-150 && (real != 0 || imag != 0))) magnitude = Hypot(real, imag);
            return (magnitude, Math.Atan2(imag, real));
        }

        // √(a² + b²) without squaring the larger: four samples of 1e200 gave a magnitude of ∞, four of 1e-170 one of 0.
        private static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            double big = Math.Max(a, b), small = Math.Min(a, b);
            if (big == 0 || double.IsInfinity(big) || double.IsNaN(big)) return big + small;
            double r = small / big;
            return big * Math.Sqrt(1 + r * r);
        }
    }
}
