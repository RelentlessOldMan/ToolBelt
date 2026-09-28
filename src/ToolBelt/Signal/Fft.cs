// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Numerics;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Discrete Fourier transform. Radix-2 Cooley–Tukey for power-of-two lengths and Bluestein's
    /// chirp-z algorithm for every other length, so the transform is O(n log n) for <em>any</em> n —
    /// not just powers of two — and exact to floating-point rounding. The direct convolutions in
    /// <see cref="Convolution"/> are the O(n·m) reference these fast paths are graded against.
    /// </summary>
    public static class Fft
    {
        /// <summary>Forward transform. Returns a new array; the input is not modified.</summary>
        public static Complex[] Forward(Complex[] input)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (input.Length == 0) return Array.Empty<Complex>();
            var data = (Complex[])input.Clone();
            Transform(data, inverse: false);
            return data;
        }

        /// <summary>Forward transform of a real-valued signal (imaginary parts taken as zero).</summary>
        public static Complex[] Forward(double[] real)
        {
            if (real is null) throw new ArgumentNullException(nameof(real));
            if (real.Length == 0) return Array.Empty<Complex>();
            var data = new Complex[real.Length];
            for (int i = 0; i < real.Length; i++) data[i] = new Complex(real[i], 0);
            Transform(data, inverse: false);
            return data;
        }

        /// <summary>
        /// Inverse transform, normalized by 1/N so that <c>Inverse(Forward(x))</c> recovers <c>x</c>.
        /// Returns a new array; the input is not modified.
        /// </summary>
        public static Complex[] Inverse(Complex[] spectrum)
        {
            if (spectrum is null) throw new ArgumentNullException(nameof(spectrum));
            if (spectrum.Length == 0) return Array.Empty<Complex>();
            var data = (Complex[])spectrum.Clone();
            Transform(data, inverse: true);
            double scale = 1.0 / data.Length;
            for (int i = 0; i < data.Length; i++) data[i] *= scale;
            return data;
        }

        /// <summary>True if <paramref name="n"/> is a positive power of two.</summary>
        public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        // Unnormalized transform in place; dispatches by length.
        private static void Transform(Complex[] data, bool inverse)
        {
            int n = data.Length;
            if (n <= 1) return;
            if ((n & (n - 1)) == 0) Radix2(data, inverse);
            else Bluestein(data, inverse);
        }

        // Iterative in-place radix-2 Cooley–Tukey. Requires a power-of-two length. Unnormalized.
        private static void Radix2(Complex[] a, bool inverse)
        {
            int n = a.Length;

            // Bit-reversal permutation.
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { var t = a[i]; a[i] = a[j]; a[j] = t; }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? 1 : -1);
                var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    int half = len >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        Complex u = a[i + k];
                        Complex v = a[i + k + half] * w;
                        a[i + k] = u + v;
                        a[i + k + half] = u - v;
                        w *= wlen;
                    }
                }
            }
        }

        // Bluestein's chirp-z: turns an arbitrary-length DFT into a power-of-two convolution.
        private static void Bluestein(Complex[] data, bool inverse)
        {
            int n = data.Length;
            double dir = inverse ? 1 : -1;

            // Chirp w[k] = exp(dir·iπk²/n). Reduce k² modulo 2n so the angle stays small and accurate.
            var chirp = new Complex[n];
            for (int k = 0; k < n; k++)
            {
                long kk = (long)k * k % (2L * n);
                double ang = dir * Math.PI * kk / n;
                chirp[k] = new Complex(Math.Cos(ang), Math.Sin(ang));
            }

            int m = 1;
            while (m < 2 * n - 1) m <<= 1;

            var a = new Complex[m];
            for (int k = 0; k < n; k++) a[k] = data[k] * chirp[k];

            var b = new Complex[m];
            b[0] = Complex.Conjugate(chirp[0]);
            for (int k = 1; k < n; k++)
            {
                var c = Complex.Conjugate(chirp[k]);
                b[k] = c;
                b[m - k] = c;
            }

            Radix2(a, inverse: false);
            Radix2(b, inverse: false);
            for (int k = 0; k < m; k++) a[k] *= b[k];
            Radix2(a, inverse: true);

            double scale = 1.0 / m;
            for (int k = 0; k < n; k++) data[k] = a[k] * scale * chirp[k];
        }
    }
}
