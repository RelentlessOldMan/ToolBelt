using System;
using System.Globalization;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Review round 209 (Numerics core, thirteenth pass): Polynomial.Evaluate at infinity, the quadratic's cancelling
    /// discriminant, WeightedAverage's rounded quotient, Uncertainty.Format's exponent just below a power of ten, and
    /// AdaptiveSimpson's overflow retry.
    /// </summary>
    public sealed class NumericsCoreReview209Tests
    {
        private static double Pow2(int k)
        {
            double r = 1;
            for (; k > 1000; k -= 1000) r *= Math.Pow(2, 1000);
            for (; k < -1000; k += 1000) r *= Math.Pow(2, -1000);
            return r * Math.Pow(2, k);
        }

        // The limit of p(x) as x goes to +-inf: the leading term's sign.
        private static double Limit(double[] c, double x)
        {
            int n = c.Length - 1;
            while (n > 0 && c[n] == 0) n--;
            if (n == 0) return c[0];
            bool negative = (c[n] < 0) ^ (x < 0 && n % 2 == 1);
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        public void Polynomial_Evaluate_AtInfinity_FollowsTheLeadingTerm()
        {
            var rng = new Random(2091);
            for (int t = 0; t < 2000; t++)
            {
                var c = new double[1 + rng.Next(6)];
                for (int i = 0; i < c.Length; i++) c[i] = rng.Next(3) == 0 ? 0 : (rng.Next(2001) - 1000) / 8.0;
                if (c[c.Length - 1] == 0) c[c.Length - 1] = 1.5;
                var p = new Polynomial(c);
                foreach (double x in new[] { double.PositiveInfinity, double.NegativeInfinity })
                    Check.Equal(Limit(c, x), p.Evaluate(x), $"p = [{string.Join(", ", c)}] at {x}");
            }
            Check.Equal(5.0, new Polynomial(5).Evaluate(double.PositiveInfinity));
            Check.True(double.IsNaN(new Polynomial(1, 2).Evaluate(double.NaN)));
        }

        public void Polynomial_QuadraticRoots_CloseRoots_AreExact()
        {
            // (x - r1)(x - r2) with r = 1 + i 2^-26 (scaled by 2^s): every coefficient is exact, so the roots are too.
            var rng = new Random(2092);
            int wrong = 0;
            string? first = null;
            for (int t = 0; t < 20000; t++)
            {
                int i = rng.Next(1, 1 << 12), j = rng.Next(1, 1 << 12);
                if (i == j) continue;
                double s = Pow2(rng.Next(-200, 201));
                double r1 = (1 + i * Pow2(-26)) * s, r2 = (1 + j * Pow2(-26)) * s;
                if (rng.Next(2) == 0) { r1 = -r1; r2 = -r2; }
                double lo = Math.Min(r1, r2), hi = Math.Max(r1, r2);
                var roots = new Polynomial(r1 * r2, -(r1 + r2), 1).RealRoots();
                if (roots.Length != 2 || roots[0] != lo || roots[1] != hi)
                {
                    wrong++;
                    if (first == null)
                        first = $"{lo:R}, {hi:R} gave [{string.Join(", ", Array.ConvertAll(roots, r => r.ToString("R", CultureInfo.InvariantCulture)))}]";
                }
            }
            Check.Equal(0, wrong, first);
        }

        // |x - n/d| times d 2^1074, exactly.
        private static BigInteger Distance(double x, BigInteger n, BigInteger d)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            BigInteger xs = (BigInteger)m * BigInteger.Pow(2, be - 1);
            if (bits < 0) xs = -xs;
            return BigInteger.Abs(xs * d - n * BigInteger.Pow(2, 1074));
        }

        private static double Neighbour(double x, int dir)
        {
            if (x == 0) return dir > 0 ? double.Epsilon : -double.Epsilon;
            long bits = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(bits + ((x > 0) == (dir > 0) ? 1 : -1));
        }

        public void WeightedAverage_SmallMeanOfLargerValues_IsCorrectlyRounded()
        {
            Check.Equal(1.0 / 68, WeightedAverage.Compute(
                new[] { -97.75, 79.625, 122, 30.875, -97.375, 47.625, -24.5 }, new double[] { 2, 1, 3, 3, 3, 1, 4 }));
            var rng = new Random(2093);
            int wrong = 0;
            string? first = null;
            for (int t = 0; t < 20000; t++)
            {
                int len = rng.Next(2, 11);
                var v = new double[len];
                var w = new double[len];
                BigInteger num = 0, den = 0;
                for (int i = 0; i < len; i++)
                {
                    int k = rng.Next(-1000, 1001), wi = rng.Next(1, 5);
                    v[i] = k / 8.0; w[i] = wi;
                    num += (BigInteger)k * wi; den += 8 * wi;
                }
                double got = WeightedAverage.Compute(v, w);
                BigInteger dist = Distance(got, num, den);
                if (Distance(Neighbour(got, 1), num, den) < dist || Distance(Neighbour(got, -1), num, den) < dist)
                {
                    wrong++;
                    if (first == null) first = $"[{string.Join(", ", v)}] weighted [{string.Join(", ", w)}] gave {got:R}";
                }
            }
            Check.Equal(0, wrong, first);
        }

        public void Uncertainty_Format_JustBelowAPowerOfTen_KeepsTheMantissaInRange()
        {
            Check.Equal("(9.9999999999999970 ± 0.0000000000000010)e22", Uncertainty.Format(9.999999999999997e22, 1e7));
            var rng = new Random(2094);
            int wrong = 0, checkedCount = 0;
            string? first = null;
            for (int t = 0; t < 3000; t++)
            {
                int k = rng.Next(-300, 301);
                if (k > -8 && k < 8) continue;
                double p = double.Parse("1e" + k.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double v = p;
                for (int s = rng.Next(1, 4); s > 0; s--) v = Neighbour(v, -1);
                if (rng.Next(2) == 0) v = -v;
                double u = Math.Abs(v) * Math.Pow(10, -rng.Next(3, 17));
                string text = Uncertainty.Format(v, u);
                int open = text.IndexOf('(');
                if (open < 0) continue;
                checkedCount++;
                string mantissa = text.Substring(open + 1, text.IndexOf(' ') - open - 1).TrimStart('-');
                if (!(mantissa.Length > 0 && mantissa[0] >= '1' && mantissa[0] <= '9' && (mantissa.Length == 1 || mantissa[1] == '.')))
                {
                    wrong++;
                    if (first == null) first = $"Format({v:R}, {u:R}) = {text}";
                }
            }
            Check.True(checkedCount > 1000, checkedCount.ToString(CultureInfo.InvariantCulture));
            Check.Equal(0, wrong, first);
        }

        public void AdaptiveSimpson_OverflowRetry_KeepsSmallSamples()
        {
            Check.Equal(double.PositiveInfinity, Integration.AdaptiveSimpson(x => 1, -1e308, 1e308));
            Check.Equal(double.NegativeInfinity, Integration.AdaptiveSimpson(x => 1, 1e308, -1e308));
            Check.Equal(double.NegativeInfinity, Integration.AdaptiveSimpson(x => -1, -1e308, 1e308));
            Check.Equal(double.PositiveInfinity, Integration.AdaptiveSimpson(x => 2, -1.7e308, 1.7e308));
            Check.Equal(double.PositiveInfinity, Integration.AdaptiveSimpson(x => 1 + x / 1e300, -1e308, 1e308));
            // c + x/10^k over [-L, L]: the odd part overflows the half-range pieces and cancels, leaving 2Lc in range.
            var rng = new Random(2095);
            for (int t = 0; t < 40; t++)
            {
                double c = 0.1 + 0.4 * rng.NextDouble(), L = 1e308 + 0.7e308 * rng.NextDouble();
                double scale = Math.Pow(10, rng.Next(303, 307));
                if (rng.Next(2) == 0) c = -c;
                double exact = 2 * (L * c), got = Integration.AdaptiveSimpson(x => c + x / scale, -L, L);
                Check.True(Math.Abs(got - exact) <= 1e-9 * Math.Abs(exact), $"c = {c:R}, L = {L:R}, scale = {scale:R}: {got:R} vs {exact:R}");
            }
        }
    }
}
