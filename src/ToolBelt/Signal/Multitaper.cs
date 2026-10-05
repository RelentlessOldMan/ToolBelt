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
            if (!(sampleRate > 0)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            int n = samples.Length;
            if (n < 2) throw new ArgumentException("Need at least two samples.", nameof(samples));
            if (tapers < 1 || tapers > n / 2) throw new ArgumentOutOfRangeException(nameof(tapers), tapers, "Use between 1 and N/2 tapers.");

            int bins = n / 2 + 1;
            bool even = n % 2 == 0;
            var psd = new double[bins];
            var tapered = new double[n];
            double amplitude = Math.Sqrt(2.0 / (n + 1));
            for (int k = 1; k <= tapers; k++)
            {
                for (int i = 0; i < n; i++) tapered[i] = samples[i] * amplitude * Math.Sin(Math.PI * k * (i + 1) / (n + 1));
                var fft = Fft.Forward(tapered);
                for (int b = 0; b < bins; b++)
                {
                    double mag = fft[b].Magnitude;
                    double scale = (b == 0 || (even && b == n / 2)) ? 1.0 : 2.0;
                    psd[b] += scale * mag * mag;
                }
            }
            // Each taper has unit energy (Σv² = 1), so the density normalisation is 1/fs per taper, averaged over K.
            double norm = 1.0 / (sampleRate * tapers);
            for (int b = 0; b < bins; b++) psd[b] *= norm;
            return (FrequencyGrid.BinFrequencies(n, sampleRate), psd);
        }
    }
}
