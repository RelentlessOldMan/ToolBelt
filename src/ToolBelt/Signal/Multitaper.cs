// ToolBelt drop-in — also copy Signal/Fft.cs and Signal/FrequencyGrid.cs.
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Multitaper power spectral density with the sine tapers of Riedel &amp; Sidorenko (1995): the whole record is
    /// tapered by K mutually orthogonal sine windows, v_k(n) = √(2/(N+1))·sin(πk(n+1)/(N+1)), and the K periodograms are
    /// averaged. Like Welch it trades resolution for variance (the estimate's variance falls roughly as 1/K and its
    /// bandwidth widens to about (K+1)/(2N)·fs), but it uses every sample in every taper instead of chopping the record
    /// into segments — the better choice for short records. Same units and one-sided normalisation as
    /// <see cref="WelchPsd"/> (power per Hz; integrating over frequency gives the signal's mean square). For the classic
    /// averaged (Bartlett) periodogram use <see cref="WelchPsd"/> with no overlap and a rectangular window.
    /// </summary>
    public static class Multitaper
    {
        public static (double[] Frequencies, double[] Psd) Estimate(double[] samples, double sampleRate, int tapers = 5)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            int n = samples.Length;
            if (n < 2) throw new ArgumentException("Need at least two samples.", nameof(samples));
            if (tapers < 1 || tapers > n / 2) throw new ArgumentOutOfRangeException(nameof(tapers), tapers, "Use between 1 and N/2 tapers.");

            int bins = n / 2 + 1;
            bool even = n % 2 == 0;
            var psd = new double[bins];
            var tapered = new double[n];
            double amplitude = Math.Sqrt(2.0 / (n + 1));
            // Scaled to a peak near 1 by a power of two (exact), as in WelchPsd: |X|² overflowed or underflowed for
            // representable densities. The square of the scale is applied last, with the sample rate's exponent.
            double peak = 0;
            foreach (double v in samples) peak = Math.Max(peak, Math.Abs(v));
            int exponent = Exponent(peak);
            double downHalf = Math.Pow(2, -(exponent / 2)), downRest = Math.Pow(2, -(exponent - exponent / 2));
            for (int k = 1; k <= tapers; k++)
            {
                for (int i = 0; i < n; i++) tapered[i] = samples[i] * downHalf * downRest * amplitude * Math.Sin(Math.PI * k * (i + 1) / (n + 1));
                var fft = Fft.Forward(tapered);
                for (int b = 0; b < bins; b++)
                {
                    double mag = fft[b].Magnitude;
                    double scale = (b == 0 || (even && b == n / 2)) ? 1.0 : 2.0;
                    psd[b] += scale * mag * mag;
                }
            }
            // Each taper has unit energy (Σv² = 1), so the density normalisation is 1/fs per taper, averaged over K.
            // Divided in turn: fs·K overflowed to ∞ at a huge rate, making every density 0.
            int rateExponent = (int)Math.Floor(Math.Log(sampleRate, 2));
            double rateMantissa = ScaleB(sampleRate, -rateExponent);
            for (int b = 0; b < bins; b++) psd[b] = ScaleB(psd[b] / tapers / rateMantissa, 2 * exponent - rateExponent);
            return (FrequencyGrid.BinFrequencies(n, sampleRate), psd);
        }

        // x·2^e in exact power-of-two steps: 2^e alone overflows or underflows past ±1023.
        private static double ScaleB(double x, int e)
        {
            for (; e > 1000; e -= 1000) x *= Math.Pow(2, 1000);
            for (; e < -1000; e += 1000) x *= Math.Pow(2, -1000);
            return x * Math.Pow(2, e);
        }

        // The power of two that brings a peak to at most 1 (or, below 2^-500, up near 1, where subnormals lose bits); 0 for a
        // peak that is 0, in range, or not finite. Math.Log is used, not bit tricks, so it works on netstandard2.0 too.
        private static int Exponent(double peak)
            => !double.IsInfinity(peak) && !double.IsNaN(peak) && (peak > 1 || (peak > 0 && peak < 3.054936363499605e-151))
                ? (int)Math.Ceiling(Math.Log(peak, 2)) : 0;
    }
}
