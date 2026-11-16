using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Round 95 (numerics core, sixth pass): each regression failed on the code before its fix.
    public sealed class NumericsCoreReview95Tests
    {
        private static void Rel(double expected, double actual, double tolerance, string? message = null)
        {
            if (!(Math.Abs(actual - expected) <= tolerance * Math.Abs(expected)))
                throw new CheckFailedException((message is null ? "" : message + " — ") + $"expected {expected:R}, got {actual:R}");
        }

        // x·2^e in steps that stay in range (a reference helper, accurate to an ulp or two).
        private static double Scale(double x, int e)
        {
            while (e > 1000) { x *= Math.Pow(2, 1000); e -= 1000; }
            while (e < -1000) { x *= Math.Pow(2, -1000); e += 1000; }
            return x * Math.Pow(2, e);
        }

        // ---- Fraction.ToDouble: a subnormal result is rounded once ----

        public void Fraction_ASubnormalResultIsRoundedOnce()
        {
            Check.Equal(double.Epsilon, new Fraction((BigInteger.One << 59) * 3 - 1, BigInteger.One << 1134).ToDouble());
            Check.Equal(-double.Epsilon, new Fraction(-((BigInteger.One << 59) * 3 - 1), BigInteger.One << 1134).ToDouble());
        }

        // n / 2^k below the normal range is n·2^(1074 − k) units of 2^-1074, rounded half to even.
        public void Fraction_SubnormalResultsMatchACorrectlyRoundedReference()
        {
            var rng = new Random(95);
            for (int trial = 0; trial < 400; trial++)
            {
                int bits = rng.Next(1, 64);
                BigInteger n = (BigInteger)(long)(rng.NextDouble() * Math.Pow(2, bits)) | BigInteger.One;
                if (rng.Next(3) == 0) n = (n << 10) | ((BigInteger.One << 9) * (rng.Next(2) + 1) - rng.Next(2));   // near a tie
                int k = BitLengthOf(n) + rng.Next(1024, 1080);
                int drop = k - 1074;
                BigInteger units = n << Math.Max(0, -drop);
                if (drop > 0)
                {
                    units = n >> drop;
                    BigInteger rest = n - (units << drop), half = BigInteger.One << (drop - 1);
                    if (rest > half || (rest == half && !units.IsEven)) units += 1;
                }
                double want = (double)units * double.Epsilon;
                Check.Equal(want, new Fraction(n, BigInteger.One << k).ToDouble(), $"{n} / 2^{k}");
            }
        }

        private static int BitLengthOf(BigInteger v)
        {
            int bits = 0;
            while (v > 0) { v >>= 1; bits++; }
            return bits;
        }

        // ---- Polynomial: coefficients that span more than the double range ----

        public void Quadratic_CoefficientsBeyondTheDoubleRangeKeepTheirRoots()
        {
            double[] r = new Polynomial(-1e-300, 0, 1e300).RealRoots();
            Check.Equal(2, r.Length, string.Join(", ", r));
            Rel(-1e-300, r[0], 1e-14);
            Rel(1e-300, r[1], 1e-14);
            r = new Polynomial(-1e-200, 1e-100, 1e200).RealRoots();
            Check.Equal(2, r.Length, string.Join(", ", r));
            Rel(-1e-200, r[0], 1e-14);
            Rel(1e-200, r[1], 1e-14);
        }

        // a(x − r1)(x − r2) for a, r1 and r2 of any size whose coefficients are finite: the roots come back.
        public void Quadratic_RootsOfAnySizeComeBack()
        {
            var rng = new Random(951);
            int checkedCount = 0;
            for (int trial = 0; trial < 4000 && checkedCount < 1500; trial++)
            {
                int ea = rng.Next(-1000, 1001), e1 = rng.Next(-1000, 1001), e2 = rng.Next(-1000, 1001);
                if (Math.Abs(e1 - e2) < 2) continue;   // well separated: the coefficients' rounding moves each root by an ulp or so
                int big = Math.Max(e1, e2);
                if (ea + e1 + e2 < -1000 || ea + e1 + e2 > 1000 || ea + big < -1000 || ea + big > 1000) continue;
                // Products formed from the mantissas: a·r1 alone can fall below the normal range and lose c's digits.
                double ma = (1 + rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
                double m1 = (1 + rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
                double m2 = (1 + rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
                double a = Scale(ma, ea), r1 = Scale(m1, e1), r2 = Scale(m2, e2);
                double b = -(Scale(ma * m1, ea + e1) + Scale(ma * m2, ea + e2)), c = Scale(ma * m1 * m2, ea + e1 + e2);
                if (double.IsInfinity(b) || double.IsInfinity(c) || b == 0 || c == 0) continue;
                double[] roots = new Polynomial(c, b, a).RealRoots();
                Check.Equal(2, roots.Length, $"{a:R}, {r1:R}, {r2:R}: {string.Join(", ", roots)}");
                double lo = Math.Min(r1, r2), hi = Math.Max(r1, r2);
                Rel(lo, roots[0], 1e-12, $"{a:R}, {r1:R}, {r2:R}");
                Rel(hi, roots[1], 1e-12, $"{a:R}, {r1:R}, {r2:R}");
                checkedCount++;
            }
            Check.True(checkedCount > 500, $"only {checkedCount} cases");
        }

        public void Cubic_ATinyMonicConstantKeepsItsRoot()
        {
            double[] r = new Polynomial(-1e-300, 0, 0, 1e300).RealRoots();
            Check.Equal(1, r.Length, string.Join(", ", r));
            Rel(1e-200, r[0], 1e-14);
        }

        // a(x³ − r³) has the one real root r, for a and r of any size whose coefficients are finite.
        public void Cubic_TheRealCubeRootOfAnySizeComesBack()
        {
            var rng = new Random(952);
            int checkedCount = 0;
            for (int trial = 0; trial < 4000 && checkedCount < 1000; trial++)
            {
                int ea = rng.Next(-1020, 1021), er = rng.Next(-600, 601);
                if (ea + 3 * er < -1020 || ea + 3 * er > 1020) continue;
                double ma = (1 + rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
                double mr = (1 + rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
                double a = Scale(ma, ea), root = Scale(mr, er);
                double c0 = -Scale(ma * mr * mr * mr, ea + 3 * er);
                if (double.IsInfinity(c0) || c0 == 0) continue;
                double[] roots = new Polynomial(c0, 0, 0, a).RealRoots();
                Check.Equal(1, roots.Length, $"{a:R}, {root:R}: {string.Join(", ", roots)}");
                Rel(root, roots[0], 1e-12, $"{a:R}, {root:R}");
                checkedCount++;
            }
            Check.True(checkedCount > 300, $"only {checkedCount} cases");
        }

        // ---- WeightedAverage: a tiny weight on a huge value ----

        public void WeightedAverage_ATinyWeightOnAHugeValueCounts()
        {
            Rel(1e308 * 1e-300 / 1e300, WeightedAverage.Compute(new[] { 1e308, 0.0 }, new[] { 1e-300, 1e300 }), 1e-14);
            Rel(1e308 * 1e-300 / 1e300, WeightedAverage.Compute(new[] { 0.0, 1e308 }, new[] { 1e300, 1e-300 }), 1e-14);
        }

        // Positive values and weights of any size against an exact rational reference.
        public void WeightedAverage_MatchesAnExactReference()
        {
            var rng = new Random(953);
            for (int trial = 0; trial < 1000; trial++)
            {
                int count = rng.Next(1, 6);
                var values = new double[count];
                var weights = new double[count];
                for (int i = 0; i < count; i++)
                {
                    values[i] = Scale(1 + rng.NextDouble(), rng.Next(-1000, 1001));
                    weights[i] = Scale(1 + rng.NextDouble(), rng.Next(-1000, 1001));
                }
                double want = ExactMean(values, weights);
                if (want == 0 || double.IsInfinity(want) || Math.Abs(want) < 1e-300) continue;
                Rel(want, WeightedAverage.Compute(values, weights), 1e-12,
                    "values " + string.Join(", ", values.Select(v => v.ToString("R"))) + "; weights " + string.Join(", ", weights.Select(v => v.ToString("R"))));
            }
        }

        // Σ wv / Σ w with every double as an exact multiple of 2^-1074.
        private static double ExactMean(double[] values, double[] weights)
        {
            BigInteger num = 0, den = 0;
            for (int i = 0; i < values.Length; i++)
            {
                BigInteger w = Units(weights[i]);
                num += Units(values[i]) * w;
                den += w;
            }
            // num / den is the mean in units of 2^-1074: a 64-bit quotient, then scaled back.
            int shift = 64 - (BitLength(num) - BitLength(den));
            BigInteger q = shift >= 0 ? (num << shift) / den : num / (den << -shift);
            return Scale((double)q, -shift - 1074);
        }

        private static BigInteger Units(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp != 0) mant |= 1L << 52; else exp = 1;
            return (BigInteger)mant << (exp - 1);   // v = mant·2^(exp − 1075) = (mant·2^(exp − 1))·2^-1074
        }

        private static int BitLength(BigInteger v)
        {
            int bits = 0;
            byte[] bytes = v.ToByteArray();
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            bits = top * 8;
            for (int b = bytes[top]; b != 0; b >>= 1) bits++;
            return bits;
        }

        // ---- LinearRegression and ZScore: data a few ulps apart ----

        public void LinearRegression_DataAFewUlpsApartFitsExactly()
        {
            double u = Math.Pow(2, -52);
            LinearFit fit = LinearRegression.Fit(new[] { 1.0, 1 + u }, new[] { 0.0, 1.0 });
            Rel(Math.Pow(2, 52), fit.Slope, 1e-12);
            Rel(1, fit.RSquared, 1e-12);
            fit = LinearRegression.Fit(new[] { 1.0, 1 + u, 1 + 2 * u, 1 + 3 * u }, new[] { 0.0, 1, 2, 3 });
            Rel(Math.Pow(2, 52), fit.Slope, 1e-12);
            Rel(1, fit.RSquared, 1e-12);
        }

        public void ZScore_DataAFewUlpsApartIsCentred()
        {
            double u = Math.Pow(2, -52);
            double[] z = ZScore.Standardize(new[] { 1.0, 1 + u });
            Check.Close(-1, z[0], 1e-12);
            Check.Close(1, z[1], 1e-12);
            z = ZScore.Standardize(new[] { 1.0, 1 + u, 1 + 2 * u, 1 + 3 * u });
            for (int k = 0; k < 4; k++) Check.Close((k - 1.5) / Math.Sqrt(1.25), z[k], 1e-12, $"z[{k}]");
        }

        // ---- DeterministicRandom.NextInt64: uniform over a range near 2^64 ----

#if NET8_0_OR_GREATER
        public void NextInt64_AHugeRangeIsUniform()
        {
            var rng = new DeterministicRandom(95);
            const int Draws = 30000;
            int firstThird = 0;
            for (int i = 0; i < Draws; i++)
                if (rng.NextInt64(long.MinValue, 1L << 62) < long.MinValue + (1L << 62)) firstThird++;
            Check.True(Math.Abs(firstThird / (double)Draws - 1 / 3.0) < 0.02, $"first third of the range: {firstThird} of {Draws}");
        }

        public void NextInt64_ASmallRangeKeepsItsSequence()
        {
            var a = new DeterministicRandom(7);
            var b = new DeterministicRandom(7);
            for (int i = 0; i < 1000; i++)
                Check.Equal((long)(b.NextUInt64() % 1000) - 500, a.NextInt64(-500, 500));
        }
#endif

        // ---- RandomUtils.NextPoisson: a zero draw at the edge of the hat ----

        private sealed class Scripted : Random
        {
            private readonly Queue<double> _draws;
            public Scripted(params double[] draws) => _draws = new Queue<double>(draws);
            public override double NextDouble() => _draws.Count > 0 ? _draws.Dequeue() : 0.5;
            protected override double Sample() => NextDouble();
        }

        // u at the edge (|u| = 0.5 − 2^-53) and v = 0 made log v = −∞ accept a kd near 1e15, cast to int.MinValue.
        public void NextPoisson_AZeroDrawIsNotACountOfIntMinValue()
        {
            foreach (double lambda in new[] { 30.0, 1000.0, 1e6 })
            {
                int k = new Scripted(1 - Math.Pow(2, -53), 0.0).NextPoisson(lambda);
                Check.True(k >= 0 && Math.Abs(k - lambda) <= 10 * Math.Sqrt(lambda), $"lambda {lambda}: {k}");
            }
        }
    }
}
