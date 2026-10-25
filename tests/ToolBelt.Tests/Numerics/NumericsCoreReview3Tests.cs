using System;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 36 (numerics core, third pass): each test failed before its fix.
    public sealed class NumericsCoreReview3Tests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        public void LinearAlgebra_DeterminantWithAPivotNearMaxValue()
        {
            Rel(2.25e8, LinearAlgebra.Determinant(new double[,] { { 1.5, 0, 0 }, { 0, 1.5e308, 0 }, { 0, 0, 1e-300 } }), 1e-14, "det");
            Rel(2.25e8, LinearAlgebra.Determinant(new double[,] { { 1e-300, 0, 0 }, { 0, 1.5e308, 0 }, { 0, 0, 1.5 } }), 1e-14, "det reversed");
        }

        public void LinearAlgebra_SolvesAColumnScaledMatrix()
        {
            var a = new double[,] { { 1e-20, 1e20 }, { 2e-20, 3e20 } };
            double[] x = LinearAlgebra.Solve(a, new[] { 1.0, 1.0 });
            Rel(2e20, x[0], 1e-12, "x0");
            Rel(-1e-20, x[1], 1e-12, "x1");
            LinearAlgebra.Inverse(a);
            // still singular: a rank-deficient matrix
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Solve(new double[,] { { 1, 2 }, { 2, 4 } }, new[] { 1.0, 1.0 }));
        }

        public void RootFinding_ASmallResidualFarFromARootIsNotConvergence()
        {
            var r = RootFinding.Newton(x => 1e-15 * (x - 5), 0);
            Check.True(r.Converged && Math.Abs(r.Root - 5) < 1e-9, "Newton linear: " + r.Root);
            Check.False(RootFinding.Newton(x => Math.Exp(-x), 0).Converged, "Newton e^-x has no root");
            r = RootFinding.Secant(x => 1e-15 * (x - 5), 0, 1);
            Check.True(r.Converged && Math.Abs(r.Root - 5) < 1e-9, "Secant linear: " + r.Root);
            Check.False(RootFinding.Secant(x => Math.Exp(-x), 0, 1).Converged, "Secant e^-x has no root");
            r = RootFinding.Secant(x => (x - 1) * (x - 1) * (x - 1), 1.0001, 1.0002);
            Check.True(!r.Converged || Math.Abs(r.Root - 1) < 1e-9, "Secant triple root: " + r.Root);
            r = RootFinding.Newton(x => x * x - 2, 1);
            Check.True(r.Converged && Math.Abs(r.Root - Math.Sqrt(2)) < 1e-12, "Newton sqrt2");
        }

        public void Percentile_MinusToPlusInfinityMatchesEmpiricalDistribution()
        {
            var data = new[] { double.NegativeInfinity, double.PositiveInfinity };
            Check.Equal(new EmpiricalDistribution(data).Quantile(0.25), Percentile.Compute(data, 25));
            Check.Equal(double.PositiveInfinity, Percentile.Compute(data, 75));
        }

        public void Splines_KeepSmallCoordinatesBesideHugeOnes()
        {
            Check.Equal(1e-200, new MonotoneCubic(new[] { 0.0, 1.0 }, new[] { 1e-200, 1e300 }).Interpolate(0));
            Check.Equal(1e-200, new AkimaSpline(new[] { 0.0, 1.0 }, new[] { 1e-200, 1e300 }).Interpolate(0));
            Check.Equal(1.0, new MonotoneCubic(new[] { 0.0, 1e-150, 1e160 }, new[] { 0.0, 1.0, 2.0 }).Interpolate(1e-150));
            double mid = new AkimaSpline(new[] { 0.0, 1e-150, 1.0, 1e160 }, new[] { 0.0, 1.0, 2.0, 3.0 }).Interpolate(5e-151);
            Check.True(mid > 0.3 && mid < 0.7, "Akima mid: " + mid);
            // a steep rise over a tiny span: the slope 1e309 overflowed to NaN
            Rel(5e8, new MonotoneCubic(new[] { 0.0, 1e-300 }, new[] { 0.0, 1e9 }).Interpolate(5e-301), 1e-12, "monotone tiny");
            Rel(5e8, new AkimaSpline(new[] { 0.0, 1e-300 }, new[] { 0.0, 1e9 }).Interpolate(5e-301), 1e-12, "akima tiny");
        }

        public void Interpolation_SmoothStepHandlesOppositeExtremes()
        {
            Check.Equal(0.5, Interpolation.SmoothStep(-1e308, 1e308, 0));
            Check.Equal(1.0, Interpolation.SmoothStep(-1e308, 1e308, 1e308));
        }

        public void LookupTable_TinyScaleKeepsPrecision()
        {
            Rel(5e-201, new LookupTable(new[] { 0.0, 2e-200 }, new[] { 0.0, 1e-200 }).Interpolate(1e-200), 1e-15, "1e-200");
            Rel(5e-161, new LookupTable(new[] { 0.0, 2e-160 }, new[] { 0.0, 1e-160 }).Interpolate(1e-160), 1e-15, "1e-160");
        }

        public void LinearAlgebra_EliminationNearMaxValue()
        {
            double[] x = LinearAlgebra.Solve(new double[,] { { 1e308, -1e308 }, { 1e308, 1e308 } }, new[] { 0.0, 5e307 });
            Rel(0.25, x[0], 1e-14, "x0");
            Rel(0.25, x[1], 1e-14, "x1");
            Rel(2e306, LinearAlgebra.Determinant(new double[,] { { 1e308, -1e308, 0 }, { 1e308, 1e308, 0 }, { 0, 0, 1e-310 } }), 1e-12, "det");
            double[,] inv = LinearAlgebra.Inverse(new double[,] { { 1e300, -1e300 }, { 1e300, 1e300 } });
            Rel(5e-301, inv[0, 0], 1e-14, "inv00");
            Rel(-5e-301, inv[1, 0], 1e-14, "inv10");
        }

        public void Polynomial_CubicWithATinyLeadingCoefficient()
        {
            double[] r = new Polynomial(1e300, 0, 0, 1e-10).RealRoots();
            Check.Equal(1, r.Length);
            Rel(-2.1544346900318837e103, r[0], 1e-12, "root");
            r = new Polynomial(-1, 0, 1, 1e-320).RealRoots();
            Check.True(r.Any(v => Math.Abs(v - 1) < 1e-12) && r.Any(v => Math.Abs(v + 1) < 1e-12), "roots ±1: " + string.Join(", ", r));
        }

        public void Polynomial_FitsValuesNearMaxValue()
            => Rel(1e308, Polynomial.Fit(new[] { 0.0, 1, 2 }, new[] { 1e308, 1e308, 1e308 }, 0).Coefficients[0], 1e-14, "fit");

        public void RunningStatistics_TinyAndHugeDeviations()
        {
            var s = new RunningStatistics();
            s.Push(1e-200); s.Push(2e-200);
            Rel(Math.Sqrt(0.5) * 1e-200, s.SampleStandardDeviation, 1e-12, "tiny sd");
            s = new RunningStatistics();
            s.Push(1e200); s.Push(-1e200);
            Rel(1e200, s.PopulationStandardDeviation, 1e-14, "huge sd");
            Check.Equal(double.PositiveInfinity, s.PopulationVariance);
            s = new RunningStatistics();
            s.Push(1e308); s.Push(-1e308);
            Rel(1e308, s.PopulationStandardDeviation, 1e-14, "max sd");
        }

        public void Differentiation_WideSpacingAndHugeValues()
        {
            foreach (double d in Differentiation.Sample(new[] { 0.0, 1e160, 2e160 }, new[] { 0.0, 1e160, 2e160 }))
                Rel(1, d, 1e-14, "uneven");
            foreach (double d in Differentiation.Sample(new[] { -1e308, 0, 1e308 }, 1.0))
                Rel(1e308, d, 1e-14, "uniform");
            foreach (double d in Differentiation.Sample(new[] { -0.9e308, 0.9e308 }, 2.0))
                Rel(0.9e308, d, 1e-14, "two");
        }

        public void RootFinding_BrentWithZeroToleranceStopsAtAdjacentDoubles()
        {
            var r = RootFinding.Brent(x => x <= 0 ? -1 : 1, -1, 1, 0, 5000); // a step: the bracket closes to 0 and 5e-324
            Check.True(r.Converged, "converged: " + r.Message);
            Check.True(Math.Abs(r.Root) <= double.Epsilon, "root " + r.Root);
        }

        public void LookupTable_FlatEndExtrapolatesToInfinity()
        {
            var t = new LookupTable(new[] { 0.0, 1, 2 }, new[] { 1.0, 1, 3 }, Extrapolation.Linear);
            Check.Equal(1.0, t.Interpolate(double.NegativeInfinity));
            Check.Equal(double.PositiveInfinity, t.Interpolate(double.PositiveInfinity));
        }

        public void SumsNearMaxValue()
        {
            Rel(1.62e308, UnitConvert.Convert(9e307, "C", "F"), 1e-14, "C to F");
            Rel(9e307, UnitConvert.Convert(1.62e308, "F", "C"), 1e-14, "F to C");
            var samples = new[] { (0.0, 0.0, 1.5e308), (1.0, 0.0, 1.5e308) };
            Rel(1.5e308, ScatteredInterpolation.InverseDistance(samples, 0.5, 0), 1e-14, "idw");
            Rel(4.5e298, Integration.Simpson(new[] { 1.5e308, 1.5e308, 1.5e308, 1.5e308 }, 1e-10), 1e-14, "simpson");
        }

        public void Validation()
        {
            Check.Throws<ArgumentException>(() => Integration.Trapezoid(new[] { 0.0, double.NaN, 2 }, new[] { 1.0, 1, 1 }));
            Check.Throws<ArgumentException>(() => ScatteredInterpolation.InverseDistance(new[] { (double.PositiveInfinity, 0.0, 1.0) }, 0, 0));
            Check.False(EngineeringNotation.TryParse("1e400", out double v));
            Check.Equal(0.0, v);
        }

        public void ParamNames()
        {
            Check.Equal("a", Check.Throws<ArgumentException>(() => LinearAlgebra.Determinant(new double[2, 3])).ParamName);
            Check.Equal("y", Check.Throws<ArgumentException>(() => new LookupTable(new[] { 0.0, 1 }, new[] { 0.0 })).ParamName);
            Check.Equal("weights", Check.Throws<ArgumentException>(() => new Random(1).WeightedChoice(new[] { 1, 2 }, new[] { 1.0 })).ParamName);
            Check.Equal("lowerLimit", Check.Throws<ArgumentException>(() => ToleranceCheck.Evaluate(new[] { 1.0 }, 2, 1)).ParamName);
            Check.Equal("columns", Check.Throws<ArgumentOutOfRangeException>(
                () => ScatteredInterpolation.ToGrid(new[] { (0.0, 0.0, 1.0) }, 0, 1, 0, 0, 1, 2)).ParamName);
            Check.Throws<ArgumentOutOfRangeException>(() => { double _ = new StreamingQuantiles(0.5)[1]; });
        }
    }
}
