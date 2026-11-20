using System;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 168 (signal analysis, tenth pass): each test failed before its fix.
    public class SignalReview168Tests
    {
        // ∞ · e^(dir·2πi·r/n), with ∞ · 0 = 0 for the parts a quarter turn makes exactly zero.
        private static Complex Term(double x, long r, int n, int dir)
        {
            double c, s;
            if (r * 4 % n == 0)
            {
                int q = (int)(r * 4 / n);
                c = q == 0 ? 1 : q == 2 ? -1 : 0;
                s = q == 1 ? dir : q == 3 ? -dir : 0;
            }
            else { c = Math.Cos(dir * 2 * Math.PI * r / n); s = Math.Sin(dir * 2 * Math.PI * r / n); }
            return new Complex(c == 0 ? 0 : x * c, s == 0 ? 0 : x * s);
        }

        // The radix-2 butterflies spread ∞ · 0 and ∞ − ∞: Forward([0, ∞, 0, 0]) gave (∞, NaN) in bins whose imaginary
        // part is 0 (the Bluestein lengths were already right).
        public void Fft_PowerOfTwo_InfiniteSample_FollowsTheDefinition()
        {
            foreach (int n in new[] { 2, 4, 8, 16, 32 })
                for (int j = 0; j < n; j++)
                    foreach (double inf in new[] { double.PositiveInfinity, double.NegativeInfinity })
                    {
                        var x = new double[n];
                        x[j] = inf;
                        Complex[] f = Fft.Forward(x);
                        var spectrum = new Complex[n];
                        spectrum[j] = inf;
                        Complex[] g = Fft.Inverse(spectrum);
                        for (int k = 0; k < n; k++)
                        {
                            Check.Equal(Term(inf, (long)j * k % n, n, -1), f[k], "forward n " + n + " at " + j + " bin " + k);
                            Check.Equal(Term(inf, (long)j * k % n, n, 1), g[k], "inverse n " + n + " at " + j + " bin " + k);
                        }
                        // With finite neighbours no bin is NaN either.
                        var y = new double[n];
                        for (int i = 0; i < n; i++) y[i] = i + 1;
                        y[j] = inf;
                        Check.True(Fft.Forward(y).All(c => !double.IsNaN(c.Real) && !double.IsNaN(c.Imaginary)), "neighbours n " + n + " at " + j);
                    }
        }

        // Complex.Magnitude of (∞, NaN) is NaN on .NET Core and ∞ on .NET Framework, so the same input gave NaN on one and
        // ∞ on the other.
        public void Spectra_WithAnInfinitePart_AreInfinite()
        {
            Check.Equal(double.PositiveInfinity, Spectrum.Amplitude(new[] { new Complex(double.PositiveInfinity, double.NaN), 0, 0, 0 })[0], "amplitude");
            Check.Equal(double.PositiveInfinity, Spectrum.Amplitude(new[] { new Complex(double.NaN, double.NegativeInfinity), 0, 0, 0 })[0], "amplitude, imaginary");
            double[] x = { 0, double.PositiveInfinity, 0, 0, 0, 0, 0, 0 };
            Check.True(Spectrum.Amplitude(Fft.Forward(x)).All(double.IsPositiveInfinity), "amplitude of the transform");
            Check.True(WelchPsd.Estimate(x, 1, 8).Psd.All(double.IsPositiveInfinity), "welch");
            Check.True(Multitaper.Estimate(x, 1, 3).Psd.All(double.IsPositiveInfinity), "multitaper");
        }

        // Rounding noise of −2e-16 in the imaginary part made the phase of a negative constant exactly −π, outside the
        // documented (−π, π].
        public void InstantaneousPhase_StaysInTheDocumentedRange()
        {
            for (int n = 1; n <= 100; n++)
                foreach (double v in new[] { -1.0, -2, -0.3, -1e10 })
                {
                    double[] p = Hilbert.InstantaneousPhase(Enumerable.Repeat(v, n).ToArray());
                    Check.True(p.All(a => a > -Math.PI && a <= Math.PI), "n " + n + " value " + v);
                }
        }

        // On the subnormal grid the threshold rounded (2.9652ε to 3ε), so an outlier was missed that is found at scale 1.
        // Scaling by a power of two changes nothing else, so the counts must agree.
        public void Hampel_SubnormalData_CountsAsAtScaleOne()
        {
            double[] x = { -2, -2, 0, 3, 0, 0, 2 };
            Check.Equal(1, Hampel.CountOutliers(x, 3, 1), "scale 1");
            Check.Equal(1, Hampel.CountOutliers(x.Select(v => v * double.Epsilon).ToArray(), 3, 1), "scale ε");
            var rng = new Random(168);
            double[] sigmas = { 0.5, 1, 1.5, 2, 2.5, 3 };
            for (int trial = 0; trial < 5000; trial++)
            {
                var y = new double[rng.Next(3, 20)];
                for (int i = 0; i < y.Length; i++) y[i] = rng.Next(-6, 7);
                int h = rng.Next(1, 4);
                double s = sigmas[rng.Next(sigmas.Length)];
                double[] tiny = y.Select(v => v * double.Epsilon).ToArray();
                Check.Equal(Hampel.CountOutliers(y, h, s), Hampel.CountOutliers(tiny, h, s), "trial " + trial);
                double[] a = Hampel.Filter(y, h, s), b = Hampel.Filter(tiny, h, s);
                for (int i = 0; i < y.Length; i++) Check.Equal(a[i] * double.Epsilon, b[i], "trial " + trial + " sample " + i);
            }
        }
    }
}
