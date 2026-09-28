// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Signal
{
    /// <summary>
    /// The frequency axis of an FFT: which physical frequency each bin represents, and the inverse
    /// lookup. Pairs with <see cref="Spectrum"/>, whose one-sided arrays share this <c>N/2 + 1</c> layout.
    /// </summary>
    public static class FrequencyGrid
    {
        /// <summary>
        /// Bin spacing in Hz: <c>sampleRate / fftLength</c>. This is the resolution bandwidth — the
        /// smallest frequency separation two tones can have and still land in distinct bins.
        /// </summary>
        public static double Resolution(int fftLength, double sampleRate)
        {
            ValidateArgs(fftLength, sampleRate);
            return sampleRate / fftLength;
        }

        /// <summary>The frequency (Hz) of a single one-sided bin.</summary>
        public static double BinFrequency(int bin, int fftLength, double sampleRate)
        {
            ValidateArgs(fftLength, sampleRate);
            if (bin < 0 || bin > fftLength / 2)
                throw new ArgumentOutOfRangeException(nameof(bin), bin, "Bin is outside the one-sided range [0, N/2].");
            return bin * sampleRate / fftLength;
        }

        /// <summary>
        /// The one-sided frequency axis, length <c>N/2 + 1</c>: 0 Hz up to the Nyquist frequency,
        /// aligned bin-for-bin with <see cref="Spectrum.Amplitude"/>.
        /// </summary>
        public static double[] BinFrequencies(int fftLength, double sampleRate)
        {
            ValidateArgs(fftLength, sampleRate);
            int bins = fftLength / 2 + 1;
            var f = new double[bins];
            for (int k = 0; k < bins; k++) f[k] = k * sampleRate / fftLength;
            return f;
        }

        /// <summary>The one-sided bin whose center is closest to <paramref name="frequency"/> (Hz).</summary>
        public static int NearestBin(double frequency, int fftLength, double sampleRate)
        {
            ValidateArgs(fftLength, sampleRate);
            int bin = (int)Math.Round(frequency * fftLength / sampleRate);
            if (bin < 0) bin = 0;
            int max = fftLength / 2;
            if (bin > max) bin = max;
            return bin;
        }

        private static void ValidateArgs(int fftLength, double sampleRate)
        {
            if (fftLength <= 0) throw new ArgumentOutOfRangeException(nameof(fftLength), fftLength, "FFT length must be positive.");
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }
    }
}
