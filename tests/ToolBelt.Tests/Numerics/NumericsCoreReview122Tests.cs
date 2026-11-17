using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using V2 = ToolBelt.Numerics.Vector2;

namespace ToolBelt.Tests.Numerics
{
    // Round 122 (numerics core, seventh pass): each regression failed on the code before its fix.
    public sealed class NumericsCoreReview122Tests
    {
        // A double times 2¹⁰⁷⁴, exactly.
        private static BigInteger Exact(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long man = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else man |= 1L << 52;
            BigInteger m = new BigInteger(man) << (exp - 1);
            return bits < 0 ? -m : m;
        }

        // Finite values across the whole range: near ±MaxValue, subnormal, random binades, small integers, moderate.
        private static double Wide(Random r)
        {
            switch (r.Next(6))
            {
                case 0: return (r.NextDouble() - 0.5) * 2 * double.MaxValue;
                case 1: return (r.NextDouble() - 0.5) * 1e-305;
                case 2: return Math.Pow(2, r.Next(-1074, 1024)) * (r.Next(2) * 2 - 1) * (1 + r.NextDouble()) / 2;
                case 3: return r.Next(-3, 4);
                default: return r.NextDouble() * 100 - 50;
            }
        }

        private static double[] SortedDistinct(Random r, int n)
            => Enumerable.Range(0, n).Select(_ => Wide(r)).Where(v => !double.IsInfinity(v)).Distinct().OrderBy(v => v).ToArray();

        // ---------- Lerp ends ----------

        // a + (b − a)·1 lost a small b next to a large a: Lerp(1e16, 1, 1) was 0.
        public void Lerp_EndsAreExactAndTheInteriorStaysBetween()
        {
            Check.Equal(1.0, Interpolation.Lerp(1e16, 1, 1));
            Check.Equal(1.0, V2.Lerp(new V2(1e16, 0), new V2(1, 0), 1).X);
            Check.Equal(1.0, Interpolation.Remap(5, 0, 5, 1e16, 1));
            var rng = new Random(122);
            for (int i = 0; i < 20000; i++)
            {
                double a = Wide(rng), b = Wide(rng), t = rng.NextDouble();
                Check.Equal(a, Interpolation.Lerp(a, b, 0));
                Check.Equal(b, Interpolation.Lerp(a, b, 1));
                var v = V2.Lerp(new V2(a, b), new V2(b, a), 1);
                Check.True(v.X == b && v.Y == a, $"Vector2 end {a:R} {b:R}");
                double m = Interpolation.Lerp(a, b, t);
                Check.True(m >= Math.Min(a, b) && m <= Math.Max(a, b), $"Lerp({a:R}, {b:R}, {t:R}) = {m:R}");
            }
        }

        // ---------- Quantile ranks ----------

