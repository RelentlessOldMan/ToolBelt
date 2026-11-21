// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Numerics;

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
            return Bin(bin, fftLength, sampleRate);
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
            for (int k = 0; k < bins; k++) f[k] = Bin(k, fftLength, sampleRate);
            return f;
        }

        // k·fs / N, which overflowed for a huge rate although the result (≤ fs/2) is representable.
        private static double Bin(int k, int fftLength, double sampleRate)
        {
            double f = k * sampleRate / fftLength;
            return double.IsInfinity(f) ? k * (sampleRate / fftLength) : f;
        }

        /// <summary>
        /// The one-sided bin whose center is closest to <paramref name="frequency"/> (Hz); frequencies beyond either end
        /// (±∞ included) give bin 0 or N/2. A frequency exactly between two centres goes to the higher bin.
        /// </summary>
        public static int NearestBin(double frequency, int fftLength, double sampleRate)
        {
            ValidateArgs(fftLength, sampleRate);
            if (double.IsNaN(frequency)) throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Frequency must not be NaN.");
            double ratio = frequency * fftLength / sampleRate;
            // f·N overflowed for a huge in-range frequency, giving N/2.
            if (double.IsInfinity(ratio) && !double.IsInfinity(frequency)) ratio = frequency / sampleRate * fftLength;
            // Clamped as a double: the int cast overflows. Ties went to the even bin (2.5 to 2, 3.5 to 4).
            double bin = Math.Round(ratio, MidpointRounding.AwayFromZero);
            int max = fftLength / 2;
            int b = !(bin > 0) ? 0 : bin >= max ? max : (int)bin;
            if (double.IsInfinity(frequency)) return b;
            // f·N/fs rounded twice (the product, then the quotient), so a frequency just off a midpoint went to the far bin
            // (3099.659649122807 Hz, N 2850, 8020 Hz gave 1102 for 1101): the bin is corrected against the exact midpoints
            // f·2N = (2b ± 1)·fs.
            while (b < max && CompareProducts(frequency, 2L * fftLength, sampleRate, 2L * b + 1) >= 0) b++;
            while (b > 0 && CompareProducts(frequency, 2L * fftLength, sampleRate, 2L * b - 1) < 0) b--;
            return b;
        }

        private static void ValidateArgs(int fftLength, double sampleRate)
        {
            if (fftLength <= 0) throw new ArgumentOutOfRangeException(nameof(fftLength), fftLength, "FFT length must be positive.");
            if (!(sampleRate > 0) || double.IsInfinity(sampleRate)) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive and finite.");
        }
        // The sign of x·a − y·b, exactly (x, y finite; a, b ≥ 0).
        private static int CompareProducts(double x, long a, double y, long b)
        {
            var (mx, ex) = Decompose(x);
            var (my, ey) = Decompose(y);
            int e = Math.Min(ex, ey);
            return (new BigInteger(mx) * a << (ex - e)).CompareTo(new BigInteger(my) * b << (ey - e));
        }

        // x as an integer mantissa times a power of two (zero has mantissa 0).
        private static (long Mantissa, int Exponent) Decompose(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            return (bits < 0 ? -man : man, exp - 1075);
        }
    }
}
