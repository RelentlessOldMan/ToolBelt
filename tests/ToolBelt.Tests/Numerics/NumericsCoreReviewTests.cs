using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using Vector2 = ToolBelt.Numerics.Vector2;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 4 (numerics core). References are mpmath unless noted.
    public sealed class NumericsCoreReviewTests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        private static T Within<T>(int ms, Func<T> f, string what)
        {
            var t = Task.Run(f);
            Check.True(Task.WhenAny(t, Task.Delay(ms)).Result == t, $"{what} must finish within {ms} ms");
            return t.GetAwaiter().GetResult(); // rethrows the original exception, not an AggregateException
        }

        // A Random whose NextDouble always returns the same value, to hit rounding edges deterministically.
        private sealed class FixedRandom : Random
        {
            private readonly double _u;
            public FixedRandom(double u) { _u = u; }
            public override double NextDouble() => _u;
        }

        // ---- Polynomial ----

        public void Polynomial_Fit_IsAccurateForOffsetAndWideX()
        {
            // Normal equations squared the condition number: x = 1e6 + 0.1i gave rms residual ~1e2 at degree 2.
            var xo = Enumerable.Range(0, 101).Select(i => 1e6 + i * 0.1).ToArray();
            var yo = xo.Select(v => 1 + 2 * (v - 1e6) + 0.5 * (v - 1e6) * (v - 1e6)).ToArray();
            var p = Polynomial.Fit(xo, yo, 2);
            double rms = Math.Sqrt(xo.Select((v, i) => Math.Pow(p.Evaluate(v) - yo[i], 2)).Average());
            Check.True(rms < 1e-3, $"rms residual at x ≈ 1e6 was {rms:R}");

            var x = Enumerable.Range(0, 101).Select(i => i * 10.0).ToArray();
            var y = x.Select(v => Math.Sin(v / 150.0)).ToArray();
            var q = Polynomial.Fit(x, y, 12);
            double rms12 = Math.Sqrt(x.Select((v, i) => Math.Pow(q.Evaluate(v) - y[i], 2)).Average());
            Check.True(rms12 < 1e-6, $"degree-12 rms residual on [0, 1000] was {rms12:R}");

            // Tiny x: the singularity test was absolute, so x in [0, 1e-4] was "singular".
            var xt = Enumerable.Range(0, 11).Select(i => i * 1e-5).ToArray();
            var t = Polynomial.Fit(xt, xt.Select(v => v * v).ToArray(), 2);
            Rel(1.0, t.Coefficients[2], 1e-9, "x² coefficient for tiny x");
        }

        public void Polynomial_Fit_RejectsNonFiniteAndTooFewDistinctX()
        {
            Check.Throws<ArgumentException>(() => Polynomial.Fit(new[] { 0.0, double.NaN, 2 }, new[] { 0.0, 1, 2 }, 1));
            Check.Throws<ArgumentException>(() => Polynomial.Fit(new[] { 0.0, 1, 2 }, new[] { 0.0, double.PositiveInfinity, 2 }, 1));
            Check.Throws<InvalidOperationException>(() => Polynomial.Fit(new[] { 1.0, 1, 1, 2 }, new[] { 0.0, 1, 2, 3 }, 2));
        }

        public void Polynomial_EmptyCoefficientsAreTheZeroPolynomial()
        {
            Check.Equal(0, new Polynomial().Degree);
            Check.Equal(0.0, new Polynomial().Evaluate(3));
        }

        public void Polynomial_QuadraticAndCubicRootsAvoidCancellation()
        {
            double[] q = new Polynomial(1, -1e8, 1).RealRoots();
            Rel(1e-8, q[0], 1e-14, "small root of x² − 1e8x + 1");   // was 7.45e-9
            Rel(1e8, q[1], 1e-14, "large root");

            double[] c = new Polynomial(-6e-9, 1.1e-5, -0.006, 1).RealRoots();
            Check.Equal(3, c.Length, "roots 0.001, 0.002, 0.003 are distinct");
            Rel(0.001, c[0], 1e-12, "r1"); Rel(0.002, c[1], 1e-12, "r2"); Rel(0.003, c[2], 1e-12, "r3");

            double[] big = new Polynomial(-6e9, 1.1e7, -6000, 1).RealRoots();
            Check.Equal(3, big.Length, "roots 1e3, 2e3, 3e3 are distinct");
            Rel(1000, big[0], 1e-12, "R1"); Rel(2000, big[1], 1e-12, "R2"); Rel(3000, big[2], 1e-12, "R3");

            double[] small = new Polynomial(0, -1e-7, 0, 1).RealRoots();
            Check.Equal(3, small.Length, "x³ − 1e-7x has three roots");
            Rel(Math.Sqrt(1e-7), small[2], 1e-12, "√1e-7");
        }

        // ---- MonotoneCubic / LookupTable / Scattered ----

        public void MonotoneCubic_DoesNotOvershootALocalMaximum()
        {
            var m = new MonotoneCubic(new double[] { 0, 1, 2 }, new double[] { 0, 1, 0.5 });
            double max = Enumerable.Range(0, 2001).Select(i => m.Interpolate(i / 1000.0)).Max();
            Check.True(max <= 1, $"peak {max:R} exceeds the data maximum 1");
        }

        public void LookupTable_InverseLookupOnAPlateauIsFinite()
        {
            Check.Equal(0.0, new LookupTable(new double[] { 0, 1, 2 }, new double[] { 0, 0, 1 }).InverseLookup(0));
            Check.Equal(0.0, new LookupTable(new double[] { 0, 1 }, new double[] { 3, 3 }).InverseLookup(3));
            Check.Throws<ArgumentOutOfRangeException>(() => new LookupTable(new double[] { 0, 1 }, new double[] { 0, 1 }).InverseLookup(double.NaN));
        }

        public void ScatteredInterpolation_HandlesTinyAndHugeDistances()
        {
            Check.Equal(5.0, ScatteredInterpolation.InverseDistance(new[] { (0.0, 0.0, 5.0), (1.0, 0.0, 7.0) }, 1e-160, 0));
            Check.Equal(6.0, ScatteredInterpolation.InverseDistance(new[] { (1e200, 0.0, 5.0), (-1e200, 0.0, 7.0) }, 0, 0));
        }

        // ---- LinearAlgebra ----

        public void LinearAlgebra_SingularityIsRelativeToScale()
        {
            Rel(1e-40, LinearAlgebra.Determinant(new double[,] { { 1e-20, 0 }, { 0, 1e-20 } }), 1e-15, "det of tiny diagonal");
            double[] x = LinearAlgebra.Solve(new double[,] { { 1e-15, 0 }, { 0, 1e-15 } }, new[] { 1e-15, 1e-15 });
            Check.Close(1, x[0], 1e-15); Check.Close(1, x[1], 1e-15);
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Solve(new double[,] { { 1e10, 2e10 }, { 1, 2 } }, new[] { 1.0, 2.0 }));
            Check.Throws<ArgumentException>(() => LinearAlgebra.Solve(new double[,] { { double.NaN, 0 }, { 0, 1 } }, new[] { 1.0, 1.0 }));
        }

        // ---- ToleranceCheck / MovingAverage / ZScore ----

        public void ToleranceCheck_NaNSampleIsAViolationAndNaNLimitThrows()
        {
            var r = ToleranceCheck.Evaluate(new[] { 0.5, double.NaN }, 0, 1);
            Check.False(r.Pass); Check.Equal(1, r.ViolationCount); Check.Equal(1, r.FirstViolationIndex);
            Check.Throws<ArgumentOutOfRangeException>(() => ToleranceCheck.Evaluate(new[] { 5.0 }, double.NaN, 1));
        }

        public void MovingAverage_RecoversAfterASpikeOrInfinity()
        {
            var m = new MovingAverage(2);
            m.Add(1e17); m.Add(1); m.Add(1);
            Check.Equal(1.0, m.Add(1), "the 1e17 spike's rounding stayed in the running sum");
            var inf = new MovingAverage(3);
            inf.Add(double.PositiveInfinity); inf.Add(1); inf.Add(1); inf.Add(1);
            Check.Equal(1.0, inf.Add(1), "∞ − ∞ left NaN in the running sum forever");
            var both = new MovingAverage(2);
            both.Add(double.PositiveInfinity);
            Check.True(double.IsNaN(both.Add(double.NegativeInfinity)), "+∞ and −∞ together average to NaN");
        }

        public void ZScore_AllEqualDataScoresZero()
        {
            Check.True(ZScore.Standardize(new[] { 0.1, 0.1, 0.1 }).All(z => z == 0));
            Check.True(ZScore.Standardize(Enumerable.Repeat(0.7, 10).ToArray()).All(z => z == 0));
        }

        // ---- RootFinding ----

        public void RootFinding_ReversedBracketsLargeRootsAndNaN()
        {
            var rev = RootFinding.Bisection(x => x - 0.3, 2, 0);
            Check.True(rev.Converged); Check.Close(0.3, rev.Root, 1e-9);
            foreach (var r in new[]
            {
                Within(5000, () => RootFinding.Bisection(x => x - (1e10 + 0.5), 0, 2e10), "Bisection at 1e10"),
                Within(5000, () => RootFinding.Brent(x => x - (1e10 + 0.5), 0, 2e10), "Brent at 1e10"),
                Within(5000, () => RootFinding.Brent(x => x * x - 1e20 - 1e10, 0, 2e10), "Brent nonlinear at 1e10"),
            })
            {
                Check.True(r.Converged, r.Message);
                Rel(1e10 + 0.5, r.Root, 1e-15, "root near 1e10");
            }
            Check.False(RootFinding.Bisection(Math.Log, -1, 2).Converged, "log(−0.5) is NaN: not a root");
            Check.False(RootFinding.Brent(x => x < 0.5 && x > 0.4 ? double.NaN : x - 0.45, 0, 1).Converged);
            var n = RootFinding.Newton(x => x - 3e10, 1e10);
            Check.True(n.Converged, n.Message); Rel(3e10, n.Root, 1e-12, "Newton at 3e10");
        }

        // ---- Primes ----

        public void Primes_NextAndSieveRejectWhatTheyCannotReturn()
        {
            Check.Equal(Primes.LargestLongPrime, Within(5000, () => Primes.Next(Primes.LargestLongPrime - 1), "Next below the largest"));
            Check.Throws<OverflowException>(() => Within(5000, () => Primes.Next(Primes.LargestLongPrime), "Next(largest)"));
            Check.Throws<ArgumentOutOfRangeException>(() => Primes.Sieve(int.MaxValue));
            Check.Throws<ArgumentOutOfRangeException>(() => Primes.Sieve(-1));
        }

        // ---- Rounding / EngineeringNotation ----

        public void Rounding_SignificantDigitsAcrossTheRange()
        {
            Check.Equal(1.23e-310, Rounding.RoundToSignificantDigits(1.2345e-310, 3));
            Check.Equal(1.23e-30, Rounding.RoundToSignificantDigits(1.23456e-30, 3));
            Check.Equal(1.2345e-300, Rounding.RoundToSignificantDigits(1.2345e-300, 15));
            Check.Equal(1e300, Rounding.RoundToMultiple(1e300, 1e-10), "the quotient overflowed to ∞");
            Check.Throws<ArgumentOutOfRangeException>(() => Rounding.RoundToMultiple(1, double.PositiveInfinity));
        }

        public void EngineeringNotation_RoundsIntoTheNextPrefixAndParsesExactly()
        {
            Check.Equal("1 k", EngineeringNotation.Format(999.95, 3));
            Check.Equal("1 M", EngineeringNotation.Format(999999, 3));
            Check.Equal("-1 k", EngineeringNotation.Format(-999.96, 3));
            Check.Equal("120", EngineeringNotation.Format(120, 2));
            Check.Equal("100", EngineeringNotation.Format(100, 1));
            Check.Equal("120 k", EngineeringNotation.Format(123456, 2));
            Check.True(EngineeringNotation.TryParse("1.1 u", out double u)); Check.Equal(1.1e-6, u);
            Check.True(EngineeringNotation.TryParse("3 n", out double n)); Check.Equal(3e-9, n);
            Check.True(EngineeringNotation.TryParse("7 p", out double p)); Check.Equal(7e-12, p);
            Check.True(EngineeringNotation.TryParse(EngineeringNotation.Format(double.PositiveInfinity), out double inf));
            Check.Equal(double.PositiveInfinity, inf);
            Check.True(EngineeringNotation.TryParse(EngineeringNotation.Format(double.NaN), out double nan));
            Check.True(double.IsNaN(nan));
        }

        // ---- Integration ----

        public void Integration_SimpsonOddIntervalsAndAdaptiveTermination()
        {
            Check.Close(9, Integration.Simpson(new double[] { 0, 1, 4, 9 }, 1), 1e-12, "∫₀³ x² from 4 samples");
            Rel(Math.Exp(30) - 1, Within(20000, () => Integration.AdaptiveSimpson(Math.Exp, 0, 30), "∫ eˣ on [0, 30]"), 1e-12, "∫ eˣ");
            Rel(1e8 + 0.5, Within(20000, () => Integration.AdaptiveSimpson(x => 1e8 + x, 0, 1), "∫ 1e8 + x"), 1e-14, "∫ 1e8 + x");
            Check.Throws<InvalidOperationException>(() => Integration.AdaptiveSimpson(x => Math.Sin(1 / x), 1e-6, 1, 1e-14, maxEvaluations: 1000));
            Check.Throws<InvalidOperationException>(() => Integration.AdaptiveSimpson(x => 1 / x, -1, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => Integration.AdaptiveSimpson(x => x, 0, 1, double.NaN));
        }

        // ---- Means / Vector2 / Histogram ----

        public void Means_DoNotOverflowOrUnderflow()
        {
            Check.Equal(1e308, Means.Arithmetic(new[] { 1e308, 1e308 }));
            Rel(1e200, Means.RootMeanSquare(new[] { 1e200, 1e200 }), 1e-15, "RMS of 1e200");
            Rel(1e-200, Means.RootMeanSquare(new[] { 1e-200 }), 1e-15, "RMS of 1e-200");
            Check.Equal(1e-310, Means.Harmonic(new[] { 1e-310, 1e-310 }));
            Check.Throws<ArgumentException>(() => Means.Geometric(new[] { 1.0, double.NaN }));
            Check.Throws<ArgumentException>(() => Means.Harmonic(new[] { 1.0, double.NaN }));
        }

        public void Vector2_LengthAndNormalizeAtExtremeMagnitudes()
        {
            Rel(Math.Sqrt(2) * 1e200, new Vector2(1e200, 1e200).Length, 1e-15, "length of (1e200, 1e200)");
            foreach (double s in new[] { 1e200, 1e-200 })
            {
                var n = new Vector2(s, s).Normalized();
                Check.Close(Math.Sqrt(0.5), n.X, 1e-15, $"normalized x at {s}");
            }
            Check.Equal(Math.PI, new Vector2(-1, -0.0).Angle(), "angle stays in (−π, π]");
        }

        public void Histogram_BinLowerBoundLandsInItsBinAndNaNIsCountedSeparately()
        {
            var rng = new Random(1);
            for (int trial = 0; trial < 2000; trial++)
            {
                double min = Math.Round(rng.NextDouble() * 10 - 5, 2), max = min + Math.Round(rng.NextDouble() * 10 + 0.1, 2);
                int bins = rng.Next(1, 50);
                for (int k = 1; k < bins; k++)
                {
                    var h = new Histogram(min, max, bins);
                    h.Add(h.BinLowerBound(k));
                    Check.Equal(1L, h.Bins[k], $"min={min:R} max={max:R} n={bins}: BinLowerBound({k}) not in bin {k}");
                }
            }
            var nan = new Histogram(0, 1, 4);
            nan.Add(double.NaN);
            Check.Equal(1L, nan.NaNCount); Check.Equal(0L, nan.Underflow);
            Check.Throws<ArgumentException>(() => new Histogram(double.NegativeInfinity, 1, 4));
        }

        // ---- Quantiles ----

        public void StreamingQuantile_IsExactAtFiveValues()
        {
            var q = new StreamingQuantile(0.99);
            q.AddRange(new double[] { 1, 2, 3, 4, 5 });
            Check.Close(4.96, q.Estimate, 1e-12, "p = 0.99 of 1..5 (was the median, 3)");
        }

        public void Percentile_And_EmpiricalDistribution_RejectNaN()
        {
            Check.Throws<ArgumentException>(() => Percentile.Median(new[] { double.NaN, 1, 2 }));
            Check.Equal(0.0, Percentile.Median(new[] { -1e308, 1e308 }), "b − a overflowed to ∞");
            Check.Throws<ArgumentOutOfRangeException>(() => new EmpiricalDistribution(new double[] { 1, 2, 3 }).Quantile(double.NaN));
            Check.Throws<ArgumentException>(() => new EmpiricalDistribution(new[] { 1, double.NaN }));
        }

        // ---- Differentiation / UnitConvert ----

        public void Differentiation_ScalesTheStepAndRejectsRepeatedX()
        {
            Rel(2e12, Differentiation.Derivative(x => x * x, 1e12), 1e-6, "d/dx x² at 1e12");
            Rel(2e12, Differentiation.RichardsonDerivative(x => x * x, 1e12), 1e-6, "Richardson at 1e12");
            Check.Close(Math.Cos(1), Differentiation.Derivative(Math.Sin, 1, 1e-6), 1e-8, "explicit step");
            Check.Throws<ArgumentException>(() => Differentiation.Sample(new double[] { 0, 1, 1, 2 }, new double[] { 0, 1, 1, 4 }));
        }

        public void UnitConvert_IdentityAndCelsiusFahrenheitAreExact()
        {
            Check.Equal(0.1, UnitConvert.Convert(0.1, "C", "C"));
            Check.Equal(98.6, UnitConvert.Convert(98.6, "F", "F"));
            Check.Equal(98.6, UnitConvert.Convert(37, "C", "F"));
            Check.Equal(100.0, UnitConvert.Convert(212, "F", "C"));
            Check.Equal(1e308, UnitConvert.Convert(1e308, "km", "km"));
            Check.Equal(25.4, UnitConvert.Convert(1, "in", "mm"));
        }

        // ---- RandomUtils / Fraction / Angle ----

        public void RandomUtils_RangedDoubleIsHalfOpenAndWeightedChoiceSkipsZeroWeights()
        {
            double max = 1 + 4 * Math.Pow(2, -52);
            Check.True(new FixedRandom(1 - Math.Pow(2, -53)).NextDouble(1, max) < max, "min + u·span rounded up to max");
            double wide = new FixedRandom(0.75).NextDouble(-1e308, 1e308);
            Check.True(!double.IsInfinity(wide) && wide > 0, $"span overflowed: {wide:R}");
            Check.Equal("b", new FixedRandom(1.0).WeightedChoice(new[] { "a", "b", "c" }, new[] { 1.0, 1.0, 0.0 }));
        }

        public void Fraction_DefaultIsZero()
        {
            var d = default(Fraction);
            Check.True(d == Fraction.Zero); Check.Equal(0.0, d.ToDouble());
            Check.Equal(BigInteger.One, d.Denominator);
            Check.Equal("1", (d + Fraction.One).ToString());
        }

        public void Angle_NormalizeNeverReturnsNegativeZero()
        {
            Check.Equal(double.PositiveInfinity, 1 / Angle.NormalizeDegrees(-0.0));
            Check.Equal(double.PositiveInfinity, 1 / Angle.NormalizeDegrees(-360.0));
        }

        // ---- Robustness lens ----

        public void Accumulators_RejectNonFiniteSamples()
        {
            // One NaN or ∞ used to make the mean and variance NaN for good.
            var rs = new RunningStatistics();
            rs.Push(1);
            Check.Throws<ArgumentException>(() => rs.Push(double.NaN));
            Check.Throws<ArgumentException>(() => rs.Push(double.PositiveInfinity));
            Check.Equal(1L, rs.Count); Check.Equal(1.0, rs.Mean);
            var ema = new ExponentialMovingAverage(0.5);
            ema.Add(2);
            Check.Throws<ArgumentException>(() => ema.Add(double.NaN));
            Check.Equal(2.0, ema.Value);
        }

        public void MinMaxScaler_RejectsNaNAndScalesTheWholeDoubleRange()
        {
            Check.Throws<ArgumentException>(() => MinMaxScaler.ToUnit(new[] { double.NaN, 1, 2 }));
            Check.Throws<ArgumentException>(() => MinMaxScaler.ToUnit(new[] { 0, double.NaN, 2 }));
            var r = MinMaxScaler.ToUnit(new[] { -1e308, 0, 1e308 }); // was ∞/∞ = NaN
            Check.Equal(0.0, r[0]); Check.Equal(0.5, r[1]); Check.Equal(1.0, r[2]);
            var wide = MinMaxScaler.ToRange(new[] { 0.0, 1, 2 }, -1e308, 1e308);
            Check.Equal(-1e308, wide[0]); Check.Equal(0.0, wide[1]); Check.Equal(1e308, wide[2]);
        }

        public void WeightedAverage_ZeroWeightDropsTheValueAndInfiniteWeightThrows()
        {
            Check.Equal(5.0, WeightedAverage.Compute(new[] { 5, double.PositiveInfinity }, new[] { 1.0, 0 }));
            Check.Equal(5.0, WeightedAverage.Compute(new[] { 5, double.NaN }, new[] { 1.0, 0 }));
            Check.Throws<ArgumentException>(() => WeightedAverage.Compute(new[] { 1.0, 2 }, new[] { double.PositiveInfinity, 1 }));
        }

        public void Fraction_ToDoubleForHugeOperandsAndParseOfZeroDenominator()
        {
            var big = BigInteger.Pow(10, 308);
            Rel(1.0 / 3, new Fraction(big, 3 * big + 1).ToDouble(), 1e-16, "10^308/(3·10^308+1)"); // was 0
            Rel(-10.0 / 7, new Fraction(-10 * BigInteger.Pow(10, 400), 7 * BigInteger.Pow(10, 400) + 1).ToDouble(), 1e-16, "−10^401/(7·10^400+1)"); // was NaN
            Check.Equal(0.0, new Fraction(1, BigInteger.Pow(10, 400)).ToDouble());
            Check.Equal(double.PositiveInfinity, new Fraction(BigInteger.Pow(10, 400), 1).ToDouble());
            // Correct rounding past 2^53: (2^60 + 1)/3 = 384307168202282325.67, nearest double 384307168202282304.
            Check.Equal(384307168202282304.0, new Fraction((BigInteger.One << 60) + 1, 3).ToDouble());
            Check.Throws<FormatException>(() => Fraction.Parse("1/0")); // was DivideByZeroException

            // Exactly: ToDouble is no farther from n/d than either neighbouring double.
            var rng = new DeterministicRandom(4);
            for (int i = 0; i < 2000; i++)
            {
                var bytes = new byte[1 + rng.Next(60)]; rng.NextBytes(bytes);
                var num = new BigInteger(bytes) + 1;
                bytes = new byte[1 + rng.Next(60)]; rng.NextBytes(bytes);
                var den = BigInteger.Abs(new BigInteger(bytes)) + 1;
                var f = new Fraction(num, den);
                double x = f.ToDouble();
                Check.True(!double.IsInfinity(x) && x != 0, $"{f} out of range");
                var err = Distance(f, x);
                foreach (double y in new[] { NextAway(x, +1), NextAway(x, -1) })
                    if (Distance(f, y).CompareTo(err) < 0) { Check.True(false, $"{f}: {x:R} is not the nearest double ({y:R} is)"); return; }
            }
        }

        // |f − x| as an exact fraction.
        private static Fraction Distance(Fraction f, double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int exp = (int)((bits >> 52) & 0x7FF);
            BigInteger mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp != 0) mant += 1L << 52; else exp = 1;
            if (x < 0) mant = -mant;
            exp -= 1075;
            var exact = exp >= 0 ? new Fraction(mant << exp, 1) : new Fraction(mant, BigInteger.One << -exp);
            var d = f - exact;
            return d.CompareTo(Fraction.Zero) < 0 ? -d : d;
        }

        private static double NextAway(double x, int direction)
            => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + direction);

        public void Primes_AnyLongIsTestedAndFactoredQuickly()
        {
            // Trial division took 35 s for IsPrime(2^63 − 25) and 54 s to factor (2^31 − 1)^2.
            Check.True(Within(2000, () => Primes.IsPrime(Primes.LargestLongPrime), "IsPrime(2^63-25)"));
            var sq = Within(2000, () => Primes.Factorize(2147483647L * 2147483647L), "Factorize((2^31-1)^2)");
            Check.Equal(1, sq.Count); Check.Equal((2147483647L, 2), sq[0]);
            var semi = Within(2000, () => Primes.Factorize(1000000007L * 1000000009L), "Factorize(p·q)");
            Check.Equal("1000000007^1 1000000009^1", Show(semi));
            // Strong pseudoprimes to the first several bases must still be found composite.
            Check.False(Primes.IsPrime(3215031751L));            // 151·751·28351, bases 2, 3, 5, 7
            Check.False(Primes.IsPrime(3825123056546413051L));   // 149491·747451·34233211, bases 2..23
            Check.Equal("149491^1 747451^1 34233211^1", Show(Primes.Factorize(3825123056546413051L)));
            Check.Equal("71^1 839^1 1471^1 6857^1", Show(Primes.Factorize(600851475143L)));
            Check.Equal((2L, 62), Primes.Factorize(1L << 62).Single());
            var sieve = new System.Collections.Generic.HashSet<long>(Primes.Sieve(200000));
            for (long n = -2; n <= 200000; n++)
                if (Primes.IsPrime(n) != sieve.Contains(n)) { Check.True(false, $"IsPrime({n}) disagrees with the sieve"); break; }
            for (long n = 2; n <= 3000; n++)
            {
                long product = 1;
                foreach (var (p, e) in Primes.Factorize(n)) { Check.True(Primes.IsPrime(p)); for (int i = 0; i < e; i++) product *= p; }
                Check.Equal(n, product);
            }
        }

        private static string Show(System.Collections.Generic.IReadOnlyList<(long Prime, int Exponent)> f)
            => string.Join(" ", f.Select(t => t.Prime + "^" + t.Exponent));

        public void BaseConverter_AsciiCaseOnlyAndDistinctDigits()
        {
            Check.Throws<FormatException>(() => BaseConverter.FromBase("K", 36)); // Kelvin sign lowered to 'k' (= 20)
            Check.Equal(20L, BaseConverter.FromBase("K", 36));
            Check.Throws<ArgumentException>(() => BaseConverter.ToBase(1, "aab")); // 1 printed "a", parsed back 0
            Check.Throws<ArgumentException>(() => BaseConverter.FromBase("a", "aab"));
        }

        public void EngineeringNotation_OutOfPrefixRangeAndHugeDigitCounts()
        {
            Check.Equal("1E-30", EngineeringNotation.Format(1e-30));        // was "1E-06 y"
            Check.Equal("1.23E+30", EngineeringNotation.Format(1.234e30));  // was "1230000 Y"
            Check.Equal("1E+27", EngineeringNotation.Format(999.96e24));    // rounds out of yotta
            Check.Equal("1.5", EngineeringNotation.Format(1.5, 400));       // was "NaN"
            Rel(4.7e-6, EngineeringNotation.Parse("4.7 μ"), 1e-15, "Greek mu");
            Rel(4.7e-6, EngineeringNotation.Parse("4.7 µ"), 1e-15, "micro sign");
            Rel(1e-30, EngineeringNotation.Parse(EngineeringNotation.Format(1e-30)), 1e-15, "E round trip");
        }

        public void UnitConvert_TryParseNeedsAKnownUnit()
        {
            Check.False(UnitConvert.TryParse("3,5 kg", out _, out _));
            Check.False(UnitConvert.TryParse("5 bananas", out _, out _));
            Check.True(UnitConvert.TryParse("3.2kg", out double v, out string u));
            Check.Equal(3.2, v); Check.Equal("kg", u);
        }

        public void Uncertainty_SubnormalAndOverPreciseUncertainties()
        {
            Check.Equal("(0.0 ± 1.2)e-310", Uncertainty.Format(0, 1.2e-310)); // was NaN
            var r = Uncertainty.Round(0, 5e-324);
            Check.Equal(5e-324, r.Uncertainty); Check.Equal(0.0, r.Value);
            Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Round(1e300, 1e-300)); // was NaN
            Check.Equal("(1.0000000000000 ± 0.0000000000012)e300", Uncertainty.Format(1e300, 1.2e288));
        }

        public void RandomUtils_HugeWeightsAndInvalidDistributionParameters()
        {
            var items = new[] { "a", "b", "c" };
            var w = new[] { 1e308, 1e308, 1e308 }; // summed to ∞: always "c"
            Check.Equal("a", new FixedRandom(0.1).WeightedChoice(items, w));
            Check.Equal("b", new FixedRandom(0.5).WeightedChoice(items, w));
            Check.Throws<ArgumentOutOfRangeException>(() => new FixedRandom(0.5).NextExponential(double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new FixedRandom(0.5).NextGaussian(0, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new FixedRandom(0.5).NextGaussian(0, -1));
            Check.Equal("min", Check.Throws<ArgumentException>(() => new FixedRandom(0.5).NextDouble(1, 0)).ParamName);
        }

        public void Vector2_HashAgreesWithEqualsForNaNAndSignedZero()
        {
            // The mismatch was on .NET Framework, which hashes a double's bits; this net8.0 suite guards the contract.
            var nan1 = new Vector2(double.NaN, 1);
            var nan2 = new Vector2(BitConverter.Int64BitsToDouble(0x7FF8000000000001), 1);
            Check.True(nan1 == nan2); Check.Equal(nan1.GetHashCode(), nan2.GetHashCode());
            Check.True(new Vector2(0, 0) == new Vector2(-0.0, -0.0));
            Check.Equal(new Vector2(0, 0).GetHashCode(), new Vector2(-0.0, -0.0).GetHashCode());
        }

        public void DeterministicRandom_Net6MembersComeFromTheGenerator()
        {
            // Unoverridden, these went through Random's own (version-dependent) derivation from Sample().
            var a = new DeterministicRandom(7); var b = new DeterministicRandom(7);
            Check.Equal((long)(a.NextUInt64() % (ulong)long.MaxValue), b.NextInt64());
            Check.Equal((a.NextUInt64() >> 40) * (1.0f / 16777216.0f), b.NextSingle());
            Check.Equal(-5 + (long)(a.NextUInt64() % 10UL), b.NextInt64(-5, 5));
            var bytes = new byte[11]; a.NextBytes(bytes);
            Span<byte> span = stackalloc byte[11]; b.NextBytes(span);
            Check.True(span.SequenceEqual(bytes), "span NextBytes must match the array overload");
        }

        public void Containers_ExposeReadOnlyViewsAndRejectNonFinitePoints()
        {
            Check.False(new Polynomial(1, 2).Coefficients is double[], "Coefficients could be cast back and written");
            Check.False(new Histogram(0, 1, 4).Bins is long[], "Bins could be cast back and written");
            var xs = new[] { 0.0, 1, 2, 3 }; var ys = new[] { 0.0, 1, 0, 1 };
            Check.False(new AkimaSpline(xs, ys).Slopes is double[], "Slopes could be cast back and written");
            var bad = new[] { 0.0, double.NaN, 2, 3 };
            Check.Throws<ArgumentException>(() => new AkimaSpline(bad, ys));
            Check.Throws<ArgumentException>(() => new MonotoneCubic(bad, ys));
            Check.Throws<ArgumentException>(() => new LookupTable(bad, ys));
            Check.Throws<ArgumentException>(() => new LookupTable(xs, new[] { 0.0, double.PositiveInfinity, 0, 1 }));
            Check.Throws<ArgumentOutOfRangeException>(() => Interpolation.Clamp(1, double.NaN, 2));
            Check.Equal("min", Check.Throws<ArgumentException>(() => new Histogram(double.NegativeInfinity, 1, 4)).ParamName);
            Check.Equal("max", Check.Throws<ArgumentException>(() => new Histogram(0, double.NaN, 4)).ParamName);
            Check.Equal("threw", Within(2000, () => // the range hung the binning loop
            {
                try { new Histogram(0, 5e-324, 2); return "no throw"; } catch (ArgumentException) { return "threw"; }
            }, "subnormal range"));
            Check.Throws<ArgumentOutOfRangeException>(() => RootFinding.Brent(x => x, -1, 1, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => RootFinding.Newton(x => x, 1, tolerance: double.PositiveInfinity));
        }
    }
}
