using System;
using System.Globalization;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using Vector2 = ToolBelt.Numerics.Vector2;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 18 (numerics core, second pass).
    public sealed class NumericsCoreReview2Tests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        private static void Same(double expected, double actual, string what)
            => Check.True(expected.Equals(actual), $"{what}: expected {expected:R}, got {actual:R}");

        // ---- Checked arithmetic (these throw OverflowException when the library is built checked) ----

        public void DeterministicRandom_BytesAndWideRangesWorkUnderCheckedArithmetic()
        {
            var bytes = new byte[4];
            new DeterministicRandom(1).NextBytes(bytes);
            Check.True(bytes.SequenceEqual(new byte[] { 197, 16, 199, 15 }), string.Join(",", bytes));
#if NET6_0_OR_GREATER
            var span = new byte[4];
            new DeterministicRandom(1).NextBytes(span.AsSpan());
            Check.True(span.SequenceEqual(bytes), "span overload");
#endif
            var rng = new DeterministicRandom(7);
            for (int i = 0; i < 200; i++)
            {
                int v = rng.Next(int.MinValue, int.MaxValue);
                Check.True(v < int.MaxValue, "in range");
            }
        }

        public void Histogram_NegativeIndexIsOutOfRangeNotOverflow()
            => Check.Throws<ArgumentOutOfRangeException>(() => new Histogram(0, 1, 10).BinLowerBound(-1));

        // ---- Uncertainty ----

        public void Uncertainty_RoundingPastTheDoubleRangeIsRejected()
        {
            // The rounded value or uncertainty carried to ∞; Format then built the format string "F2147483341".
            Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Round(1.79e308, 5e307));
            Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Round(0, 1.79e308, UncertaintyRule.OneSignificantDigit));
            Check.Throws<ArgumentOutOfRangeException>(() => Uncertainty.Format(1.79e308, 5e307));
            Check.Equal("(1.7 ± 0.5)e8", Uncertainty.Format(1.7e8, 5e7));
            Check.Equal("(1.7 ± 0.5)e308", Uncertainty.Format(1.7e308, 5e307), "the same as at any other scale");
        }

        // ---- Overflow at finite extremes ----

        public void RunningStatistics_OppositeExtremesKeepAFiniteMean()
        {
            var s = new RunningStatistics();
            s.PushRange(new[] { 1e308, -1e308, 0, 5 });
            Check.Close(1.25, s.Mean, 1e-9);
            Check.True(double.IsPositiveInfinity(s.PopulationVariance), "the variance really exceeds the double range");
            s.Push(1);
            Check.Close(1.2, s.Mean, 1e-9);

            var t = new RunningStatistics();
            t.Push(-1e308); t.Push(1e308);
            Same(0, t.Mean, "mean of ±1e308");
        }

        public void MovingAverage_HugeFiniteSamplesLeaveTheWindow()
        {
            var ma = new MovingAverage(4);
            double[] expected = { 1e308, 1e308, 1e308 / 3 * 2, 5e307, 2.5e307, 0, 0, 0, 0, 0 };
            double[] input = { 1e308, 1e308, 0, 0, 0, 0, 0, 0, 0, 0 };
            for (int i = 0; i < input.Length; i++)
            {
                double avg = ma.Add(input[i]);
                if (expected[i] == 0) Same(0, avg, $"step {i}");
                else Rel(expected[i], avg, 1e-15, $"step {i}");
            }
            var two = new MovingAverage(2);
            two.Add(1e308);
            Rel(1e308, two.Add(1e308), 1e-15, "window 2");
            Rel(1e308, two.Add(1e308), 1e-15, "window 2, next lap");
            Rel(-1e308, new MovingAverage(3).Add(-1e308), 1e-15, "single");
        }

        public void WeightedAverage_HugeFiniteWeightsAndValues()
        {
            Check.Close(1, WeightedAverage.Compute(new[] { 1.0, 1.0 }, new[] { 1e308, 1e308 }), 1e-15);
            Rel(1e308, WeightedAverage.Compute(new[] { 1e308, 1e308 }, new[] { 2.0, 2.0 }), 1e-15, "huge values");
            Check.Close(2, WeightedAverage.Compute(new[] { 1.0, 3.0 }, new[] { 1e308, 1e308 }), 1e-15);
        }

        public void ZScore_ExtremeMagnitudesStandardize()
        {
            foreach (double m in new[] { 1e308, 1e200, 1e-200, 4.9e-324 })
            {
                double[] z = ZScore.Standardize(new[] { -m, m });
                Check.Close(-1, z[0], 1e-12, $"±{m}");
                Check.Close(1, z[1], 1e-12, $"±{m}");
            }
            Check.Close(2, ZScore.Standardize(1e308, -1e308, 1e308), 1e-15);
            Check.Throws<ArgumentOutOfRangeException>(() => ZScore.Standardize(1, 0, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => ZScore.Standardize(1, 0, double.NaN));
        }

        public void Interpolation_LerpFamilyHandlesOppositeExtremes()
        {
            Same(0, Interpolation.Lerp(-1e308, 1e308, 0.5), "Lerp");
            Same(1e308, Interpolation.Lerp(-1e308, 1e308, 1), "Lerp end");
            Same(0.5, Interpolation.InverseLerp(-1e308, 1e308, 0), "InverseLerp");
            Same(0.5, Interpolation.Remap(0, -1e308, 1e308, 0, 1), "Remap");
            var v = Vector2.Lerp(new Vector2(-1e308, 0), new Vector2(1e308, 4), 0.5);
            Same(0, v.X, "Vector2.Lerp x"); Same(2, v.Y, "Vector2.Lerp y");
        }

        public void LookupTable_ExtremeTablesInterpolate()
        {
            Same(0, new LookupTable(new[] { 0.0, 1 }, new[] { -1e308, 1e308 }).Interpolate(0.5), "y span");
            Rel(5e299, new LookupTable(new[] { 0.0, 1e10 }, new[] { 0.0, 1e300 }).Interpolate(5e9), 1e-15, "slope · dx");
            Same(0.5, new LookupTable(new[] { -1e308, 1e308 }, new[] { 0.0, 1 }).Interpolate(0), "x span");
            // NaN is not inside the table, so the Throw policy throws (InverseLookup already did).
            Check.Throws<ArgumentOutOfRangeException>(() => new LookupTable(new[] { 0.0, 1 }, new[] { 0.0, 1 }, Extrapolation.Throw).Interpolate(double.NaN));
        }

        public void Splines_ExtremeDataInterpolates()
        {
            Check.Close(0, new MonotoneCubic(new[] { 0.0, 1 }, new[] { -1e308, 1e308 }).Interpolate(0.5), 1e294, "monotone y");
            Check.Close(0, new AkimaSpline(new[] { 0.0, 1 }, new[] { -1e308, 1e308 }).Interpolate(0.5), 1e294, "akima y");
            // x spans more than the double range, so the segment width itself overflowed.
            Check.Close(1.5, new MonotoneCubic(new[] { -1e308, 1e308 }, new[] { 0.0, 2 }).Interpolate(5e307), 1e-12, "monotone x");
            var ak = new AkimaSpline(new[] { -1e308, 1e308 }, new[] { 0.0, 2 });
            Check.Close(1.5, ak.Interpolate(5e307), 1e-12, "akima x");
            Rel(1e-308, ak.Derivative(5e307), 1e-12, "akima derivative");
            Rel(1e-308, ak.Slopes[1], 1e-12, "akima slope");
            // Ordinary data is unaffected.
            var plain = new AkimaSpline(new[] { 0.0, 1, 2, 3 }, new[] { 0.0, 1, 4, 9 });
            Check.Close(2, plain.Slopes[1], 1e-12); Check.Close(2.25, new MonotoneCubic(new[] { 0.0, 1, 2, 3 }, new[] { 0.0, 1, 2, 3 }).Interpolate(2.25), 1e-12);
        }

        public void StreamingQuantile_OppositeExtremesStayFinite()
        {
            var q = new StreamingQuantile(0.5);
            q.Add(-1e308); q.Add(1e308);
            Same(0, q.Estimate, "two values");
            var rng = new Random(3);
            var big = new StreamingQuantile(0.5);
            for (int i = 0; i < 1000; i++) big.Add((rng.NextDouble() * 2 - 1) * 1.5e308);
            Check.True(Math.Abs(big.Estimate) < 0.2 * 1.5e308, $"median of a symmetric stream, got {big.Estimate:R}");
            Check.Throws<ArgumentNullException>(() => new StreamingQuantiles(null!));
        }

        public void ScatteredInterpolation_GridOverTheWholeRange()
        {
            var samples = new[] { (0.0, 0.0, 1.0), (1.0, 1.0, 1.0) };
            var g = ScatteredInterpolation.ToGrid(samples, -1e308, 1e308, 3, -1e308, 1e308, 3);
            foreach (double v in g) Check.Close(1, v, 1e-12);
            var one = ScatteredInterpolation.ToGrid(new[] { (1e308, 1e308, 7.0) }, 1e308, 1e308, 1, 1e308, 1e308, 1);
            Same(7, one[0, 0], "single cell at the sample");
        }

        public void Integration_HugeFiniteSamples()
        {
            Rel(5e307, Integration.Trapezoid(new[] { 1e308, 1e308 }, 0.5), 1e-15, "trapezoid");
            Rel(5e307, Integration.Trapezoid(new[] { 0.0, 0.5 }, new[] { 1e308, 1e308 }), 1e-15, "trapezoid x");
            Rel(5e307, Integration.Simpson(new[] { 1e308, 1e308, 1e308 }, 0.25), 1e-15, "simpson");
            Rel(5e307, Integration.CumulativeTrapezoid(new[] { 1e308, 1e308 }, 0.5)[1], 1e-15, "cumulative");
        }

        public void MinMaxScaler_SubnormalRange()
        {
            var r = MinMaxScaler.ToUnit(new[] { 0, double.Epsilon });
            Same(0, r[0], "min"); Same(1, r[1], "max");
        }

        public void Means_FiniteOverflowWithATrueInfinity()
        {
            Same(double.NegativeInfinity, Means.Arithmetic(new[] { 1e308, 1e308, double.NegativeInfinity }), "-∞ wins");
            Same(double.PositiveInfinity, Means.Arithmetic(new[] { -1e308, -1e308, double.PositiveInfinity }), "+∞ wins");
            Check.True(double.IsNaN(Means.Arithmetic(new[] { double.PositiveInfinity, double.NegativeInfinity })), "∞ − ∞");
        }

        // ---- Polynomial roots ----

        private static Polynomial FromRoots(double a, double b, double c)
            => new Polynomial(-a * b * c, a * b + a * c + b * c, -(a + b + c), 1);

        public void Polynomial_CubicRootsAtExtremeScales()
        {
            double[] r = FromRoots(1e110, 1, 2).RealRoots();
            Check.Equal(3, r.Length, string.Join(", ", r));
            Check.Close(1, r[0], 1e-12); Check.Close(2, r[1], 1e-12); Rel(1e110, r[2], 1e-12, "big root");

            r = new Polynomial(0, -1e240, 0, 1).RealRoots();
            Check.Equal(3, r.Length, string.Join(", ", r));
            Rel(-1e120, r[0], 1e-12, "-1e120"); Same(0, Math.Abs(r[1]), "0"); Rel(1e120, r[2], 1e-12, "1e120");

            r = new Polynomial(-1, 0, 1, 1e-300).RealRoots();
            Check.Equal(3, r.Length, string.Join(", ", r));
            Rel(-1e300, r[0], 1e-12, "-1e300"); Check.Close(-1, r[1], 1e-12); Check.Close(1, r[2], 1e-12);
        }

        public void Polynomial_CloseCubicRootsAreNotMerged()
        {
            double[] r = FromRoots(1, 1.00001, 2).RealRoots();
            Check.Equal(3, r.Length, string.Join(", ", r));
            Check.Close(1, r[0], 1e-9); Check.Close(1.00001, r[1], 1e-9); Check.Close(2, r[2], 1e-12);
            // Genuine repeated roots are still reported once.
            Check.Equal(2, FromRoots(0.1, 0.1, 2).RealRoots().Length);
            Check.Equal(2, FromRoots(2, 2, 1).RealRoots().Length);
            Check.Equal(1, FromRoots(3, 3, 3).RealRoots().Length);
        }

        public void Polynomial_QuadraticWithSubnormalCoefficients()
        {
            double[] r = new Polynomial(-1e-320, 0, 1e-320).RealRoots();
            Check.Equal(2, r.Length, string.Join(", ", r));
            Same(-1, r[0], "-1"); Same(1, r[1], "1");
        }

        // ---- Differentiation ----

        public void Differentiation_StepBelowTheSpacingOfDoubles()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.Derivative(x => x, 1e14, 1e-3));
            Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.RichardsonDerivative(x => x, 1e8, 1e-9));
            // Richardson divided by the nominal step, not the one x ± h actually took.
            Same(1, Differentiation.RichardsonDerivative(x => x, 1e6, 1e-7), "linear");
            Check.Close(Math.Cos(1e6), Differentiation.RichardsonDerivative(Math.Sin, 1e6, 1e-7), 1e-5);
        }

        // ---- EmpiricalDistribution ----

        public void EmpiricalDistribution_EqualNeighboursAndNaN()
        {
            var e = new EmpiricalDistribution(new[] { 0.1, 0.1 });
            Same(0.1, e.Quantile(0.3), "equal pair");
            var e3 = new EmpiricalDistribution(new[] { 0.7, 0.7, 0.7 });
            for (int i = 0; i <= 1000; i++) Same(0.7, e3.Quantile(i / 1000.0), $"p = {i / 1000.0}");
            Check.True(double.IsNaN(e.Cdf(double.NaN)), "Cdf(NaN)");
            Same(0, new EmpiricalDistribution(new[] { -1e308, 1e308 }).Quantile(0.5), "opposite extremes");
        }

        // ---- Rounding / EngineeringNotation ----

        public void Rounding_SignificantDigitsMatchTheDecimal()
        {
            Same(9.938315141319214E-146, Rounding.RoundToSignificantDigits(9.938315141319214E-146, 16), "already 16 digits");
            Same(2.927988114458566E+87, Rounding.RoundToSignificantDigits(2.9279881144585663E+87, 16), "16 of 17");
            Same(0.2, Rounding.RoundToSignificantDigits(0.15, 1), "the decimal 0.15 rounds half away");
            Same(-1.3, Rounding.RoundToSignificantDigits(-1.25, 2), "negative half");
            Same(5e-324, Rounding.RoundToSignificantDigits(4.9406564584124654E-324, 3), "subnormal");
            Same(double.PositiveInfinity, Rounding.RoundToSignificantDigits(double.MaxValue, 2), "beyond the range");
            Same(1000, Rounding.RoundToSignificantDigits(999.96, 3), "carry");
        }

        public void EngineeringNotation_FullPrecisionRoundTrips()
        {
            string s = EngineeringNotation.Format(999.9999999999999, 17);
            Check.True(!s.EndsWith(" k", StringComparison.Ordinal), s);
            Same(999.9999999999999, EngineeringNotation.Parse(s), s);
            var rng = new Random(11);
            for (int i = 0; i < 2000; i++)
            {
                double v = (rng.NextDouble() + 0.1) * Math.Pow(10, rng.Next(-24, 26)) * (i % 2 == 0 ? 1 : -1);
                string t = EngineeringNotation.Format(v, 17);
                Same(v, EngineeringNotation.Parse(t), t);
            }
            Same(1e-24, EngineeringNotation.Parse("1 y"), "1 y");
            Same(3e-9, EngineeringNotation.Parse("3 n"), "3 n");
            Same(1500, EngineeringNotation.Parse("1.5e0 k"), "mantissa with exponent");
            Check.False(EngineeringNotation.TryParse("1e306 k", out _), "overflows");
            Check.False(EngineeringNotation.TryParse("1e400", out _), "overflows (net8 parsed ∞)");
            Same(double.PositiveInfinity, EngineeringNotation.Parse("Infinity"), "Infinity");
            Check.Equal("1.5 k", EngineeringNotation.Format(1500));
            Check.Equal("1 k", EngineeringNotation.Format(999.96, 3));
            Check.Equal("-123 µ", EngineeringNotation.Format(-0.000123));
        }

        // ---- UnitConvert / LinearAlgebra / Vector2 ----

        public void UnitConvert_ExactRatiosAreExact()
        {
            Same(12, UnitConvert.Convert(1, "ft", "in"), "ft→in");
            Same(36, UnitConvert.Convert(3, "ft", "in"), "3 ft→in");
            Same(1000, UnitConvert.Convert(1, "mm", "um"), "mm→um");
            Same(1000, UnitConvert.Convert(1, "ms", "us"), "ms→us");
            Same(1000, UnitConvert.Convert(1, "g", "mg"), "g→mg");
            Rel(Math.PI, UnitConvert.Convert(180, "deg", "rad"), 1e-15, "deg→rad");
            Rel(1e308, UnitConvert.Convert(1e305, "km", "m"), 1e-15, "large values");
        }

        public void LinearAlgebra_RowScaledSystemsAndExtremeDeterminants()
        {
            var a = new double[,] { { 1e20, 0 }, { 0, 1 } };
            double[] x = LinearAlgebra.Solve(a, new[] { 1e20, 1 });
            Same(1, x[0], "x0"); Same(1, x[1], "x1");
            var inv = LinearAlgebra.Inverse(a);
            Same(1e-20, inv[0, 0], "inv00"); Same(1, inv[1, 1], "inv11");
            Rel(1, LinearAlgebra.Determinant(new double[,] { { 1e200, 0, 0, 0 }, { 0, 1e200, 0, 0 }, { 0, 0, 1e-200, 0 }, { 0, 0, 0, 1e-200 } }), 1e-14, "det");
            Same(double.PositiveInfinity, LinearAlgebra.Determinant(new double[,] { { 1e200, 0 }, { 0, 1e200 } }), "a determinant beyond the range");
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Solve(new double[,] { { 1e10, 2e10 }, { 1, 2 } }, new[] { 1.0, 2.0 }));
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Solve(new double[,] { { 1, 2 }, { 0, 0 } }, new[] { 1.0, 2.0 }));
        }

        public void RootFinding_NewtonTinyStepNeedsARoot()
        {
            var wrong = RootFinding.Newton(x => x - 1, 5, x => 1e300);
            Check.False(wrong.Converged, "a huge derivative made the step tiny, far from any root");
            var big = RootFinding.Newton(x => x * x - 1e20, 3e10, x => 2 * x);
            Check.True(big.Converged, "a root near 1e10 whose residual cannot reach the tolerance");
            Rel(1e10, big.Root, 1e-15, "root");
        }

        public void Vector2_InfiniteComponentsNormalize()
        {
            var v = new Vector2(double.PositiveInfinity, 3).Normalized();
            Same(1, v.X, "x"); Same(0, v.Y, "y");
            var d = new Vector2(double.NegativeInfinity, double.PositiveInfinity).Normalized();
            Check.Close(-Math.Sqrt(0.5), d.X, 1e-15); Check.Close(Math.Sqrt(0.5), d.Y, 1e-15);
        }
    }
}
