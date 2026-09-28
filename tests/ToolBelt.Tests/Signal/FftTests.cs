using System;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    public sealed class FftTests
    {
        // Naive O(n^2) DFT — the reference every fast path is graded against.
        private static Complex[] DirectDft(Complex[] x, bool inverse)
        {
            int n = x.Length;
            var result = new Complex[n];
            double dir = inverse ? 1 : -1;
            for (int k = 0; k < n; k++)
            {
                Complex sum = Complex.Zero;
                for (int j = 0; j < n; j++)
                {
                    double ang = dir * 2 * Math.PI * k * j / n;
                    sum += x[j] * new Complex(Math.Cos(ang), Math.Sin(ang));
                }
                result[k] = sum;
            }
            return result;
        }

        private static void CloseSpectrum(Complex[] expected, Complex[] actual, double tol, string ctx)
        {
            Check.Equal(expected.Length, actual.Length, ctx + " length");
            for (int i = 0; i < expected.Length; i++)
            {
                Check.Close(expected[i].Real, actual[i].Real, tol, $"{ctx}[{i}].Re");
                Check.Close(expected[i].Imaginary, actual[i].Imaginary, tol, $"{ctx}[{i}].Im");
            }
        }

        public void MatchesDirectDft_PowerOfTwo()
        {
            var rng = new Random(101);
            foreach (int n in new[] { 2, 4, 8, 16, 64, 256 })
            {
                var x = new Complex[n];
                for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
                CloseSpectrum(DirectDft(x, false), Fft.Forward(x), 1e-9, $"radix2 n={n}");
            }
        }

        // The Bluestein path — the whole point of supporting arbitrary lengths.
        public void MatchesDirectDft_NonPowerOfTwo()
        {
            var rng = new Random(202);
            foreach (int n in new[] { 3, 5, 6, 7, 9, 10, 12, 15, 17, 100, 121 })
            {
                var x = new Complex[n];
                for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
                CloseSpectrum(DirectDft(x, false), Fft.Forward(x), 1e-7, $"bluestein n={n}");
            }
        }

        public void RoundTrips_AnyLength()
        {
            var rng = new Random(303);
            foreach (int n in new[] { 1, 2, 3, 8, 13, 64, 100 })
            {
                var x = new Complex[n];
                for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble(), rng.NextDouble());
                var back = Fft.Inverse(Fft.Forward(x));
                for (int i = 0; i < n; i++)
                {
                    Check.Close(x[i].Real, back[i].Real, 1e-9, $"n={n} [{i}].Re");
                    Check.Close(x[i].Imaginary, back[i].Imaginary, 1e-9, $"n={n} [{i}].Im");
                }
            }
        }

        public void PureToneLandsInSingleBin()
        {
            const int n = 32;
            const int bin = 5;
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = Math.Cos(2 * Math.PI * bin * i / n);
            var spec = Fft.Forward(x);
            // A real cosine at bin k has magnitude N/2 at bins k and N-k, ~0 elsewhere.
            for (int k = 0; k <= n / 2; k++)
            {
                double expected = k == bin ? n / 2.0 : 0.0;
                Check.Close(expected, spec[k].Magnitude, 1e-9, $"bin {k}");
            }
        }

        // Parseval: energy is preserved (up to the 1/N convention) between time and frequency domains.
        public void SatisfiesParseval()
        {
            var rng = new Random(404);
            const int n = 48; // non-power-of-two on purpose
            var x = new double[n];
            double timeEnergy = 0;
            for (int i = 0; i < n; i++) { x[i] = rng.NextDouble() * 2 - 1; timeEnergy += x[i] * x[i]; }

            var spec = Fft.Forward(x);
            double freqEnergy = 0;
            for (int k = 0; k < n; k++) freqEnergy += spec[k].Magnitude * spec[k].Magnitude;
            Check.Close(timeEnergy, freqEnergy / n, 1e-9);
        }

        public void Linearity()
        {
            var rng = new Random(505);
            const int n = 20;
            var a = new Complex[n];
            var b = new Complex[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = new Complex(rng.NextDouble(), rng.NextDouble());
                b[i] = new Complex(rng.NextDouble(), rng.NextDouble());
            }
            var sum = new Complex[n];
            for (int i = 0; i < n; i++) sum[i] = 3 * a[i] - 2 * b[i];

            var fa = Fft.Forward(a);
            var fb = Fft.Forward(b);
            var fsum = Fft.Forward(sum);
            for (int i = 0; i < n; i++)
            {
                var combined = 3 * fa[i] - 2 * fb[i];
                Check.Close(combined.Real, fsum[i].Real, 1e-9, $"[{i}].Re");
                Check.Close(combined.Imaginary, fsum[i].Imaginary, 1e-9, $"[{i}].Im");
            }
        }

        public void ConvolutionTheorem()
        {
            // Circular convolution via FFT matches the direct circular convolution.
            var rng = new Random(606);
            const int n = 16;
            var a = new double[n];
            var b = new double[n];
            for (int i = 0; i < n; i++) { a[i] = rng.NextDouble(); b[i] = rng.NextDouble(); }

            var fa = Fft.Forward(a);
            var fb = Fft.Forward(b);
            var prod = new Complex[n];
            for (int i = 0; i < n; i++) prod[i] = fa[i] * fb[i];
            var conv = Fft.Inverse(prod);

            for (int k = 0; k < n; k++)
            {
                double direct = 0;
                for (int j = 0; j < n; j++) direct += a[j] * b[(k - j + n) % n];
                Check.Close(direct, conv[k].Real, 1e-9, $"k={k}");
            }
        }

        public void IsPowerOfTwo_Classifies()
        {
            Check.True(Fft.IsPowerOfTwo(1));
            Check.True(Fft.IsPowerOfTwo(2));
            Check.True(Fft.IsPowerOfTwo(1024));
            Check.False(Fft.IsPowerOfTwo(0));
            Check.False(Fft.IsPowerOfTwo(3));
            Check.False(Fft.IsPowerOfTwo(-4));
        }

        public void EmptyInputs()
        {
            Check.Equal(0, Fft.Forward(Array.Empty<Complex>()).Length);
            Check.Equal(0, Fft.Forward(Array.Empty<double>()).Length);
            Check.Equal(0, Fft.Inverse(Array.Empty<Complex>()).Length);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Fft.Forward((Complex[])null!));
            Check.Throws<ArgumentNullException>(() => Fft.Forward((double[])null!));
            Check.Throws<ArgumentNullException>(() => Fft.Inverse(null!));
        }
    }
}
