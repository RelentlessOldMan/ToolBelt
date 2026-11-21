using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using Vector2 = ToolBelt.Numerics.Vector2;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Review round 197 (Numerics core, twelfth pass): overflow in Lerp and Remap, the infinite radius, exact
    /// RoundToMultiple, degenerate random sources, NextGaussian's overflow, the subnormal standard deviation, poles in
    /// the bracketing root finders, and the parse and format edges.
    /// </summary>
    public sealed class NumericsCoreReview197Tests
    {
        // A source that replays the given draws, cycling.
        private sealed class Replay : Random
        {
            private readonly double[] _draws;
            private int _i;
            public Replay(params double[] draws) { _draws = draws; }
            public override double NextDouble() => _draws[_i++ % _draws.Length];
            protected override double Sample() => NextDouble();
        }

        private const double OneBelow1 = 1 - 1.1102230246251565E-16; // 1 - 2^-53

        private static double Pow2(int k)
        {
            double r = 1;
            for (; k > 1000; k -= 1000) r *= Math.Pow(2, 1000);
            for (; k < -1000; k += 1000) r *= Math.Pow(2, -1000);
            return r * Math.Pow(2, k);
        }

        public void Lerp_ProductOverflow_IsNotInfinite()
        {
            Check.Equal(-7.5e307, Interpolation.Lerp(1.5e308, 0, 1.5));
            Check.Equal(-7.5e307, Vector2.Lerp(new Vector2(1.5e308, 0), new Vector2(0, 0), 1.5).X);
            // Differential: Lerp is scale-invariant in powers of two, so huge ends agree with the same ends scaled down.
            var rng = new Random(197);
            for (int trial = 0; trial < 3000; trial++)
            {
                double a = (rng.NextDouble() * 2 - 1) * 1.7e308, b = (rng.NextDouble() * 2 - 1) * 1.7e308;
                double t = rng.NextDouble() * 4 - 1.5;
                double small = Interpolation.Lerp(a * Pow2(-600), b * Pow2(-600), t);
                double expected = small * Pow2(600);
                double got = Interpolation.Lerp(a, b, t);
                if (double.IsInfinity(expected) || Math.Abs(expected) > 1.79e308) continue;
                Check.True(Math.Abs(got - expected) <= 4e-16 * Math.Max(Math.Abs(a), Math.Abs(b)) * (1 + Math.Abs(t)),
                    $"Lerp({a:R}, {b:R}, {t:R}) = {got:R}, scaled {expected:R}");
                double vx = Vector2.Lerp(new Vector2(a, 0), new Vector2(b, 0), t).X;
                Check.True(Math.Abs(vx - expected) <= 4e-16 * Math.Max(Math.Abs(a), Math.Abs(b)) * (1 + Math.Abs(t)),
                    $"Vector2.Lerp({a:R}, {b:R}, {t:R}) = {vx:R}, scaled {expected:R}");
            }
        }

        public void Remap_QuotientOutOfRange_StillMaps()
        {
            // 1e-310 is subnormal, so the ratio is taken with it scaled into the normal range.
            double ratio = 1e-300 / (1e-310 * Pow2(64)) * Pow2(64);
            Check.True(Math.Abs(Interpolation.Remap(1, 0, 1e-310, 0, 1e-300) / ratio - 1) < 1e-15, "t overflowed");
            Check.True(Math.Abs(MinMaxScaler.Scale(1, 0, 1e-310, 0, 1e-300) / ratio - 1) < 1e-15, "Scale: t overflowed");
            Check.True(Math.Abs(Interpolation.Remap(1e-320, 0, 1e300, 0, 1e300) - 1e-320) <= 5e-324, "t underflowed");
            Check.True(Math.Abs(MinMaxScaler.Scale(1e-320, 0, 1e300, 0, 1e300) - 1e-320) <= 5e-324, "Scale: t underflowed");
            // Differential: value m1*2^e1 on [0, m2*2^e2] onto [0, m3*2^e3] is (m1*m3/m2)*2^(e1 - e2 + e3).
            var rng = new Random(1971);
            int checkedCount = 0;
            for (int trial = 0; trial < 4000; trial++)
            {
                double m1 = 1 + rng.NextDouble(), m2 = 1 + rng.NextDouble(), m3 = 1 + rng.NextDouble();
                int e1 = rng.Next(-1020, 1020), e2 = rng.Next(-1020, 1020), e3 = rng.Next(-1020, 1020);
                int e = e1 - e2 + e3;
                if (e < -1000 || e > 1000) continue;
                double expected = m1 * m3 / m2 * Pow2(e);
                double value = m1 * Pow2(e1), hi = m2 * Pow2(e2), outHi = m3 * Pow2(e3);
                double r = Interpolation.Remap(value, 0, hi, 0, outHi);
                Check.True(Math.Abs(r / expected - 1) < 1e-15, $"Remap({value:R}, 0, {hi:R}, 0, {outHi:R}) = {r:R}, expected {expected:R}");
                double s = MinMaxScaler.Scale(value, 0, hi, 0, outHi);
                if (value < hi) Check.True(Math.Abs(s / expected - 1) < 1e-15, $"Scale({value:R}, 0, {hi:R}, 0, {outHi:R}) = {s:R}");
                checkedCount++;
            }
            Check.True(checkedCount > 1000, "enough trials");
        }

        public void InverseDistance_InfiniteRadius_KeepsFarSamples()
        {
            var samples = new List<(double X, double Y, double Value)> { (1e308, 1e308, 2) };
            Check.Equal(2.0, ScatteredInterpolation.InverseDistance(samples, -1e308, -1e308, radius: double.PositiveInfinity));
            Check.Equal(2.0, ScatteredInterpolation.InverseDistance(samples, -1e308, -1e308));
            Check.True(double.IsNaN(ScatteredInterpolation.InverseDistance(samples, -1e308, -1e308, radius: double.MaxValue)),
                "a finite radius still excludes it");
        }

        public void RoundToMultiple_ReadsTheDecimals()
        {
            Check.Equal(0.2, Rounding.RoundToMultiple(0.15, 0.1));
            Check.Equal(0.3, Rounding.RoundToMultiple(0.3, 0.1));
            Check.Equal(-0.2, Rounding.RoundToMultiple(-0.15, 0.1));
            Check.Equal(3.7638018064963644E+22, Rounding.RoundToMultiple(3.7638018064963644E+22, 6));
            Check.Equal(-8012905292218.974, Rounding.RoundToMultiple(-8012905292218.973, 0.003));
            Check.True(1 / Rounding.RoundToMultiple(-0.04, 0.1) < 0, "-0.04 rounds to -0");
            Check.Equal(1e300, Rounding.RoundToMultiple(1e300, 1e-10));
            Check.True(double.IsPositiveInfinity(Rounding.RoundToMultiple(1.7e308, 1e308)), "past MaxValue");
            // Differential against decimal: short decimals, whose shortest round-trip form is that decimal.
            var rng = new Random(19700);
            decimal[] multiples = { 0.1m, 0.05m, 0.003m, 7m, 2.5m, 0.25m, 0.00001m, 125m, 0.7m, 3m };
            for (int trial = 0; trial < 20000; trial++)
            {
                decimal dv = rng.Next(-1000000000, 1000000000) / (decimal)Math.Pow(10, rng.Next(0, 7));
                decimal dm = multiples[rng.Next(multiples.Length)];
                decimal expected = Math.Round(dv / dm, MidpointRounding.AwayFromZero) * dm;
                double v = (double)dv;
                // The double read from the decimal's own text, to start from the double nearest it on every framework.
                v = double.Parse(dv.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double r = Rounding.RoundToMultiple(v, (double)dm);
                decimal back = decimal.Parse(r.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                Check.True(back == expected, $"RoundToMultiple({dv}, {dm}) = {r:R}, expected {expected}");
            }
        }

        public void NextGamma_DegenerateSource_ThrowsNotHangs()
        {
            Check.Throws<InvalidOperationException>(() => new Replay(0).NextGamma(2));
            Check.Throws<InvalidOperationException>(() => new Replay(0).NextGamma(4.9e-324));
            // A fair source is unaffected.
            var rng = new Random(5);
            double sum = 0;
            for (int i = 0; i < 20000; i++) sum += rng.NextGamma(3, 2);
            Check.True(Math.Abs(sum / 20000 - 6) < 0.1, "mean of Gamma(3, 2)");
        }

        public void NextPoisson_DegenerateSource_ThrowsNotHangs()
        {
            Check.Throws<InvalidOperationException>(() => new Replay(OneBelow1).NextPoisson(5));
            Check.Throws<InvalidOperationException>(() => new Replay(0).NextPoisson(50));
            Check.Throws<InvalidOperationException>(() => new Replay(OneBelow1).NextPoisson(50));
            Check.Equal(0, new Replay(0).NextPoisson(5));
            var rng = new Random(6);
            double small = 0, large = 0;
            for (int i = 0; i < 20000; i++) { small += rng.NextPoisson(5); large += rng.NextPoisson(50); }
            Check.True(Math.Abs(small / 20000 - 5) < 0.1, "mean of Poisson(5)");
            Check.True(Math.Abs(large / 20000 - 50) < 0.3, "mean of Poisson(50)");
        }

        public void NextGaussian_ProductOverflow_IsNotInfinite()
        {
            // Draws 0.7 then 0: u1 = 0.3, u2 = 0, so z = sqrt(-2 ln 0.3).
            double z = Math.Sqrt(-2.0 * Math.Log(1.0 - 0.7));
            double r = new Replay(0.7, 0).NextGaussian(-1.5e308, 1.5e308);
            double expected = 1.5e308 * (z - 1);
            Check.True(Math.Abs(r / expected - 1) < 1e-14, $"{r:R}, expected {expected:R}");
            Check.True(double.IsPositiveInfinity(new Replay(OneBelow1, 0).NextGaussian(0, 1e308)), "a draw truly past the range");
        }

        public void NextDouble_ReversedBounds_ThrowsOutOfRange()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new Replay(0.5).NextDouble(2, 1));
            Check.Equal("min", ex.ParamName);
        }

        // The double of n quanta (n * 2^-1074), n <= 2^52.
        private static double Quanta(BigInteger n) => BitConverter.Int64BitsToDouble((long)n);

        private static BigInteger RoundedRoot(BigInteger num, BigInteger den)
        {
            BigInteger y = num / den, n = new BigInteger(Math.Sqrt((double)y));
            while (n * n > y) n--;
            while ((n + 1) * (n + 1) <= y) n++;
            BigInteger lhs = 4 * num, rhs = (2 * n + 1) * (2 * n + 1) * den;
            if (lhs > rhs || (lhs == rhs && !n.IsEven)) n++;
            return n;
        }

        public void RunningStatistics_SubnormalStandardDeviation_IsExact()
        {
            var two = new RunningStatistics();
            two.Push(0);
            two.Push(Quanta(15));
            Check.Equal(Quanta(11), two.SampleStandardDeviation, "15/sqrt(2) = 10.6 quanta");
            // Differential: integer quanta k_i, sample SD = sqrt((n*sum k^2 - (sum k)^2) / (n(n - 1))) quanta, rounded once.
            var rng = new Random(1972);
            for (int trial = 0; trial < 3000; trial++)
            {
                int n = rng.Next(2, 7);
                var s = new RunningStatistics();
                BigInteger sum = 0, sumSq = 0;
                for (int i = 0; i < n; i++)
                {
                    long k = rng.Next(-(1 << 20), 1 << 20);
                    s.Push(Quanta(Math.Abs(k)) * Math.Sign(k));
                    sum += k; sumSq += (BigInteger)k * k;
                }
                BigInteger num = n * sumSq - sum * sum;
                Check.Equal(Quanta(RoundedRoot(num, (BigInteger)n * (n - 1))), s.SampleStandardDeviation, $"trial {trial} sample");
                Check.Equal(Quanta(RoundedRoot(num, (BigInteger)n * n)), s.PopulationStandardDeviation, $"trial {trial} population");
            }
        }

        public void Bisection_Brent_PoleIsNotARoot()
        {
            Check.False(RootFinding.Bisection(x => 1 / x, -1, 2).Converged, "Bisection at the pole of 1/x");
            Check.False(RootFinding.Brent(x => 1 / x, -1, 2).Converged, "Brent at the pole of 1/x");
            Check.False(RootFinding.Bisection(x => 1 / (x - 0.3), -1, 2).Converged, "Bisection, pole at 0.3");
            Check.False(RootFinding.Brent(x => 1 / (x - 0.3), -1, 2).Converged, "Brent, pole at 0.3");
            // Real roots still converge, steep ones too.
            Check.True(RootFinding.Bisection(x => x * x - 2, 0, 2).Converged);
            Check.True(RootFinding.Brent(x => x * x - 2, 0, 2).Converged);
            Check.True(RootFinding.Bisection(x => 1e20 * (x - 0.3), -1, 2).Converged);
            Check.True(RootFinding.Brent(x => 1e20 * (x - 0.3), -1, 2).Converged);
            Check.True(RootFinding.Brent(x => Math.Sign(x - 0.3), -1, 2).Converged, "a jump is still a sign change");
        }

        public void EngineeringNotation_NegativeZero_RoundTrips()
        {
            Check.Equal("-0", EngineeringNotation.Format(-0.0));
            Check.Equal("0", EngineeringNotation.Format(0.0));
            Check.True(1 / EngineeringNotation.Parse(EngineeringNotation.Format(-0.0)) < 0, "parses back as -0");
        }

        public void EngineeringNotation_HugeMantissaExponent_SameVerdictWithPrefix()
        {
            Check.True(EngineeringNotation.TryParse("1e-99999999999999999999", out double plain) && plain == 0);
            Check.True(EngineeringNotation.TryParse("1e-99999999999999999999 k", out double prefixed), "with a prefix");
            Check.Equal(0.0, prefixed);
            Check.False(EngineeringNotation.TryParse("1e99999999999999999999 k", out _), "overflow still fails");
            Check.True(EngineeringNotation.TryParse("1.5e3 k", out double ok) && ok == 1.5e6);
        }

        public void FromBase_PastLong_ThrowsNamedOverflow()
        {
            var ex = Check.Throws<OverflowException>(() => BaseConverter.FromBase("10000000000000000", 16));
            Check.True(ex.Message.Contains("10000000000000000"), ex.Message);
            Check.Equal(long.MaxValue, BaseConverter.FromBase("7fffffffffffffff", 16));
            Check.Throws<OverflowException>(() => BaseConverter.FromBase("8000000000000000", 16));
            var rng = new Random(1974);
            for (int trial = 0; trial < 2000; trial++)
            {
                int radix = rng.Next(2, 37);
                long v = (long)(rng.NextDouble() * long.MaxValue);
                if (trial % 10 == 0) v = long.MaxValue - rng.Next(0, 1000);
                Check.Equal(v, BaseConverter.FromBase(BaseConverter.ToBase(v, radix), radix));
            }
            Check.Throws<OverflowException>(() => BaseConverter.FromBase(BaseConverter.ToBase(long.MaxValue, 36) + "0", 36));
        }

        public void UnitConvert_BelowAbsoluteZero_Throws()
        {
            Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => UnitConvert.Convert(-500, "C", "K")).ParamName);
            Check.Throws<ArgumentOutOfRangeException>(() => UnitConvert.Convert(-1, "K", "C"));
            Check.Throws<ArgumentOutOfRangeException>(() => UnitConvert.Convert(-460, "F", "C"));
            Check.Throws<ArgumentOutOfRangeException>(() => UnitConvert.Convert(-300, "C", "F"));
            Check.Equal(0.0, UnitConvert.Convert(-273.15, "C", "K"));
            Check.Equal(-273.15, UnitConvert.Convert(0, "K", "C"));
            Check.True(Math.Abs(UnitConvert.Convert(-459.67, "F", "K")) < 1e-12, "absolute zero in F");
        }
    }
}
