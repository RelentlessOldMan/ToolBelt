using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 61 (numerics core, fourth pass): each test failed before its fix.
    public sealed class NumericsCoreReview4Tests
    {
        private const double Eps = double.Epsilon;

        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        // A list that reports a size without storing it, for size guards.
        private sealed class Ramp : IReadOnlyList<double>
        {
            public Ramp(int count) => Count = count;
            public int Count { get; }
            public double this[int index] => index;
            public IEnumerator<double> GetEnumerator() { for (int i = 0; i < Count; i++) yield return i; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public void AdaptiveSimpson_LargeConstantIntegrand()
        {
            int calls = 0;
            Rel(1e308, Integration.AdaptiveSimpson(x => { calls++; return 1e308; }, 0, 1), 1e-15, "1e308");
            Check.True(calls < 1000, $"calls {calls}");
            Rel(4e307, Integration.AdaptiveSimpson(x => 4e307, 0, 1), 1e-15, "4e307");
        }

        public void RunningStatistics_SumAfterTheRunningTotalOverflows()
        {
            var s = new RunningStatistics();
            foreach (double v in new[] { 1.7053549740963408e308, 2.979184497603767e307, -1.7053549740963408e308, -2.979184497603767e307, 61 })
                s.Push(v);
            Check.Equal(61.0, s.Sum);
            // Below the scaled copy's reach: 1e-310 · 2⁻⁶⁴ is 0.
            s = new RunningStatistics();
            foreach (double v in new[] { 1e308, 1e308, -1e308, -1e308, 1e-310 })
                s.Push(v);
            Check.Equal(1e-310, s.Sum);
        }

        private static double[,] Wilkinson(int n, double s)
        {
            var a = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < i; j++) a[i, j] = -s;
                a[i, i] = s;
                a[i, n - 1] = s;
            }
            return a;
        }

        public void LinearAlgebra_SolvesALargeEntryMatrixWithFullPivotGrowth()
        {
            foreach (double s in new[] { 1e288, 5e288, 1e290, 1e300 })
            {
                int n = 70;
                var a = Wilkinson(n, s);
                var b = new double[n];
                for (int i = 0; i < n; i++) b[i] = s;
                var x = LinearAlgebra.Solve(a, b);
                for (int i = 0; i < n - 1; i++) Check.True(Math.Abs(x[i]) < 1e-12, $"s {s:R}: x[{i}] = {x[i]:R}");
                Rel(1, x[n - 1], 1e-12, $"s {s:R}: x[last]");

                var inv = LinearAlgebra.Inverse(a);
                var e = LinearAlgebra.Multiply(inv, b);
                for (int i = 0; i < n - 1; i++) Check.True(Math.Abs(e[i]) < 1e-9, $"s {s:R}: (A^-1 b)[{i}] = {e[i]:R}");
                Rel(1, e[n - 1], 1e-9, $"s {s:R}: (A^-1 b)[last]");
            }
        }

        public void AkimaSpline_FlatSplineOnTinyXWithHugeY()
        {
            foreach (var x in new[] { new[] { 1e-305, 2e-305, 3e-305 }, new[] { 1e-305, 2e-305 } })
            {
                var spline = new AkimaSpline(x, x.Select(_ => 1e200).ToArray());
                Check.Equal(0.0, spline.Derivative(1.5e-305));
                foreach (double m in spline.Slopes) Check.Equal(0.0, m);
            }
        }

        public void Angle_DifferenceOfFiniteAnglesWhoseDifferenceOverflows()
        {
            Check.Equal(-56.0, Angle.DifferenceDegrees(-1.7e308, 1.7e308));
            double lerp = Angle.LerpDegrees(-1.7e308, 1.7e308, 0.5);
            Check.False(double.IsNaN(lerp), "lerp NaN");
        }

        public void Angle_DifferenceAndLerpOfLargeAngles()
        {
            Check.Equal(10.0, Angle.DifferenceDegrees(3.6e17, 10));
            Check.Equal(-10.0, Angle.DifferenceDegrees(10, 3.6e17));
            Check.Equal(5.0, Angle.LerpDegrees(3.6e17, 10, 0.5));
        }

        public void Angle_SmallNegativeAnglesKeepTheirValue()
        {
            Check.Equal(-1e-13, Angle.NormalizeSignedDegrees(-1e-13));
            Check.Equal(-1e-15, Angle.NormalizeSignedDegrees(-1e-15));
            Check.Equal(-1e-15, Angle.DifferenceDegrees(1e-15, 0));
            Check.Equal(1e-15, Angle.DifferenceDegrees(0, 1e-15));
            Check.Equal(-1e-300, Angle.DifferenceDegrees(0, -1e-300));
        }

        public void MonotoneCubic_LimiterWithAHugeSlopeRatio()
        {
            var m = new MonotoneCubic(new[] { 0.0, 1, 2 }, new[] { 0, 1e-300, 1e10 });
            double a = m.Interpolate(0.5), b = m.Interpolate(1.5);
            Check.True(a >= 0 && a <= 1e-300, $"0.5 -> {a:R}");
            Check.True(b >= 1e-300 && b <= 1e10, $"1.5 -> {b:R}");
        }

        public void Splines_SecantThatOverflows()
        {
            double m = new MonotoneCubic(new[] { 0, 1e-300, 1 }, new[] { 0, 1e10, 2e10 }).Interpolate(0.5);
            Check.True(m >= 1e10 && m <= 2e10, $"monotone {m:R}");
            foreach (bool modified in new[] { false, true })
            {
                double a = new AkimaSpline(new[] { 0, 1e-300, 1, 2 }, new[] { 0, 1e10, 2e10, 3e10 }, modified).Interpolate(0.5);
                Check.True(!double.IsNaN(a) && !double.IsInfinity(a), $"akima modified={modified}: {a:R}");
            }
        }

        public void LookupTable_ReturnsTheKnotValueAtAKnot()
        {
            Check.Equal(1e-17, new LookupTable(new[] { 0.0, 1 }, new[] { 0.1, 1e-17 }).Interpolate(1));
            Check.Equal(1e-17, new LookupTable(new[] { 0.0, 1, 2 }, new[] { 0.1, 1e-17, 5 }).Interpolate(1));
            Check.Equal(1e-17, new LookupTable(new[] { 0, 1e-17 }, new[] { 0.1, 0.3 }).InverseLookup(0.3));
            var random = new Random(61);
            for (int trial = 0; trial < 2000; trial++)
            {
                double a = random.NextDouble() * 10 - 5, b = random.NextDouble() * 1e-3;
                Check.Equal(b, new LookupTable(new[] { 0.0, 1, 2 }, new[] { a, b, a }).Interpolate(1), $"trial {trial}");
            }
        }

        public void Means_GeometricOfEqualValuesIsExactAndOrdered()
        {
            Check.Equal(1e300, Means.Geometric(new[] { 1e300, 1e300, 1e300 }));
            double v = 8.820485625798109E-299;
            Check.Equal(v, Means.Geometric(new[] { v, v, v }));
            var random = new Random(61);
            for (int trial = 0; trial < 2000; trial++)
            {
                double baseValue = Math.Pow(10, random.NextDouble() * 600 - 300);
                var values = new[] { baseValue, baseValue * (1 + 1e-14 * random.NextDouble()), baseValue * (1 + 1e-14 * random.NextDouble()) };
                Check.Equal(baseValue, Means.Geometric(new[] { baseValue, baseValue, baseValue }), $"equal {trial}");
                double hm = Means.Harmonic(values), gm = Means.Geometric(values), am = Means.Arithmetic(values);
                // Reference from ratios near 1, whose logs are small and accurate.
                double reference = baseValue * Math.Exp(values.Sum(x => Math.Log(x / baseValue)) / 3);
                Rel(reference, gm, 9e-16, $"trial {trial}");
                Check.True(gm >= hm * (1 - 6.7e-16) && gm <= am * (1 + 6.7e-16), $"trial {trial}: HM {hm:R} GM {gm:R} AM {am:R}");
            }
        }

        public void Polynomial_CubicKeepsADoubleRootNextToTheSimpleRoot()
        {
            foreach (var (r, s) in new[] { (-8.078125, -8.09375), (10.671875, 10.75), (15.46875, 15.5) })
            {
                // (x - r)^2 (x - s), exact in doubles for these dyadic roots.
                var p = new Polynomial(-r * r * s, r * r + 2 * r * s, -(2 * r + s), 1);
                var roots = p.RealRoots();
                string list = string.Join(", ", roots.Select(x => x.ToString("R")));
                Check.True(roots.Any(x => Math.Abs(x - r) < 1e-6), $"double root {r} missing: [{list}]");
                Check.True(roots.Any(x => Math.Abs(x - s) < 1e-6), $"simple root {s} missing: [{list}]");
            }
        }

        public void Polynomial_FitRejectsADesignMatrixPastArrayMaxLength()
        {
            var x = new Ramp(50000);
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Polynomial.Fit(x, x, 49999));
            Check.Equal("degree", ex.ParamName);
        }

        public void Histogram_RejectsBinsBelowTheSpacingOfDoubles()
        {
            var ex = Check.Throws<ArgumentException>(() => new Histogram(1e16, 1e16 + 16, 64));
            Check.Equal("binCount", ex.ParamName);
            var ok = new Histogram(1e16, 1e16 + 1024, 64); // 16-wide bins, every bound distinct
            Check.Equal(64, ok.BinCount);
        }

        public void WeightedChoice_SubnormalWeightsAreFair()
        {
            var random = new Random(61);
            var items = new[] { 0, 1, 2 };
            var counts = new int[3];
            for (int i = 0; i < 60000; i++) counts[random.WeightedChoice(items, new[] { Eps, Eps, Eps })]++;
            foreach (int c in counts) Check.True(Math.Abs(c - 20000) < 1000, $"counts {string.Join(", ", counts)}");

            counts = new int[3];
            for (int i = 0; i < 60000; i++) counts[random.WeightedChoice(items, new[] { 3 * Eps, 2 * Eps, 0 })]++;
            Check.True(Math.Abs(counts[0] - 36000) < 1000 && counts[2] == 0, $"counts {string.Join(", ", counts)}");
        }

        public void ScatteredInterpolation_SamplesFartherThanMaxValue()
        {
            Check.Equal(5.0, ScatteredInterpolation.InverseDistance(new[] { (1e308, 0.0, 5.0) }, -1e308, 0));
            Check.Equal(5.0, ScatteredInterpolation.InverseDistance(new[] { (1e308, 0.0, 5.0), (1e308, 1e308, 5.0) }, -1e308, 0));
            double v = ScatteredInterpolation.InverseDistance(new[] { (1e308, 0.0, 5.0), (1e308, 1e308, 7.0) }, -1e308, 0);
            Check.True(v > 5 && v < 7, $"two values: {v:R}");
        }

        public void ScatteredInterpolation_StaysWithinTheSampleRange()
        {
            var random = new Random(61);
            for (int trial = 0; trial < 2000; trial++)
            {
                var samples = Enumerable.Range(0, 3).Select(_ => (random.NextDouble() * 10, random.NextDouble() * 10, 0.1)).ToArray();
                Check.Equal(0.1, ScatteredInterpolation.InverseDistance(samples, random.NextDouble() * 10, random.NextDouble() * 10), $"trial {trial}");
            }
        }

        public void ScatteredInterpolation_GridEndsAreExact()
        {
            double min = 2.774923684901988, max = 6.6201694317255955;
            var samples = new[] { (min, 0.0, 1.0), (max, 0.0, 42.0) };
            var grid = ScatteredInterpolation.ToGrid(samples, min, max, 36, 0, 0, 1, power: 0.1);
            Check.Equal(1.0, grid[0, 0]);
            Check.Equal(42.0, grid[0, 35]);
        }

        public void ScatteredInterpolation_RejectsInfiniteValues()
        {
            var ex = Check.Throws<ArgumentException>(() => ScatteredInterpolation.InverseDistance(new[] { (0.0, 0.0, double.PositiveInfinity), (1.0, 0.0, 1.0) }, 0.5, 0));
            Check.Equal("samples", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => ScatteredInterpolation.InverseDistance(new[] { (0.0, 0.0, double.PositiveInfinity), (1.0, 0.0, double.NegativeInfinity) }, 0.5, 0));
            Check.Equal("samples", ex.ParamName);
        }

        public void ScatteredInterpolation_RejectsAGridPastArrayMaxLength()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => ScatteredInterpolation.ToGrid(new[] { (0.0, 0.0, 1.0) }, 0, 1, 65536, 0, 1, 65536));
            Check.Equal("rows", ex.ParamName);
        }

        public void Simpson_SubnormalSpacing()
        {
            Check.Equal(2 * Eps, Integration.Simpson(new[] { 1.0, 1, 1 }, Eps));
            Check.Equal(3 * Eps, Integration.Simpson(new[] { 1.0, 1, 1, 1 }, Eps));
            Check.Equal(3 * Eps, Integration.AdaptiveSimpson(x => 1, 0, 3 * Eps));
            Check.Equal(6 * Eps, Integration.AdaptiveSimpson(x => 1, 0, 6 * Eps));
        }

        public void Percentile_IntegerRankIsTheOrderStatistic()
        {
            var data = Enumerable.Range(0, 101).Select(i => i * 1.1).ToArray();
            Check.Equal(data[29], Percentile.Compute(data, 29));
            for (int p = 0; p <= 100; p++) Check.Equal(data[p], Percentile.Compute(data, p), $"p {p}");
        }

        public void Differentiation_RejectsInfinitePositions()
        {
            var ex = Check.Throws<ArgumentException>(() => Differentiation.Sample(new[] { 0, 1, double.PositiveInfinity }, new[] { 0.0, 1, 2 }));
            Check.Equal("x", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => Differentiation.Sample(new[] { double.NegativeInfinity, 0, 1 }, new[] { 0.0, 1, 2 }));
            Check.Equal("x", ex.ParamName);
        }

        public void WeightedAverage_EqualValuesAreExact()
        {
            Check.Equal(0.1, WeightedAverage.Compute(new[] { 0.1, 0.1, 0.1 }, new[] { 1.0, 1, 1 }));
            var random = new Random(61);
            for (int trial = 0; trial < 2000; trial++)
            {
                double v = random.NextDouble() * 100 - 50;
                var w = Enumerable.Range(0, 5).Select(_ => random.NextDouble() + 0.01).ToArray();
                Check.Equal(v, WeightedAverage.Compute(Enumerable.Repeat(v, 5).ToArray(), w), $"trial {trial}");
            }
        }

        public void ExponentialMovingAverage_ConstantInputStaysPut()
        {
            var ema = new ExponentialMovingAverage(0.36643333237917786);
            for (int i = 0; i < 50; i++) ema.Add(6.761694413964494);
            Check.Equal(6.761694413964494, ema.Value);
            var random = new Random(61);
            for (int trial = 0; trial < 2000; trial++)
            {
                double alpha = random.NextDouble() * 0.99 + 0.01, c = random.NextDouble() * 20 - 10;
                var e = new ExponentialMovingAverage(alpha);
                for (int i = 0; i < 50; i++) e.Add(c);
                Check.Equal(c, e.Value, $"trial {trial}");
            }
        }
    }
}
