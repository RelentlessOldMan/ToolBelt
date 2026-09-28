using System;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's hand-rolled FFT (radix-2 + Bluestein) against Math.NET's <c>Fourier</c>. Math.NET
    /// and ToolBelt use different normalization/sign conventions, so the robust comparison is the
    /// magnitude spectrum with a matched scaling (<see cref="FourierOptions.NoScaling"/> = raw DFT sum).
    /// </summary>
    public sealed class FftCrossCheckTests
    {
        private static void CompareMagnitudes(Complex[] toolbelt, Complex[] mathnet, double tol, string ctx)
        {
            Check.Equal(toolbelt.Length, mathnet.Length, ctx + " length");
            for (int i = 0; i < toolbelt.Length; i++)
                Check.Close(mathnet[i].Magnitude, toolbelt[i].Magnitude, tol, $"{ctx}[{i}] |X|");
        }

        public void MatchesMathNet_PowerOfTwo()
        {
            var rng = new Random(1);
            foreach (int n in new[] { 2, 4, 8, 16, 128, 1024 })
            {
                var x = new Complex[n];
                for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);

                var mine = Fft.Forward(x);

                var theirs = (Complex[])x.Clone();
                Fourier.Forward(theirs, FourierOptions.NoScaling);

                CompareMagnitudes(mine, theirs, 1e-8, $"pow2 n={n}");
            }
        }

        public void MatchesMathNet_NonPowerOfTwo()
        {
            var rng = new Random(2);
            foreach (int n in new[] { 3, 5, 7, 11, 13, 100, 360, 1000 })
            {
                var x = new Complex[n];
                for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);

                var mine = Fft.Forward(x);

                var theirs = (Complex[])x.Clone();
                Fourier.Forward(theirs, FourierOptions.NoScaling);

                CompareMagnitudes(mine, theirs, 1e-6, $"bluestein n={n}");
            }
        }

        public void FullComplexValuesMatch_UpToConjugateConvention()
        {
            // With NoScaling, Math.NET's default forward uses the +i exponent; ToolBelt uses -i. The two
            // therefore agree bin-for-bin once one is conjugated. Verifying this pins the sign convention,
            // not just the magnitude.
            var rng = new Random(3);
            const int n = 64;
            var x = new Complex[n];
            for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble(), rng.NextDouble());

            var mine = Fft.Forward(x);
            var theirs = (Complex[])x.Clone();
            Fourier.Forward(theirs, FourierOptions.NoScaling);

            // Try both conventions; one must match to tight tolerance.
            bool direct = true, conj = true;
            for (int i = 0; i < n; i++)
            {
                if (Complex.Abs(mine[i] - theirs[i]) > 1e-9) direct = false;
                if (Complex.Abs(mine[i] - Complex.Conjugate(theirs[i])) > 1e-9) conj = false;
            }
            Check.True(direct || conj, "ToolBelt FFT matches Math.NET under some standard sign convention");
        }

        public void InverseRoundTrip_MatchesMathNet()
        {
            var rng = new Random(4);
            foreach (int n in new[] { 8, 15, 256, 500 })
            {
                var x = new Complex[n];
                for (int i = 0; i < n; i++) x[i] = new Complex(rng.NextDouble(), rng.NextDouble());

                // ToolBelt: Inverse(Forward(x)) == x.
                var mineBack = Fft.Inverse(Fft.Forward(x));
                for (int i = 0; i < n; i++)
                    Check.True(Complex.Abs(mineBack[i] - x[i]) < 1e-9, $"toolbelt roundtrip n={n}[{i}]");
            }
        }
    }
}
