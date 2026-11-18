using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Signal
{
    // Regressions from review round 137 (signal analysis, eighth pass): each test failed before its fix, but the guards.
    public class SignalReview137Tests
    {
        // A read-only list of n copies of one value, so huge lengths cost nothing.
        private sealed class Repeat : IReadOnlyList<double>
        {
            private readonly double _value;
            public Repeat(int count, double value) { Count = count; _value = value; }
            public int Count { get; }
            public double this[int index] => _value;
            public IEnumerator<double> GetEnumerator() { for (int i = 0; i < Count; i++) yield return _value; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // ---------- SavitzkyGolay ----------

        // Order w − 1 built a w × w basis: 120 s at w = 2001. It fits every window exactly, so the output is the input.
        public void SavitzkyGolayOrderOneBelowTheWindowIsTheIdentity()
        {
            var rng = new Random(137);
            var data = Enumerable.Range(0, 2500).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
            var smoothed = SavitzkyGolay.Smooth(data, 2001, 2000);
            Check.Equal(data.Length, smoothed.Length);
            for (int i = 0; i < data.Length; i++) Check.Equal(data[i], smoothed[i], $"[{i}]");
            // Guards: small windows at order w − 1, and order w − 3 still fits through the general path.
            foreach (int w in new[] { 3, 5, 51 })
            {
                var d = Enumerable.Range(0, w + 7).Select(_ => rng.NextDouble() * 1e300).ToArray();
                Check.True(SavitzkyGolay.Smooth(d, w, w - 1).SequenceEqual(d), $"w = {w}");
            }
            var cubic = Enumerable.Range(0, 40).Select(i => 0.5 * i * i * i - 3.0 * i + 2).ToArray();
            var fitted = SavitzkyGolay.Smooth(cubic, 7, 4);
            for (int i = 0; i < cubic.Length; i++) Check.True(Math.Abs(fitted[i] - cubic[i]) <= 1e-9 * Math.Max(1, Math.Abs(cubic[i])), $"[{i}]");
        }

        // ---------- Array.MaxLength ----------

        // new double[n] past Array.MaxLength threw OutOfMemoryException; Window.Apply named Create's "length".
        public void LongVirtualListsAreArgumentErrors()
        {
            var huge = new Repeat(int.MaxValue, 1);
            Check.Equal("signal", Check.Throws<ArgumentException>(() => MedianFilter.Apply(huge, 3)).ParamName);
            Check.Equal("data", Check.Throws<ArgumentException>(() => SavitzkyGolay.Smooth(huge, 3, 1)).ParamName);
            Check.Equal("samples", Check.Throws<ArgumentException>(() => Window.Apply(huge, WindowType.Hann)).ParamName);
            // Guards: ordinary lengths.
            Check.Equal(5, MedianFilter.Apply(new Repeat(5, 2), 3).Length);
            Check.Equal(5, SavitzkyGolay.Smooth(new Repeat(5, 2), 3, 1).Length);
            Check.Equal(5, Window.Apply(new Repeat(5, 2), WindowType.Hann).Length);
        }

        // ---------- PhaseUnwrap.GroupDelay ----------

        // An infinite frequency passed the order check: [0, 1, ∞] gave a delay of 0 at the finite 1 Hz.
        public void GroupDelayRefusesNonFiniteInput()
        {
            Check.Equal("frequencies", Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new[] { 0.0, -1, -2 }, new[] { 0.0, 1, double.PositiveInfinity })).ParamName);
            Check.Equal("frequencies", Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new[] { 0.0, -1, -2 }, new[] { double.NegativeInfinity, 0, 1 })).ParamName);
            Check.Equal("frequencies", Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new[] { 0.0, -1 }, new[] { 0.0, double.NaN })).ParamName);
            Check.Equal("unwrappedPhase", Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new[] { 0.0, double.NaN, -2 }, new[] { 0.0, 1, 2 })).ParamName);
            Check.Equal("unwrappedPhase", Check.Throws<ArgumentException>(() => PhaseUnwrap.GroupDelay(new[] { 0.0, double.NegativeInfinity }, new[] { 0.0, 1 })).ParamName);
            // Guard: a pure delay of 2 ms, with frequencies spanning ±1e308.
            var f = new[] { 0.0, 10, 25, 40 };
            var tau = PhaseUnwrap.GroupDelay(f.Select(x => -2 * Math.PI * x * 0.002).ToArray(), f);
            foreach (var t in tau) Check.True(Math.Abs(t - 0.002) < 1e-15, $"{t}");
            Check.True(PhaseUnwrap.GroupDelay(new[] { 0.0, 0.0 }, new[] { -1e308, 1e308 }).All(t => t == 0));
        }

        // ---------- Hampel ----------

        // nSigmas = ∞ made the threshold ∞·0 = NaN in a constant window, so the documented rule (flag any point off the
        // median) did not hold; a finite 1e308 flagged it.
        public void HampelRefusesAnInfiniteThreshold()
        {
            var samples = new[] { 1.0, 1, 1, 5, 1, 1, 1 };
            Check.Equal("nSigmas", Check.Throws<ArgumentOutOfRangeException>(() => Hampel.CountOutliers(samples, 2, double.PositiveInfinity)).ParamName);
            Check.Equal("nSigmas", Check.Throws<ArgumentOutOfRangeException>(() => Hampel.Filter(samples, 2, double.PositiveInfinity)).ParamName);
            // Guards.
            Check.Equal(1, Hampel.CountOutliers(samples, 2, 1e308));
            Check.Equal(1, Hampel.CountOutliers(samples, 2, 0));
            Check.Equal("nSigmas", Check.Throws<ArgumentOutOfRangeException>(() => Hampel.CountOutliers(samples, 2, double.NaN)).ParamName);
        }

        // ---------- Window measures ----------

        // An entry of ∞ or NaN gave NaN, where an empty, all-zero or zero-sum window is refused.
        public void WindowMeasuresRefuseNonFiniteEntries()
        {
            foreach (var w in new[] { new[] { 1.0, double.NaN, 1 }, new[] { 1.0, double.PositiveInfinity, 1 }, new[] { double.NegativeInfinity }, new[] { 1.0, double.NaN } })
            {
                Check.Equal("window", Check.Throws<ArgumentException>(() => Window.EquivalentNoiseBandwidth(w)).ParamName);
                Check.Equal("window", Check.Throws<ArgumentException>(() => Window.CoherentGain(w)).ParamName);
            }
            // Guards: the rectangular window is 1 bin wide with unit gain; a Hann window 1.5 bins with gain 1/2.
            Check.Equal(1.0, Window.EquivalentNoiseBandwidth(new[] { 1.0, 1, 1, 1 }));
            Check.Equal(1.0, Window.CoherentGain(new[] { 1.0, 1, 1, 1 }));
            var hann = Window.Create(WindowType.Hann, 4096);
            Check.True(Math.Abs(Window.EquivalentNoiseBandwidth(hann) - 1.5) < 1e-3);
            Check.True(Math.Abs(Window.CoherentGain(hann) - 0.5) < 1e-3);
            Check.Equal(4.9406564584124654E-324, Window.CoherentGain(new[] { double.Epsilon, double.Epsilon }));
        }

        // ---------- WelchPsd and Multitaper ----------

        private static double[] Down1200(double[] psd) => psd.Select(v => v * Math.Pow(2, -600) * Math.Pow(2, -600)).ToArray();

        // Samples were scaled only when their peak was below 2^-500, so at 1e-150 a quiet bin's |X|² underflowed in the
        // sum: s = 1e-162 put bin 2 116% off and bins 0, 3, 4 at 0. The density is degree-2 homogeneous, so 2^600·x must
        // give exactly 2^1200 times it.
        public void SpectraOfTinySignalsKeepTheirQuietBins()
        {
            foreach (double s in new[] { 1e-158, 1e-161, 1e-162 })
            {
                var x = Enumerable.Range(0, 8).Select(i => 1e-150 * Math.Cos(2 * Math.PI * i / 8) + s * Math.Cos(2 * Math.PI * 2 * i / 8)).ToArray();
                var up = x.Select(v => v * Math.Pow(2, 600)).ToArray();
                var psd = WelchPsd.Estimate(x, 1e-100, 8, 0, WindowType.Rectangular).Psd;
                var reference = Down1200(WelchPsd.Estimate(up, 1e-100, 8, 0, WindowType.Rectangular).Psd);
                for (int k = 0; k < psd.Length; k++) Check.Equal(reference[k], psd[k], $"Welch s = {s}, bin {k}");
            }
            // The same invariance for a random record and for Multitaper (whose tapers' leakage kept the probe's bins
            // normal, so this one is a guard).
            var rng = new Random(1137);
            for (int trial = 0; trial < 20; trial++)
            {
                int n = 16 + rng.Next(48);
                double amplitude = Math.Pow(10, -150 - rng.NextDouble() * 10);
                var x = Enumerable.Range(0, n).Select(_ => (rng.NextDouble() * 2 - 1) * amplitude).ToArray();
                var up = x.Select(v => v * Math.Pow(2, 600)).ToArray();
                double fs = Math.Pow(10, rng.NextDouble() * 4 - 2);
                var welch = WelchPsd.Estimate(x, fs, 16, 0.5, WindowType.Hann).Psd;
                var welchRef = Down1200(WelchPsd.Estimate(up, fs, 16, 0.5, WindowType.Hann).Psd);
                for (int k = 0; k < welch.Length; k++) Check.Equal(welchRef[k], welch[k], $"Welch trial {trial}, bin {k}");
                var mt = Multitaper.Estimate(x, fs, 3).Psd;
                var mtRef = Down1200(Multitaper.Estimate(up, fs, 3).Psd);
                for (int k = 0; k < mt.Length; k++) Check.Equal(mtRef[k], mt[k], $"Multitaper trial {trial}, bin {k}");
            }
        }

        // ---------- Convolution ----------

        // x as m·2^e exactly.
        private static (BigInteger Mantissa, int Exponent) Exact(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int biased = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & 0xFFFFFFFFFFFFFL;
            BigInteger m = biased == 0 ? fraction : fraction | (1L << 52);
            int e = biased == 0 ? -1074 : biased - 1075;
            return (bits < 0 ? -m : m, e);
        }

        // The exact sum of the products, in units of 2^-1074, rounded half to even.
        private static BigInteger ExactUnits(IEnumerable<(double X, double Y)> products)
        {
            BigInteger sum = 0;                                                // in units of 2^-2200
            foreach (var (x, y) in products)
            {
                var (mx, ex) = Exact(x);
                var (my, ey) = Exact(y);
                sum += mx * my << (ex + ey + 2200);
            }
            BigInteger unit = BigInteger.One << 1126, q = BigInteger.DivRem(sum, unit, out BigInteger r);
            BigInteger twice = 2 * r;
            if (twice > unit || (twice == unit && !q.IsEven)) q++;
            return q;
        }

        // The subnormal rescue ran only when the whole inputs' peaks multiplied below 2^-969, so an element whose overlap
        // held only tiny values next to a normal peak had each product rounded: [1, x, x, x] ∗ [y, y, y] (0.4ε each) gave
        // 0 for ε, and [1, 2^-538 ×4] ∗ itself gave 0 for ε at elements 5 and 6.
        public void ConvolutionOfTinyOverlapsBesideANormalPeakIsRoundedOnce()
        {
            double x = 0.4 * Math.Pow(2, -1014), y = Math.Pow(2, -60);
            Check.Equal(double.Epsilon, Convolution.Convolve(new[] { 1, x, x, x }, new[] { y, y, y })[3]);
            double t = Math.Pow(2, -538);
            var a = new[] { 1, t, t, t, t };
            var c = Convolution.Convolve(a, a);
            Check.Equal(double.Epsilon, c[5]);
            Check.Equal(double.Epsilon, c[6]);
            Check.Equal(0.0, c[7]);
            Check.Equal(0.0, c[8]);
            var cc = Convolution.CrossCorrelate(new[] { 1, x, x, x }, new[] { y, y, y });
            Check.Equal(double.Epsilon, cc[3]);

            // Differential: elements made only of tiny products are within one unit of the exact sum, rounded once.
            var rng = new Random(2137);
            for (int trial = 0; trial < 300; trial++)
            {
                int na = 2 + rng.Next(8), nb = 2 + rng.Next(8);
                double Tiny() => (0.5 + rng.NextDouble() / 2) * Math.Pow(2, -528 - rng.Next(6)) * (rng.Next(4) == 0 ? -1 : 1);
                var u = new double[na];
                var v = new double[nb];
                u[0] = 1 + rng.NextDouble();
                v[0] = 1 + rng.NextDouble();
                for (int i = 1; i < na; i++) u[i] = Tiny();
                for (int i = 1; i < nb; i++) v[i] = Tiny();
                var got = Convolution.Convolve(u, v);
                for (int k = Math.Max(na, nb); k < got.Length; k++)
                {
                    var pairs = new List<(double, double)>();
                    for (int i = Math.Max(1, k - (nb - 1)); i <= Math.Min(na - 1, k - 1); i++) pairs.Add((u[i], v[k - i]));
                    BigInteger expected = ExactUnits(pairs);
                    Check.True(Math.Abs(got[k]) < 2.2250738585072014e-308, $"trial {trial}, [{k}] = {got[k]}");
                    long bits = BitConverter.DoubleToInt64Bits(Math.Abs(got[k]));
                    BigInteger units = got[k] < 0 ? -(BigInteger)bits : bits;
                    Check.True(BigInteger.Abs(units - expected) <= 1, $"trial {trial}, [{k}]: {units} units for {expected}");
                }
            }
        }
    }
}
