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
        /// each tapered by <paramref name="window"/>, and the periodograms are averaged. Segments are not detrended and the
        /// window is the symmetric one from <see cref="Window.Create"/>: this equals SciPy's
        /// <c>welch(x, fs, window=get_window(w, n, fftbins=False), nperseg=n, noverlap=n − step, detrend=False)</c>,
        /// not its defaults (a periodic window and constant detrending). Subtract the mean first if DC is not of interest.
        /// </summary>
        public static (double[] Frequencies, double[] Psd) Estimate(
            double[] samples, double sampleRate, int segmentLength,
            double overlap = 0.5, WindowType window = WindowType.Hann)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
            if (segmentLength < 2) throw new ArgumentOutOfRangeException(nameof(segmentLength), segmentLength, "Segment length must be at least 2.");
            if (segmentLength > samples.Length) throw new ArgumentException("Segment longer than the signal.", nameof(segmentLength));
            if (!(overlap >= 0 && overlap < 1)) throw new ArgumentOutOfRangeException(nameof(overlap), overlap, "Overlap must be in [0, 1).");

            if (!Enum.IsDefined(typeof(WindowType), window)) throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown window type.");
            var w = Window.Create(window, segmentLength);
            double windowPower = 0;
            foreach (var v in w) windowPower += v * v;
            if (windowPower == 0)
                throw new ArgumentException($"A {window} window of {segmentLength} samples is all zeros; use a longer segment or another window.", nameof(segmentLength));

            int step = Math.Max(1, (int)Math.Round(segmentLength * (1 - overlap)));
            int bins = segmentLength / 2 + 1;
            bool even = segmentLength % 2 == 0;
            var psd = new double[bins];
            int segments = 0;

            // |X|² overflowed or underflowed for representable densities (amplitude 1e155 at 1e10 Hz gave ∞, 1e-170 at
            // 1e-100 Hz gave 0): the samples are scaled to a peak near 1 by a power of two (exact), and the square of that
            // scale is applied last, together with the sample rate's exponent.
            double peak = 0;
            foreach (double v in samples) peak = Math.Max(peak, Math.Abs(v));
            int exponent = Exponent(peak);
            double downHalf = Math.Pow(2, -(exponent / 2)), downRest = Math.Pow(2, -(exponent - exponent / 2));

            var seg = new double[segmentLength];
            for (int start = 0; start + segmentLength <= samples.Length; start += step)
            {
                for (int i = 0; i < segmentLength; i++) seg[i] = samples[start + i] * downHalf * downRest * w[i];
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
            // Divided in turn: 1 / (fs·Σw²·segments) underflowed to 0 for a huge rate although the PSD is representable.
            int rateExponent = (int)Math.Floor(Math.Log(sampleRate, 2));
            double rateMantissa = ScaleB(sampleRate, -rateExponent);
            for (int k = 0; k < bins; k++) psd[k] = ScaleB(psd[k] / (windowPower * segments) / rateMantissa, 2 * exponent - rateExponent);

            return (FrequencyGrid.BinFrequencies(segmentLength, sampleRate), psd);
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
