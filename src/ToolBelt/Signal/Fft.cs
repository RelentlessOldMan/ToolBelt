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
            CheckLength(input.Length, nameof(input));
            var data = (Complex[])input.Clone();
            Transform(data, inverse: false, nameof(input));
            return data;
        }

        /// <summary>Forward transform of a real-valued signal (imaginary parts taken as zero).</summary>
        public static Complex[] Forward(double[] real)
        {
            if (real is null) throw new ArgumentNullException(nameof(real));
            if (real.Length == 0) return Array.Empty<Complex>();
            CheckLength(real.Length, nameof(real));
            var data = new Complex[real.Length];
            for (int i = 0; i < real.Length; i++) data[i] = new Complex(real[i], 0);
            Transform(data, inverse: false, nameof(real));
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
            CheckLength(spectrum.Length, nameof(spectrum));
            var data = (Complex[])spectrum.Clone();
            double scale = 1.0 / data.Length;
            // The transform applies 1/N before or with its own scale-back, so a subnormal result is rounded once: by the 2^-600
            // and again by 1/N it was −10ε for −10.53ε, and Bluestein's −0 for −0.58ε at N = 6.
            Transform(data, inverse: true, nameof(spectrum), scale);
            bool finite = true;
            for (int i = 0; i < data.Length; i++) finite &= IsFinite(data[i]);
            // The unscaled sum overflowed ([1e308, 1e308] gave ∞ + NaN·i); scaling first can't, but would lose denormals,
            // so it is only the fallback.
            if (!finite && AllFinite(spectrum))
            {
                for (int i = 0; i < data.Length; i++) data[i] = Scale(spectrum[i], scale);
                Transform(data, inverse: true, nameof(spectrum));
            }
            return data;
        }

        /// <summary>True if <paramref name="n"/> is a positive power of two.</summary>
        public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        // Part by part: Complex × double is a complex product, and ∞ · (s + 0i) gives ∞ + NaN·i (Inverse([∞]) was (∞, NaN)).
        private static Complex Scale(Complex c, double s) => new Complex(c.Real * s, c.Imaginary * s);

        private static bool IsFinite(Complex c)
            => !double.IsNaN(c.Real) && !double.IsInfinity(c.Real) && !double.IsNaN(c.Imaginary) && !double.IsInfinity(c.Imaginary);

        private static bool AllFinite(Complex[] values)
        {
            foreach (var c in values) if (!IsFinite(c)) return false;
            return true;
        }

        // The convolution length is a power of two ≥ 2n − 1; past 2³⁰ it no longer fits an int. Checked before anything is
        // allocated: the copy and Bluestein's chirp (about 17 GB at 2^29 + 1) came first, so the caller got
        // OutOfMemoryException instead.
        private static void CheckLength(int n, string paramName)
        {
            if (!IsPowerOfTwo(n) && 2L * n - 1 > 1 << 30)
                throw new ArgumentException($"A length-{n} transform that is not a power of two is too long (Bluestein needs 2n − 1 ≤ 2^30); pad it to a power of two.", paramName);
        }

        // Unnormalized transform in place, then multiplied by post; dispatches by length.
        private static void Transform(Complex[] data, bool inverse, string paramName, double post = 1)
        {
            int n = data.Length;
            if (n <= 1) { if (post != 1) for (int k = 0; k < n; k++) data[k] = Scale(data[k], post); return; }
            if ((n & (n - 1)) == 0)
            {
                // Subnormal inputs lost bits in the twiddle products ([7ε, 3ε, 5ε, 0, 0, 0, 0, 0] was 8% off in bins 1 and 7),
                // so a tiny input is scaled up by 2^600 (exact) and back, as Bluestein does. Huge inputs overflowed the partial
                // sums (four 1e308s gave NaN in bins that are 0), so from 2^512 they are scaled down by 2^-512 the same way: the
                // sums then stay under 2^543, and what that flushes to 0 is under 2^-1074 of the largest sample.
                double max = 0;
                for (int k = 0; k < n; k++) max = Math.Max(max, Math.Max(Math.Abs(data[k].Real), Math.Abs(data[k].Imaginary)));
                double down = max > 0 && max < 3.054936363499605e-151 ? Math.Pow(2, 600)                // 2^-500
                    : max >= 1.3407807929942597e154 && !double.IsInfinity(max) ? Math.Pow(2, -512) : 1;  // 2^512
                if (down != 1) for (int k = 0; k < n; k++) data[k] = Scale(data[k], down);
                Radix2(data, inverse);
                double back = 1 / down * post;                                          // exact: powers of two
                if (back != 1) for (int k = 0; k < n; k++) data[k] = Scale(data[k], back);
                return;
            }
            Bluestein(data, inverse, paramName, post);
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

            // Twiddles e^(∓2πik/n), each from its own cos/sin: building them by repeated multiplication (w *= wlen) lets the
            // rounding error grow with n, to about 1e-11 at 2²⁰.
            var twiddle = new Complex[n >> 1];
            double step = 2.0 * Math.PI / n * (inverse ? 1 : -1);
            for (int k = 0; k < twiddle.Length; k++) twiddle[k] = new Complex(Math.Cos(step * k), Math.Sin(step * k));

            // Counted by the half-length: at n = 2^30, len <<= 1 wrapped to int.MinValue, still ≤ n, and the loop never ended.
            for (int half = 1; half < n; half <<= 1)
            {
                int len = half << 1, stride = n / len;
                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        Complex u = a[i + k];
                        Complex v = a[i + k + half] * twiddle[k * stride];
                        a[i + k] = u + v;
                        a[i + k + half] = u - v;
                    }
                }
            }
        }

        // Bluestein's chirp-z: turns an arbitrary-length DFT into a power-of-two convolution.
        private static void Bluestein(Complex[] data, bool inverse, string paramName, double post)
        {
            int n = data.Length;
            double dir = inverse ? 1 : -1;

            // A non-finite sample made every bin NaN ([∞, 0, 0] gave NaN everywhere, where the radix-2 path gives ∞ in each
            // bin): the chirp convolution spreads ∞·0 and ∞ − ∞ to all of them. The finite samples go through the
            // convolution; each non-finite one is added to every bin directly afterwards (AddNonFinite).
            int[]? at = null;
            Complex[]? odd = null;
            int count = 0;
            for (int k = 0; k < n; k++) if (!IsFinite(data[k])) count++;
            if (count > 0)
            {
                at = new int[count];
                odd = new Complex[count];
                for (int k = 0, j = 0; k < n; k++)
                {
                    if (IsFinite(data[k])) continue;
                    at[j] = k;
                    odd[j++] = data[k];
                    data[k] = Complex.Zero;
                }
            }

            // Chirp w[k] = exp(dir·iπk²/n). Reduce k² modulo 2n so the angle stays small and accurate.
            var chirp = new Complex[n];
            for (int k = 0; k < n; k++)
            {
                long kk = (long)k * k % (2L * n);
                double ang = dir * Math.PI * kk / n;
                chirp[k] = new Complex(Math.Cos(ang), Math.Sin(ang));
            }

            // CheckLength keeps 2n − 1 ≤ 2³⁰, so the doubling loop can't wrap to 0 and spin forever.
            CheckLength(n, paramName);
            int m = 1;
            while (m < 2 * n - 1) m <<= 1;

            // The convolution's spectra multiply to about n·m times the input, so large inputs overflowed (1e308 at n = 3 gave
            // NaN in every bin) although the transform is representable: the input is scaled to at most 1 by a power of two,
            // which is exact, and scaled back at the end.
            double max = 0;
            for (int k = 0; k < n; k++) max = Math.Max(max, Math.Max(Math.Abs(data[k].Real), Math.Abs(data[k].Imaginary)));
            // Tiny inputs are scaled up the same way: subnormal samples lost bits in the chirp products ([7ε, 3ε, 0] gave
            // 5ε for 6ε). A non-finite maximum is left unscaled: (int)Math.Ceiling(∞) differs between runtimes.
            int exponent = 0;
            if (!double.IsInfinity(max) && !double.IsNaN(max) && (max > 1 || (max > 0 && max < 3.054936363499605e-151)))   // 2^-500
                exponent = (int)Math.Ceiling(Math.Log(max, 2));
            double downHalf = Math.Pow(2, -(exponent / 2)), downRest = Math.Pow(2, -(exponent - exponent / 2));
            double upHalf = Math.Pow(2, exponent / 2), upRest = Math.Pow(2, exponent - exponent / 2);

            var a = new Complex[m];
            for (int k = 0; k < n; k++) a[k] = Scale(Scale(data[k], downHalf), downRest) * chirp[k];

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

            // post (1/N for the inverse) while the values are still near 1, then the scale-back, exact unless subnormal: one
            // rounding there.
            double scale = 1.0 / m;
            for (int k = 0; k < n; k++)
            {
                var v = Scale(a[k], scale) * chirp[k];
                if (post != 1) v = Scale(v, post);
                data[k] = new Complex(Back(v.Real, upHalf, upRest), Back(v.Imaginary, upHalf, upRest));
            }
            if (at != null) AddNonFinite(data, at, odd!, dir, post);
        }

        // Adds x[j]·e^(dir·2πijk/n)·post to every bin k, part by part, for the non-finite samples, as the definition does: a
        // product with an exact zero (a zero part of the sample, or a twiddle on an axis) adds nothing, so ∞ + 0i at j = 0
        // gives ∞ + 0i, not ∞ + NaN·i. A NaN part still makes every part it multiplies NaN. A bin already NaN in both parts
        // is left, which keeps many infinities from costing n per sample.
        private static void AddNonFinite(Complex[] data, int[] at, Complex[] x, double dir, double post)
        {
            int n = data.Length;
            for (int k = 0; k < n; k++)
            {
                double re = data[k].Real, im = data[k].Imaginary;
                for (int j = 0; j < at.Length && !(double.IsNaN(re) && double.IsNaN(im)); j++)
                {
                    long r = (long)at[j] * k % n;
                    double c, s;
                    if (r * 4 % n == 0)
                    {
                        int q = (int)(r * 4 / n);                       // a quarter turn: exact
                        c = q == 0 ? 1 : q == 2 ? -1 : 0;
                        s = q == 1 ? dir : q == 3 ? -dir : 0;
                    }
                    else
                    {
                        double ang = dir * 2 * Math.PI * r / n;
                        c = Math.Cos(ang);
                        s = Math.Sin(ang);
                    }
                    re += (Times(x[j].Real, c) - Times(x[j].Imaginary, s)) * post;
                    im += (Times(x[j].Real, s) + Times(x[j].Imaginary, c)) * post;
                }
                data[k] = new Complex(re, im);
            }
        }

        // The scaled part times 2^e. A bin whose value is the largest double came back a rounding error over it, and the
        // scale-back overflowed: Forward([Max, 0, 0]) gave ∞ in bins 1 and 2. Within 2^-40 of it, the part is taken as the
        // largest double; one truly past it is still ∞.
        private static double Back(double s, double upHalf, double upRest)
        {
            double r = s * upHalf * upRest;
            if (!double.IsInfinity(r) || double.IsInfinity(s) || double.IsNaN(s)) return r;
            double limit = double.MaxValue / upRest / upHalf;                // exact: powers of two
            return Math.Abs(s) <= limit * (1 + 9.094947017729282E-13) ? Math.Sign(s) * double.MaxValue : r;
        }

        // a·b, but 0 where one is an exact zero and the other is not NaN (∞·0 is a term the sum does not have).
        private static double Times(double a, double b)
            => (a == 0 && !double.IsNaN(b)) || (b == 0 && !double.IsNaN(a)) ? 0 : a * b;
    }
}