        // p·(n − 1) rounded: Quantile(0.29) over 0..100 was 28.999999999999996, where Percentile gives 29.
        public void Quantile_AtARankFraction_IsThatSample()
        {
            var data = Enumerable.Range(0, 101).Select(i => (double)i).ToArray();
            var e = new EmpiricalDistribution(data);
            for (int j = 0; j <= 100; j++)
            {
                Check.Equal(Percentile.Compute(data, j), e.Quantile(j / 100.0), "p " + j);
                Check.Equal((double)j, e.Quantile(j / 100.0), "p " + j);
            }
            var rng = new Random(7);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = 2 + rng.Next(300);
                var s = Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 1000).OrderBy(v => v).ToArray();
                var d = new EmpiricalDistribution(s);
                for (int k = 0; k < n; k++)
                    Check.Equal(s[k], d.Quantile((double)k / (n - 1)), $"n {n} k {k}");
            }
        }

        // ---------- Angle ----------

        // ±∞ normalized to NaN everywhere (BUGLIST R95-2); a step that overflows did too.
        public void Angle_Infinity_IsRejected()
        {
            foreach (double inf in new[] { double.PositiveInfinity, double.NegativeInfinity })
            {
                Check.Equal("degrees", Check.Throws<ArgumentOutOfRangeException>(() => Angle.NormalizeDegrees(inf)).ParamName);
                Check.Equal("degrees", Check.Throws<ArgumentOutOfRangeException>(() => Angle.NormalizeSignedDegrees(inf)).ParamName);
                Check.Equal("from", Check.Throws<ArgumentOutOfRangeException>(() => Angle.DifferenceDegrees(inf, 10)).ParamName);
                Check.Equal("to", Check.Throws<ArgumentOutOfRangeException>(() => Angle.DifferenceDegrees(10, inf)).ParamName);
                Check.Equal("from", Check.Throws<ArgumentOutOfRangeException>(() => Angle.LerpDegrees(inf, 10, 0.5)).ParamName);
                Check.Equal("t", Check.Throws<ArgumentOutOfRangeException>(() => Angle.LerpDegrees(10, 20, inf)).ParamName);
            }
            Check.Equal("t", Check.Throws<ArgumentOutOfRangeException>(() => Angle.LerpDegrees(10, 20, 1e308)).ParamName);
            Check.True(double.IsNaN(Angle.NormalizeDegrees(double.NaN)), "NaN still passes through");
            Check.Equal(10.0, Angle.LerpDegrees(10, 10, 1e308));
            Check.Equal(30.0, Angle.LerpDegrees(10, 20, 2));
        }

        // ---------- MovingAverage ----------

        // One compensation term could not hold three magnitudes: {1e300, 1e200, 1, 1, 1} in a window of 3 ended at 0.
        public void MovingAverage_MatchesTheExactWindowMean()
        {
            var ma = new MovingAverage(3);
            foreach (double v in new[] { 1e300, 1e200, 1, 1 }) ma.Add(v);
            Check.Equal(1.0, ma.Add(1));

            var rng = new Random(3);
            foreach (int w in new[] { 1, 2, 3, 5, 17, 64 })
                for (int rep = 0; rep < 4; rep++)
                {
                    var m = new MovingAverage(w);
                    var window = new Queue<double>();
                    for (int i = 0; i < 400; i++)
                    {
                        double v = rng.Next(3) == 0 ? rng.Next(-2, 3) : Wide(rng);
                        double got = m.Add(v);
                        window.Enqueue(v);
                        if (window.Count > w) window.Dequeue();
                        BigInteger sum = BigInteger.Zero, abs = BigInteger.Zero;
                        foreach (double s in window) { var e = Exact(s); sum += e; abs += BigInteger.Abs(e); }
                        Check.True(!double.IsNaN(got) && !double.IsInfinity(got), $"w {w} step {i}: {got:R}");
                        // |avg·n − Σ| within a few ulps of the sum, plus a subnormal unit per sample.
                        BigInteger err = BigInteger.Abs(Exact(got) * window.Count - sum);
                        Check.True(err * (BigInteger.One << 50) <= BigInteger.Abs(sum) + (new BigInteger(window.Count * 8) << 50),
                            $"w {w} step {i}: {got:R} for [{string.Join(", ", window.Select(z => z.ToString("R")))}]");
                    }
                }
        }

        // ---------- Harmonic ----------

        // Never held in [min, max]: near-equal values landed an ulp above the largest, and near MaxValue gave ∞.
        public void Harmonic_StaysInsideTheData()
        {
            double max = double.MaxValue, near = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(max) - 6);
            double h = Means.Harmonic(new[] { max, near, max, max, near });
            Check.True(h >= near && h <= max, h.ToString("R"));
            var rng = new Random(5);
            for (int i = 0; i < 50000; i++)
            {
                double b = Math.Abs(Wide(rng));
                if (!(b > 0)) continue;
                long bits = BitConverter.DoubleToInt64Bits(b);
                var v = Enumerable.Range(0, 2 + rng.Next(6)).Select(_ => BitConverter.Int64BitsToDouble(Math.Max(1, bits + rng.Next(-8, 9))))
                    .Where(z => z > 0 && !double.IsInfinity(z)).ToArray();
                if (v.Length == 0) continue;
                double hm = Means.Harmonic(v);
                Check.True(hm >= v.Min() && hm <= v.Max(), $"[{string.Join(", ", v.Select(z => z.ToString("R")))}] → {hm:R}");
            }
        }

        // ---------- undefined enums and NaN bounds ----------

        public void UndefinedEnums_AndNaNDataBounds_AreRejected()
        {
            var x = new[] { 0.0, 1 };
            Check.Equal("policy", Check.Throws<ArgumentOutOfRangeException>(() => new LookupTable(x, x, (Extrapolation)42)).ParamName);
            Check.Equal("rule", Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Round(1.234, 0.097, (UncertaintyRule)9)).ParamName);
            Check.Equal("rule", Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Format(1.234, 0.097, rule: (UncertaintyRule)9)).ParamName);
            Check.Equal("style", Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Format(1.234, 0.097, (UncertaintyStyle)9)).ParamName);
            Check.Equal("dataMin", Check.Throws<ArgumentOutOfRangeException>(() => MinMaxScaler.Scale(0.5, double.NaN, 1, 0, 1)).ParamName);
            Check.Equal("dataMax", Check.Throws<ArgumentOutOfRangeException>(() => MinMaxScaler.Scale(0.5, 0, double.NaN, 0, 1)).ParamName);
        }

        // ---------- matrix product size ----------

        // 100000x0 by 0x100000 threw OutOfMemoryException from two empty inputs.
        public void Multiply_ProductPastArrayMaxLength_IsRejected()
        {
            Check.Equal("b", Check.Throws<ArgumentException>(() => LinearAlgebra.Multiply(new double[100000, 0], new double[0, 100000])).ParamName);
            var p = LinearAlgebra.Multiply(new double[3, 0], new double[0, 2]);
            Check.True(p.GetLength(0) == 3 && p.GetLength(1) == 2 && p.Cast<double>().All(v => v == 0), "an empty inner dimension gives zeros");
        }

        // ---------- Trapezoid overflow ----------

        // Finite panels past double range summed as +∞ and −∞ to NaN (the reviewer's case integrates to about −5.5e615).
        public void Trapezoid_OverflowingPanels_MatchTheExactSum()
        {
            var xs = new[] { -9.321e307, -8.953e307, -1.160, -4.37e-223, 9.95, 33.76 };
            var ys = new[] { -17.36, -17.71, -1.222e308, 7.784e307, -1.72e93, -1.713e308 };
            Check.Equal(double.NegativeInfinity, Integration.Trapezoid(xs, ys));
            Check.Equal(0.0, Integration.Trapezoid(new[] { 1e308, 1e308, 0, -1e308, -1e308 }, 10));

            var rng = new Random(11);
            BigInteger max2 = Exact(double.MaxValue) << 1075, twelve = BigInteger.Pow(10, 12);
            void Grade(double got, BigInteger s2, BigInteger a2, string what)
            {
                Check.True(!double.IsNaN(got), what + " is NaN");
                if (double.IsInfinity(got))
                    Check.True(Math.Sign(got) == s2.Sign && BigInteger.Abs(s2) * twelve + a2 >= max2 * twelve, what + " overflowed wrongly: " + got);
                else
                    Check.True(BigInteger.Abs((Exact(got) << 1075) - s2) * twelve <= a2 + ((BigInteger.One << 1082) * twelve), what + " = " + got.ToString("R"));
            }
            for (int t = 0; t < 4000; t++)
            {
                var x = SortedDistinct(rng, 2 + rng.Next(5));
                if (x.Length < 2) continue;
                var y = x.Select(_ => Wide(rng)).ToArray();
                string desc = $"x=[{string.Join(",", x.Select(v => v.ToString("R")))}] y=[{string.Join(",", y.Select(v => v.ToString("R")))}]";
                BigInteger s2 = 0, a2 = 0, su = 0, au = 0, dx = Exact(10);
                for (int i = 1; i < x.Length; i++)
                {
                    BigInteger p = (Exact(y[i - 1]) + Exact(y[i])) * (Exact(x[i]) - Exact(x[i - 1]));
                    s2 += p; a2 += BigInteger.Abs(p);
                    BigInteger q = (Exact(y[i - 1]) + Exact(y[i])) * dx;
                    su += q; au += BigInteger.Abs(q);
                }
                Grade(Integration.Trapezoid(x, y), s2, a2, "uneven " + desc);
                Grade(Integration.Trapezoid(y, 10), su, au, "uniform " + desc);
            }
        }

        // ---------- Sample derivative overflow ----------

        // A slope past double range at the end of a 1e308-wide grid came out ∞ − ∞ = NaN.
        public void SampleDerivative_FiniteData_IsNeverNaN()
        {
            var x0 = new[] { -1.2426e308, 2.76e-306, 4.29e-306, 10.64, 33.25 };
            var y0 = new[] { -41.68, -3.958e307, 4.11e-308, -1.5496e308, 1.227e-306 };
            Check.Equal(double.NegativeInfinity, Differentiation.Sample(x0, y0)[0]);

            var rng = new Random(11);
            BigInteger mx = new BigInteger(double.MaxValue), big = BigInteger.One << 1000;
            for (int t = 0; t < 4000; t++)
            {
                var x = SortedDistinct(rng, 3 + rng.Next(4));
                if (x.Length < 3) continue;
                int n = x.Length;
                var y = x.Select(_ => Wide(rng)).ToArray();
                var d = Differentiation.Sample(x, y);
                for (int i = 0; i < n; i++)
                {
                    int c = Math.Min(Math.Max(i, 1), n - 2);
                    BigInteger X0 = Exact(x[c - 1]), X1 = Exact(x[c]), X2 = Exact(x[c + 1]), A = Exact(x[i]);
                    BigInteger F0 = Exact(y[c - 1]), F1 = Exact(y[c]), F2 = Exact(y[c + 1]);
                    BigInteger S = X2 - X0, N2 = 2 * A - X0 - X1, N1 = S - N2;
                    // D = P / Q exactly: s01·(1 − r) + s12·r with r = N2 / S.
                    BigInteger P = (F1 - F0) * N1 * (X2 - X1) + (F2 - F1) * N2 * (X1 - X0), Q = (X1 - X0) * (X2 - X1) * S;
                    string what = $"d[{i}] = {d[i]:R} for x=[{string.Join(",", x.Select(v => v.ToString("R")))}] y=[{string.Join(",", y.Select(v => v.ToString("R")))}]";
                    Check.True(!double.IsNaN(d[i]), what);
                    if (double.IsInfinity(d[i]))
                        Check.True(Math.Sign(d[i]) == P.Sign && BigInteger.Abs(P) >= Q * big, "overflowed wrongly: " + what);
                    if (BigInteger.Abs(P) * 1000000 > Q * mx * 1000001)
                        Check.True(double.IsInfinity(d[i]) && Math.Sign(d[i]) == P.Sign, "past double range: " + what);
                }
            }
        }
    }
}
