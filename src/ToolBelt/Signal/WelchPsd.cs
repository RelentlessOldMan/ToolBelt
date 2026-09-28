// ToolBelt drop-in — self-contained; depends on ToolBelt.Signal.Fft, Window, FrequencyGrid.
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Welch's method for power spectral density: average the windowed periodograms of overlapping
    /// segments. Trades frequency resolution for a lower-variance estimate — the standard way to see the
    /// spectrum of a noisy signal without a single noisy periodogram fooling you.
    /// </summary>
    public static class WelchPsd
    {
        /// <summary>
        /// One-sided PSD (units of power per Hz) and its frequency axis. The signal is split into segments
        /// of <paramref name="segmentLength"/> samples overlapping by <paramref name="overlap"/> (0–1),
        /// each tapered by <paramref name="window"/>, and the periodograms are averaged.
        /// </summary>
        public static (double[] Frequencies, double[] Psd) Estimate(
            double[] samples, double sampleRate, int segmentLength,
            double overlap = 0.5, WindowType window = WindowType.Hann)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
            if (segmentLength < 2) throw new ArgumentOutOfRangeException(nameof(segmentLength), segmentLength, "Segment length must be at least 2.");
            if (segmentLength > samples.Length) throw new ArgumentException("Segment longer than the signal.", nameof(segmentLength));
            if (overlap < 0 || overlap >= 1) throw new ArgumentOutOfRangeException(nameof(overlap), overlap, "Overlap must be in [0, 1).");

            var w = Window.Create(window, segmentLength);
            double windowPower = 0;
            foreach (var v in w) windowPower += v * v;

            int step = Math.Max(1, (int)Math.Round(segmentLength * (1 - overlap)));
            int bins = segmentLength / 2 + 1;
            bool even = segmentLength % 2 == 0;
            var psd = new double[bins];
            int segments = 0;

            var seg = new double[segmentLength];
            for (int start = 0; start + segmentLength <= samples.Length; start += step)
            {
                for (int i = 0; i < segmentLength; i++) seg[i] = samples[start + i] * w[i];
                var fft = Fft.Forward(seg);
                for (int k = 0; k < bins; k++)
                {
                    double mag = fft[k].Magnitude;
                    double scale = (k == 0 || (even && k == segmentLength / 2)) ? 1.0 : 2.0;
                    psd[k] += scale * mag * mag;
                }
                segments++;
            }

            // Density normalization: divide by fs·Σw² (per segment) and by the segment count.
            double norm = 1.0 / (sampleRate * windowPower * segments);
            for (int k = 0; k < bins; k++) psd[k] *= norm;

            return (FrequencyGrid.BinFrequencies(segmentLength, sampleRate), psd);
        }
    }
}
