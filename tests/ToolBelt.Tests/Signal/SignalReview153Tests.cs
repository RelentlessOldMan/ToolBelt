using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 153 (signal analysis, ninth pass): each test failed before its fix.
    public class SignalReview153Tests
    {
        private sealed class Repeat : IReadOnlyList<double>
        {
            private readonly double _value;
            public Repeat(int count, double value) { Count = count; _value = value; }
            public int Count { get; }
            public double this[int index] => _value;
            public IEnumerator<double> GetEnumerator() { for (int i = 0; i < Count; i++) yield return _value; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // The DFT by definition, part by part, a product with an exact zero (a zero part or a twiddle on an axis) adding
        // nothing: the reference for non-finite samples.
        private static Complex[] NaiveDft(Complex[] x, bool inverse)
        {
            int n = x.Length;
            double dir = inverse ? 1 : -1;
            var result = new Complex[n];
            for (int k = 0; k < n; k++)
            {
                double re = 0, im = 0;
                for (int j = 0; j < n; j++)
                {
                    long r = (long)j * k % n;
                    double c, s;
                    if (r * 4 % n == 0)
                    {
                        int q = (int)(r * 4 / n);
                        c = q == 0 ? 1 : q == 2 ? -1 : 0;
                        s = q == 1 ? dir : q == 3 ? -dir : 0;
                    }
                    else { c = Math.Cos(dir * 2 * Math.PI * r / n); s = Math.Sin(dir * 2 * Math.PI * r / n); }
                    re += Times(x[j].Real, c) - Times(x[j].Imaginary, s);
                    im += Times(x[j].Real, s) + Times(x[j].Imaginary, c);
                }
                result[k] = inverse ? new Complex(re / n, im / n) : new Complex(re, im);
            }
            return result;
        }

        private static double Times(double a, double b)
            => (a == 0 && !double.IsNaN(b)) || (b == 0 && !double.IsNaN(a)) ? 0 : a * b;

        private static void SamePart(double expected, double actual, string where)
        {
            if (double.IsNaN(expected) || double.IsInfinity(expected)) Check.True(expected.Equals(actual), $"{where}: {actual} for {expected}");
            else Check.True(Math.Abs(expected - actual) <= 1e-9 * (1 + Math.Abs(expected)), $"{where}: {actual} for {expected}");
        }

        // ---------- Fft ----------

        // Bluestein's chirp convolution spread ∞·0 and ∞ − ∞ to every bin, so one infinite sample made the whole transform
        // NaN at every length that is not a power of two ([∞, 0, 0] → NaN ×3; the radix-2 path gives ∞ ×4 for [∞, 0, 0, 0]).
        public void Fft_InfiniteSamplesAtOtherLengthsGiveTheDefinitionsInfinities()
        {
            Check.True(Fft.Forward(new[] { double.PositiveInfinity, 0, 0 }).All(c => double.IsPositiveInfinity(c.Real) && c.Imaginary == 0));
            Check.True(Fft.Inverse(new[] { new Complex(double.PositiveInfinity, 0), Complex.Zero, Complex.Zero })
                .All(c => double.IsPositiveInfinity(c.Real) && c.Imaginary == 0));
            Check.True(Fft.Forward(new[] { double.PositiveInfinity, 0, 0, 0 }).All(c => double.IsPositiveInfinity(c.Real) && c.Imaginary == 0));
            var rng = new Random(153);
            double[] odd = { double.PositiveInfinity, double.NegativeInfinity, double.NaN };
            foreach (int n in new[] { 3, 5, 6, 7, 9, 10, 12, 15 })
            {
                for (int trial = 0; trial < 40; trial++)
                {
                    var x = new Complex[n];
                    for (int j = 0; j < n; j++) x[j] = new Complex(rng.Next(-5, 6), rng.Next(3) == 0 ? rng.Next(-5, 6) : 0);
                    int count = 1 + rng.Next(2);
                    for (int t = 0; t < count; t++)
                    {
                        int at = rng.Next(n);
                        double v = odd[rng.Next(trial % 4 == 0 ? 3 : 2)];
                        x[at] = rng.Next(2) == 0 ? new Complex(v, 0) : new Complex(x[at].Real, v);
                    }
                    foreach (bool inverse in new[] { false, true })
                    {
                        var expected = NaiveDft(x, inverse);
                        var actual = inverse ? Fft.Inverse(x) : Fft.Forward(x);
                        for (int k = 0; k < n; k++)
                        {
                            SamePart(expected[k].Real, actual[k].Real, $"n={n} t={trial} inv={inverse} k={k} re");
                            SamePart(expected[k].Imaginary, actual[k].Imaginary, $"n={n} t={trial} inv={inverse} k={k} im");
                        }
                    }
                }
            }
        }

        // A bin worth exactly double.MaxValue came back a rounding error over it, and the scale-back overflowed to ∞.
        public void Fft_ABinWorthTheLargestDoubleStaysFinite()
        {
            foreach (int n in new[] { 3, 5, 6, 7, 9 })
            {
                var x = new double[n];
                x[0] = double.MaxValue;
                foreach (var c in Fft.Forward(x))
                {
                    Check.True(!double.IsInfinity(c.Real) && Math.Abs(c.Real - double.MaxValue) <= 1e-12 * double.MaxValue, $"n={n}: {c}");
                    Check.True(c.Imaginary == 0 || Math.Abs(c.Imaginary) <= 1e-12 * double.MaxValue, $"n={n}: {c}");
                }
            }
            foreach (int n in new[] { 5, 9 })
            {
                var all = Enumerable.Repeat(new Complex(double.MaxValue, 0), n).ToArray();
                var back = Fft.Inverse(all);
                Check.True(!double.IsInfinity(back[0].Real) && Math.Abs(back[0].Real - double.MaxValue) <= 1e-12 * double.MaxValue, $"n={n}: {back[0]}");
            }
            // A bin truly past it is still ∞.
            Check.True(double.IsPositiveInfinity(Fft.Forward(new[] { double.MaxValue, double.MaxValue, double.MaxValue })[0].Real));
        }

        // ---------- GroupDelay ----------

        // On an uneven grid the secant over [f(i−1), f(i+1)] is the slope at their midpoint: φ = −πf² (τ = f) at
        // f = [0, 1, 10] gave 5 at f = 1. Graded against the quadratic's exact delay at random uneven grids.
        public void GroupDelay_AnUnevenGridGivesTheDelayAtEachPoint()
        {
            double[] tau = PhaseUnwrap.GroupDelay(new[] { 0.0, -Math.PI, -100 * Math.PI }, new[] { 0.0, 1, 10 });
            Check.Close(1.0, tau[1], 1e-12);
            var rng = new Random(1531);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = 3 + rng.Next(10);
                var f = new double[n];
                f[0] = rng.NextDouble() * 10 - 5;
                for (int i = 1; i < n; i++) f[i] = f[i - 1] + 0.01 + rng.NextDouble() * (rng.Next(2) == 0 ? 1 : 50);
                double a = rng.NextDouble() * 4 - 2, t0 = rng.NextDouble() * 3 - 1;
                // φ = −2π(t0·f + a·f²/2): τ = t0 + a·f
                var phase = f.Select(v => -2 * Math.PI * (t0 * v + a * v * v / 2)).ToArray();
                double[] got = PhaseUnwrap.GroupDelay(phase, f);
                double scale = 1 + Math.Abs(a) * f.Max(Math.Abs) + Math.Abs(t0);
                for (int i = 1; i < n - 1; i++)
                    Check.True(Math.Abs(got[i] - (t0 + a * f[i])) <= 1e-9 * scale, $"trial {trial} i={i}: {got[i]} for {t0 + a * f[i]}");
            }
            // A pure delay on an uneven grid is still exact.
            double[] pure = PhaseUnwrap.GroupDelay(new[] { 0.0, -2 * Math.PI * 0.25, -2 * Math.PI * 2.5 }, new[] { 0.0, 1, 10 });
            Check.True(pure.All(v => v == 0.25), string.Join(", ", pure));
        }

        // Past Array.MaxLength the result threw OutOfMemoryException after the whole input was checked.
        public void GroupDelay_MoreThanAnArrayHoldsIsRefused()
        {
            var e = Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new Repeat(int.MaxValue, 0), new Repeat(int.MaxValue, 1)));
            Check.Equal("unwrappedPhase", e.ParamName);
        }

        // ---------- WelchPsd ----------

        // The step was rounded to the nearest, ties to even, so the real overlap moved either way: 0.75 at 10 samples was
        // 0.8 (step 2) and 0.5 at 5 samples was 0.6, where SciPy's n // 2 gives step 3. The overlap now rounds down.
        public void Welch_TheOverlapRoundsDownToWholeSamples()
        {
            var rng = new Random(1532);
            var x = Enumerable.Range(0, 297).Select(_ => rng.NextDouble() - 0.5).ToArray();
            void Same(int length, double overlap, double exact)
            {
                var a = WelchPsd.Estimate(x, 10, length, overlap).Psd;
                var b = WelchPsd.Estimate(x, 10, length, exact).Psd;
                Check.True(a.SequenceEqual(b), $"L={length}: overlap {overlap} is not {exact}");
            }
            Same(10, 0.75, 0.7);      // noverlap 7, step 3
            Same(5, 0.5, 0.4);        // noverlap 2, step 3
            Same(9, 0.5, 4.0 / 9);    // noverlap 4, step 5
            Same(100, 0.29, 0.29);    // 29, though 0.29 · 100 is 28.999999999999996
        }

        // ---------- TimeDelayEstimate ----------

        // The inputs were divided by their peaks, which rounds: past 1e140 or under 1e-140 an exact tie between two lags
        // went to the far one (lag −8 for 1 at 2^497 and 2^-608). Graded against the unscaled lag over random trials.
        public void CrossCorrelation_ATieGoesToTheNearestLagAtAnyScale()
        {
            double[] a = { 1, 2, 1, -3, -2, 0, 0, 1, -1, 0, 3, 1, -1, 0, 2 };
            double[] b = { -3, 0, 2, 2, -3, 1, -2, 1, -3 };
            foreach (double s in new[] { 1, Math.Pow(2, 10), Math.Pow(2, 497), Math.Pow(2, -608) })
            {
                Check.Equal(1, TimeDelayEstimate.CrossCorrelationLag(a, b.Select(v => v * s).ToArray()));
                Check.Equal(1, TimeDelayEstimate.CrossCorrelationPeak(a, b.Select(v => v * s).ToArray()).Lag);
            }
            var rng = new Random(1533);
            for (int trial = 0; trial < 300; trial++)
            {
                var r = Enumerable.Range(0, 3 + rng.Next(14)).Select(_ => (double)rng.Next(-3, 4)).ToArray();
                var d = Enumerable.Range(0, 3 + rng.Next(14)).Select(_ => (double)rng.Next(-3, 4)).ToArray();
                if (r.All(v => v == 0) || d.All(v => v == 0)) continue;
                int lag = TimeDelayEstimate.CrossCorrelationLag(r, d);
                int e = (rng.Next(2) == 0 ? 1 : -1) * (141 + rng.Next(800));
                double s = Math.Pow(2, e);
                Check.Equal(lag, TimeDelayEstimate.CrossCorrelationLag(r, d.Select(v => v * s).ToArray()));
                Check.Equal(lag, TimeDelayEstimate.CrossCorrelationLag(r.Select(v => v * s).ToArray(), d));
            }
        }

        // ---------- SavitzkyGolay ----------

        // The weights sum to a hair over 1, so a constant double.MaxValue came back ∞ (334 of 420 window and order pairs up
        // to 41), although a polynomial passes through unchanged.
        public void SavitzkyGolay_AConstantLargestDoubleStaysFinite()
        {
            for (int w = 3; w <= 41; w += 2)
                for (int order = 0; order < w && order <= 6; order++)
                {
                    var data = Enumerable.Repeat(double.MaxValue, w + 4).ToArray();
                    double[] got = SavitzkyGolay.Smooth(data, w, order);
                    Check.True(got.All(v => !double.IsInfinity(v) && Math.Abs(v - double.MaxValue) <= 1e-12 * double.MaxValue),
                        $"w={w} order={order}: {string.Join(", ", got)}");
                }
        }

        // ---------- Hampel ----------

        // A NaN whose window median is NaN (−∞ and +∞ either side) was "replaced" by NaN and counted.
        public void Hampel_ANaNLeftNaNIsNotCounted()
        {
            double[] x = { double.NegativeInfinity, double.NaN, double.PositiveInfinity };
            Check.Equal(0, Hampel.CountOutliers(x, 1));
            Check.True(double.IsNaN(Hampel.Filter(x, 1)[1]));
            Check.Equal(1, Hampel.CountOutliers(new[] { 1.0, double.NaN, 1.0 }, 1));
            Check.Equal(0, Hampel.CountOutliers(new[] { double.NaN }, 1));
        }
    }
}
