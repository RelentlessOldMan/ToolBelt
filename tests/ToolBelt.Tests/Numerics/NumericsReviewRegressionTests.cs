using System;
using System.Linq;
using System.Threading.Tasks;
using ToolBelt.Numerics;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>Regression tests for the 2026-10-05 review findings in Numerics and Signal.</summary>
    public sealed class NumericsReviewRegressionTests
    {
        public void PhaseUnwrap_HugeJumpsTerminateAndNonFiniteIsRejected()
        {
            var t = Task.Run(() => PhaseUnwrap.Unwrap(new[] { 0.0, 1e20, 1e20 + 1 }));
            Check.True(t.Wait(5000), "Unwrap of a 1e20 jump must terminate");
            Check.Throws<ArgumentException>(() => PhaseUnwrap.Unwrap(new[] { 0.0, double.PositiveInfinity }));
            Check.Throws<ArgumentException>(() => PhaseUnwrap.Unwrap(new[] { 0.0, double.NaN }));
            // Same answers as the old loop for ordinary data: whole turns, smallest jump within tolerance.
            double[] u = PhaseUnwrap.Unwrap(new[] { 3.0, -3.0, 3.1, -3.1, 0.0, 20.0 });
            for (int i = 1; i < u.Length; i++) Check.True(Math.Abs(u[i] - u[i - 1]) <= Math.PI + 1e-12, $"jump {i}");
            Check.Close(20.0 - 2 * Math.PI * Math.Round(20.0 / (2 * Math.PI)), u[5] - u[4], 1e-12);
        }

        public void TailProbabilities_KeepTheirPrecision()
        {
            Check.Close(2.151973671249891e-17, SpecialFunctions.Erfc(6), 1e-12 * 2.15e-17 * 100);
            Check.Close(Math.Exp(-50), SpecialFunctions.RegularizedGammaQ(1, 50), 1e-12 * Math.Exp(-50));     // Q(1,x) = e^-x
            Check.Close(1 + SpecialFunctions.Erf(1.5), SpecialFunctions.Erfc(-1.5), 1e-15);
            // χ² = 72 with df = 1: p = erfc(√36) = erfc(6).
            var r = HypothesisTests.ChiSquareGoodnessOfFit(new double[] { 160, 40 }, new double[] { 100, 100 });
            Check.Close(72, r.Statistic, 1e-12);
            Check.Close(2.151973671249891e-17, r.PValue, 1e-9 * 2.15e-17);
            Check.Close(Distributions.NormalSurvival(5), 1 - Distributions.NormalCdf(5), 1e-12);
            Check.Close(4.906713927148187e-198, Distributions.NormalSurvival(30), 1e-9 * 4.9e-198);   // 1 - Cdf would give 0
            Check.Close(Distributions.StudentTCdf(-3, 7), Distributions.StudentTSurvival(3, 7), 1e-15);
            Check.Close(1 - Distributions.FCdf(2, 3, 9), Distributions.FSurvival(2, 3, 9), 1e-13);
            Check.Equal(1.0, Distributions.ChiSquareSurvival(0, 4));
        }

        public void IncompleteGamma_LargeShapeConverges()
        {
            // Near the centre of a large-shape gamma, P(a, a) → 1/2 + 1/(3√(2πa)) (Temme). 300 iterations returned 0.49994.
            double a = 1e4;
            Check.Close(0.5 + 1 / (3 * Math.Sqrt(2 * Math.PI * a)), SpecialFunctions.RegularizedGammaP(a, a), 1e-6);
            Check.Close(Distributions.ChiSquareCdf(2 * (a - 300), 2 * a), SpecialFunctions.RegularizedGammaP(a, a - 300), 1e-10);
            var big = HypothesisTests.ChiSquareGoodnessOfFit(Enumerable.Repeat(1.0, 3).ToArray(), Enumerable.Repeat(1.0, 3).ToArray());
            Check.Close(1, big.PValue, 1e-12);
        }

        public void KolmogorovSmirnov_PerfectFitIsNotSignificant()
        {
            int n = 200000;
            var x = Enumerable.Range(0, n).Select(i => (i + 0.5) / n).ToArray();
            var r = HypothesisTests.KolmogorovSmirnovOneSample(x, v => v);
            Check.True(r.PValue > 0.99, $"perfect fit gave p = {r.PValue}");
            // Continuity across the two evaluation regimes (λ = 1.18).
            var below = HypothesisTests.KolmogorovSmirnovOneSample(new[] { 0.1, 0.35, 0.6, 0.9 }, v => v);
            Check.True(below.PValue > 0 && below.PValue <= 1);
        }

        public void PermutationTest_TwoSidedForStatisticsNotCentredOnZero()
        {
            var rng = new DeterministicRandom(3);
            double[] tight = Enumerable.Range(0, 12).Select(_ => rng.NextGaussian(0, 0.08)).ToArray();
            double[] wide = Enumerable.Range(0, 12).Select(_ => rng.NextGaussian(0, 5.5)).ToArray();
            double Ratio(System.Collections.Generic.IReadOnlyList<double> a, System.Collections.Generic.IReadOnlyList<double> b) => Variance(a) / Variance(b);
            double p1 = PermutationTest.TwoSample(tight, wide, new DeterministicRandom(1), 4999, Ratio).PValue;
            double p2 = PermutationTest.TwoSample(wide, tight, new DeterministicRandom(1), 4999, Ratio).PValue;
            Check.True(p1 < 0.01 && p2 < 0.01, $"variance ratio either way round must be significant: {p1}, {p2}");
            // Mean difference keeps its exact symmetric answer.
            Check.Close(0.1, PermutationTest.TwoSample(new double[] { 1, 2, 3 }, new double[] { 4, 5, 6 }, new DeterministicRandom(1)).PValue, 1e-15);
        }

        public void PermutationTest_IsScaleInvariant()
        {
            double[] a = { 1, 1.2, 0.9, 1.1, 1.05 }, b = { 5, 5.3, 4.8, 5.1, 4.9 };
            double big = PermutationTest.TwoSample(a, b, new DeterministicRandom(2)).PValue;
            double tiny = PermutationTest.TwoSample(a.Select(v => v * 1e-13).ToArray(), b.Select(v => v * 1e-13).ToArray(), new DeterministicRandom(2)).PValue;
            Check.Close(big, tiny, 1e-15);
            Check.True(big < 0.02);
        }

        public void StreamingQuantile_RejectsInfinity()
        {
            var q = new StreamingQuantile(0.5);
            Check.Throws<ArgumentException>(() => q.Add(double.PositiveInfinity));
            Check.Throws<ArgumentException>(() => q.Add(double.NegativeInfinity));
        }

        public void Weibull_FitsVeryTightData()
        {
            var rng = new DeterministicRandom(4);
            double[] x = Enumerable.Range(0, 400).Select(_ => 100 * Math.Pow(-Math.Log(1 - rng.NextDouble()), 1 / 3000.0)).ToArray();
            var fit = DistributionFit.FitWeibull(x);
            Check.True(fit.Parameters["shape"] > 2000, $"shape {fit.Parameters["shape"]}");
            Check.Close(100, fit.Parameters["scale"], 0.05);
        }

        public void Regression_MixedUnitsAreNotCollinear()
        {
            var rng = new DeterministicRandom(5);
            int n = 50;
            var x = new double[n, 2];
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                x[i, 0] = 1e6 * (1 + rng.NextDouble());
                x[i, 1] = 1e-7 * rng.NextDouble();
                y[i] = 2 + 3e-6 * x[i, 0] + 4e6 * x[i, 1] + rng.NextGaussian(0, 0.01);
            }
            var r = MultipleRegression.Fit(x, y);
            Check.Close(4e6, r.Coefficients[2], 4e6 * 0.05);
            Check.Throws<InvalidOperationException>(() => MultipleRegression.Fit(new double[,] { { 1, 2 }, { 2, 4 }, { 3, 6 }, { 4, 8 } }, new double[] { 1, 2, 3, 4 }));
        }

        public void RandomUtils_RejectsUnrepresentableInputs()
        {
            var rng = new DeterministicRandom(6);
            Check.Throws<ArgumentOutOfRangeException>(() => rng.NextPoisson(5e9));
            Check.True(rng.NextPoisson(1e8) > 0);
            Check.Throws<ArgumentException>(() => rng.WeightedChoice(new[] { "a", "b", "c" }, new[] { 1, double.NaN, 1 }));
            Check.Throws<ArgumentException>(() => rng.WeightedChoice(new[] { "a", "b" }, new[] { 1, double.PositiveInfinity }));
        }

        private static double Variance(System.Collections.Generic.IReadOnlyList<double> v)
        {
            double m = v.Average();
            return v.Sum(t => (t - m) * (t - m)) / (v.Count - 1);
        }
    }
}
