using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 3 (statistics). References are 40-digit mpmath unless noted.
    public sealed class StatisticsReviewTests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        // ---- incomplete gamma / beta at enormous arguments ----

        public void ChiSquare_StaysAccurateAtTenBillionDegreesOfFreedom()
        {
            // The series / continued fraction ran out of iterations: 0.42 here before.
            Rel(0.50000188063194516128, Distributions.ChiSquareCdf(1e10, 1e10), 1e-13, "ChiSquareCdf(1e10; 1e10)");
            Rel(0.50000188063194516128, SpecialFunctions.RegularizedGammaP(5e9, 5e9), 1e-13, "P(5e9, 5e9)");
            Rel(0.49998670192398588013, SpecialFunctions.RegularizedGammaQ(1e8, 1e8), 1e-13, "Q(1e8, 1e8)");
        }

        public void SpecialFunctions_RegularizedBetaI_IsAccurateAtLargeParameters()
        {
            // References by 45-digit quadrature of the density; dyadic x keeps the inputs exact, so the bound measures the
            // method rather than the conditioning of x. Before: 0.4999999999919 at the first (SpecialFunctions: 2.8e-9).
            Rel(0.5, SpecialFunctions.RegularizedBetaI(0.5, 1e6, 1e6), 1e-14, "I_0.5(1e6, 1e6)");
            Rel(0.33302199340196517101, SpecialFunctions.RegularizedBetaI(0.4999847412109375, 1e8, 1e8), 1e-13, "I(1e8, 1e8) at −0.43σ");
            Rel(0.04214358834073678619, SpecialFunctions.RegularizedBetaI(0.49993896484375, 1e8, 1e8), 1e-13, "I(1e8, 1e8) at −1.7σ");
            Rel(0.95785641165926321381, SpecialFunctions.RegularizedBetaI(0.50006103515625, 1e8, 1e8), 1e-13, "I(1e8, 1e8) at +1.7σ");
            Rel(0.12972551375281150341, SpecialFunctions.RegularizedBetaI(0.249755859375, 1e6, 3e6), 1e-13, "I(1e6, 3e6) at −1.1σ");
            // The same point through F (y = x/(1 + x) when d1 = d2): rounding x and re-forming y costs ~κ·ε ≈ 1e-12 here.
            Rel(0.04214358834073678619, Distributions.FSurvival(0.50006103515625 / 0.49993896484375, 2e8, 2e8), 1e-11, "FSurvival(2e8, 2e8) at +1.7σ");
        }

        public void SpecialFunctions_LnGamma_IsAccurateForTinyAndInfiniteArguments()
        {
            Rel(18.420680738180208905, SpecialFunctions.LnGamma(1e-8), 1e-14, "LnGamma(1e-8)");
            Rel(713.80137882815416205, SpecialFunctions.LnGamma(1e-310), 1e-14, "LnGamma(1e-310)");
            Rel(1.0957979948180755217, SpecialFunctions.LnGamma(0.3), 1e-14, "LnGamma(0.3)");
            Check.Equal(double.PositiveInfinity, SpecialFunctions.LnGamma(double.PositiveInfinity));
        }

        public void SpecialFunctions_Gamma_OfNaN_IsNaN()
            => Check.True(double.IsNaN(SpecialFunctions.Gamma(double.NaN)), "Gamma(NaN) recursed until the stack overflowed");

        public void SpecialFunctions_Erf_OfTinyArgument_IsLinear()
        {
            Rel(1.1283791670955126e-200, SpecialFunctions.Erf(1e-200), 1e-15, "Erf(1e-200)");
            Rel(-1.1283791670955126e-200, SpecialFunctions.Erf(-1e-200), 1e-15, "Erf(-1e-200)");
            Check.Equal(1.0, SpecialFunctions.Erfc(1e-200));
        }

        // ---- Anova / GaugeRR private F tails ----

        public void Anova_FUpperTail_IsAccurateAtLargeDegreesOfFreedom()
        {
            // P(F > 1) with equal df is exactly ½ (symmetry); the old private copy (300 iterations, cancelling LnGamma)
            // was off by 5e-11 here.
            Rel(0.5, Anova.FUpperTailProbability(1, 1e5, 1e5), 1e-14, "P(F(1e5, 1e5) > 1)");
            Rel(0.5, Anova.FUpperTailProbability(1, 3e8, 3e8), 1e-14, "P(F(3e8, 3e8) > 1)");
            Rel(Distributions.FSurvival(2.5, 3, 1e9), Anova.FUpperTailProbability(2.5, 3, 1e9), 1e-14, "matches Distributions");
            Rel(Distributions.FSurvival(40, 7, 12), Anova.FUpperTailProbability(40, 7, 12), 1e-14, "small tail");
        }

        public void Anova_RejectsNaNDegreesOfFreedom()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Anova.FUpperTailProbability(1, double.NaN, 5));
            Check.Throws<ArgumentOutOfRangeException>(() => Anova.FUpperTailProbability(1, 5, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => Anova.FUpperTailProbability(1, 5, double.PositiveInfinity));
            Check.True(double.IsNaN(Anova.FUpperTailProbability(double.NaN, 5, 5)), "NaN statistic");
            Check.Equal(0.0, Anova.FUpperTailProbability(double.PositiveInfinity, 5, 5));
        }

        public void Anova_RoundOffIsNotVariation()
        {
            // Group means 0.1 and 0.10000000000000002 (round-off): both sums of squares are ~1e-34 crumbs, and their
            // ratio was reported as a real F.
            var same = Anova.OneWay(new[] { 0.1, 0.1, 0.1 }, new[] { 0.1, 0.1, 0.1, 0.1 });
            Check.Equal(0.0, same.FStatistic);
            Check.Equal(1.0, same.PValue);
            var apart = Anova.OneWay(new[] { 0.1, 0.1, 0.1 }, new[] { 0.7, 0.7, 0.7, 0.7 });
            Check.Equal(double.PositiveInfinity, apart.FStatistic);
            Check.Equal(0.0, apart.PValue);
        }

        public void GaugeRR_AllIdenticalReadings_Throws()
        {
            var m = new double[3, 2, 2];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) for (int k = 0; k < 2; k++) m[i, j, k] = 0.1;
            // Was: Unacceptable, NaN percentages and int.MaxValue distinct categories.
            Check.Throws<ArgumentException>(() => GaugeRR.Analyze(m));
        }

        public void GaugeRR_RoundOffIsNotGaugeVariation()
        {
            // Parts differ; every reading of a part is identical (sums of 0.1 + 0.2 leave round-off in the cell means).
            var m = new double[3, 2, 3];
            double[] part = { 0.1 + 0.2, 0.7, 1.1 };
            for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) for (int k = 0; k < 3; k++) m[i, j, k] = part[i];
            var r = GaugeRR.Analyze(m);
            Check.Equal(0.0, r.TotalGaugeRR.Variance);
            Check.Equal(GaugeAcceptability.Acceptable, r.Acceptability);
        }

        // ---- ChangePoint ----

        public void ChangePoint_DetectShift_FindsTheStepNotAnEndOutlier()
        {
            // The largest |Δmean| is at k = 9 (cutting off the single 20); the step is at 5.
            var (index, magnitude) = ChangePoint.DetectShift(new double[] { 0, 0, 0, 0, 0, 10, 10, 10, 10, 20 });
            Check.Equal(5, index);
            Check.Close(12, magnitude, 1e-12);
        }

        public void ChangePoint_DetectShift_MinimisesTotalSquaredError()
        {
            var rng = new Random(31);
            for (int trial = 0; trial < 200; trial++)
            {
                int n = rng.Next(2, 40), step = rng.Next(1, n);
                var x = Enumerable.Range(0, n).Select(i => rng.NextDouble() + (i >= step ? 3 * rng.NextDouble() : 0)).ToArray();
                int bestK = 1;
                double bestSse = double.PositiveInfinity;
                for (int k = 1; k < n; k++)
                {
                    double ml = x.Take(k).Average(), mr = x.Skip(k).Average();
                    double sse = x.Take(k).Sum(v => (v - ml) * (v - ml)) + x.Skip(k).Sum(v => (v - mr) * (v - mr));
                    if (sse < bestSse - 1e-12) { bestSse = sse; bestK = k; }
                }
                Check.Equal(bestK, ChangePoint.DetectShift(x).Index, $"trial {trial}");
            }
        }

        public void ChangePoint_DetectShift_RejectsNonFiniteValues()
        {
            // All-NaN returned (1, −1); a NaN anywhere poisoned every comparison.
            Check.Throws<ArgumentException>(() => ChangePoint.DetectShift(new[] { double.NaN, double.NaN, double.NaN }));
            Check.Throws<ArgumentException>(() => ChangePoint.DetectShift(new[] { 1, 2, double.NaN, 4 }));
            Check.Throws<ArgumentException>(() => ChangePoint.DetectShift(new[] { 1, double.PositiveInfinity }));
        }

        // ---- Trend ----

        public void Trend_MannKendall_RejectsNaN()
        {
            // Math.Sign(NaN) threw ArithmeticException.
            Check.Throws<ArgumentException>(() => Trend.MannKendall(new[] { 1, double.NaN, 3 }));
            Check.Throws<ArgumentException>(() => Trend.MannKendall(new[] { 1, 2, double.PositiveInfinity }));
        }

        public void Trend_MannKendall_RejectsSeriesTooLongForPairwiseSlopes()
        {
            // n(n − 1)/2 overflowed int above 65,536 points (negative List capacity).
            var tooLong = new double[Trend.MaxPoints + 1];
            Check.Throws<ArgumentException>(() => Trend.MannKendall(tooLong));
        }

        public void Trend_MannKendall_TauIsTauA()
        {
            var r = Trend.MannKendall(new double[] { 1, 2, 2, 3 });   // 5 concordant, 1 tie of 6 pairs
            Check.Equal(5, r.S);
            Check.Close(5.0 / 6, r.Tau, 1e-15);
        }

        public void Trend_SensSlope_MedianDoesNotOverflow()
        {
            var r = Trend.MannKendall(new[] { 0, 1.5e308 });
            Check.Equal(1.5e308, r.SensSlope);
            // Every slope is 0.9e308, but y₃ − y₀ and y₃ − y₁ overflow: the slopes were [0.9e308 ×4, ∞, ∞], and the even
            // median's sum 0.9e308 + 0.9e308 overflowed to ∞.
            var e = Trend.MannKendall(new[] { -1.35e308, -0.45e308, 0.45e308, 1.35e308 });
            Rel(0.9e308, e.SensSlope, 1e-15, "Sen's slope");
            Check.Equal(6, e.S);
        }

        // ---- HypothesisTests ----

        public void HypothesisTests_NaNSamples_AreRejected()
        {
            // KS two-sample looped forever (NaN == NaN is false, so the inner advances never moved past it).
            Check.Throws<ArgumentException>(() => HypothesisTests.KolmogorovSmirnovTwoSample(new[] { 1, double.NaN, 3 }, new[] { 1.0, 2 }));
            Check.Throws<ArgumentException>(() => HypothesisTests.KolmogorovSmirnovOneSample(new[] { double.NaN, 1 }, x => 0.5));
            Check.Throws<ArgumentException>(() => HypothesisTests.MannWhitneyU(new[] { 1, double.NaN }, new[] { 2.0, 3 }));
            Check.Throws<ArgumentException>(() => HypothesisTests.OneSampleT(new[] { 1, double.NaN, 3 }, 0));
            Check.Throws<ArgumentException>(() => HypothesisTests.TwoSampleT(new[] { 1.0, 2 }, new[] { 1, double.PositiveInfinity }));
            Check.Throws<ArgumentException>(() => HypothesisTests.KolmogorovSmirnovOneSample(new[] { 1.0, 2 }, x => double.NaN));
        }

        public void HypothesisTests_ConstantSample_HasZeroVariance()
        {
            // mean(0.1, 0.1, 0.1) is 0.10000000000000002 by naive summation: a variance of 2e-34 and t ≈ 2e15 against μ = 0.1.
            var r = HypothesisTests.OneSampleT(new[] { 0.1, 0.1, 0.1 }, 0.1);
            Check.Equal(0.0, r.Statistic);
            Check.Equal(1.0, r.PValue);
            var w = HypothesisTests.TwoSampleT(new[] { 0.1, 0.1, 0.1 }, new[] { 0.1, 0.1, 0.1, 0.1 });
            Check.Equal(1.0, w.PValue);
        }

        public void HypothesisTests_GoodnessOfFit_ValidatesCounts()
        {
            Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareGoodnessOfFit(new[] { 10.0, 20 }, new[] { 0.5, 0.5 }));   // proportions, not counts
            Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareGoodnessOfFit(new[] { 10.0, double.NaN }, new[] { 15.0, 15 }));
            Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareGoodnessOfFit(new[] { 10.0, 20 }, new[] { double.NaN, 15 }));
            Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareGoodnessOfFit(new[] { -5.0, 35 }, new[] { 15.0, 15 }));
            var ok = HypothesisTests.ChiSquareGoodnessOfFit(new[] { 10.0, 20 }, new[] { 15.0, 15 });
            Check.Close(10.0 / 3, ok.Statistic, 1e-14);
        }

        public void HypothesisTests_Independence_RejectsNaNCounts()
            => Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareIndependence(new[,] { { 1, double.NaN }, { 3, 4 } }));

        // ---- Correlation / LinearRegression ----

        public void LinearRegression_ConstantX_WithRoundOffMean_Throws()
        {
            // sxx was 2e-34 of round-off, not 0: slope ≈ 1e33 instead of "x is constant".
            Check.Throws<ArgumentException>(() => LinearRegression.Fit(new[] { 0.1, 0.1, 0.1 }, new[] { 1.0, 2, 3 }));
            Check.True(double.IsNaN(Correlation.Pearson(new[] { 0.1, 0.1, 0.1 }, new[] { 1.0, 2, 3 })), "constant x");
        }

        public void Correlation_DoesNotOverflowOrUnderflow()
        {
            double[] x = { 1, 2, 3, 4 }, y = { 2, 1, 4, 3 };
            double r = Correlation.Pearson(x, y);
            Rel(r, Correlation.Pearson(x.Select(v => v * 1e80).ToArray(), y.Select(v => v * 1e80).ToArray()), 1e-14, "large");
            Rel(r, Correlation.Pearson(x.Select(v => v * 1e-80).ToArray(), y.Select(v => v * 1e-80).ToArray()), 1e-14, "tiny");
            double r2 = LinearRegression.Fit(x, y).RSquared;
            Rel(r2, LinearRegression.Fit(x.Select(v => v * 1e80).ToArray(), y.Select(v => v * 1e80).ToArray()).RSquared, 1e-14, "R² large");
            Rel(r2, LinearRegression.Fit(x.Select(v => v * 1e-80).ToArray(), y.Select(v => v * 1e-80).ToArray()).RSquared, 1e-14, "R² tiny");
        }

        // ---- PermutationTest / Bootstrap ----

        public void PermutationTest_InfiniteNullValue_DoesNotMakeEverythingATie()
        {
            // Statistic: ratio of means. Observed 7.5/2 = 3.75; the rearrangement with second group {1, 2, −3} gives +∞,
            // which made the tie tolerance infinite: every rearrangement a tie, p = 1.
            Func<IReadOnlyList<double>, IReadOnlyList<double>, double> ratio = (g1, g2) => g1.Average() / g2.Average();
            var r = PermutationTest.TwoSample(new[] { 10.0, 11, 12, -3 }, new[] { 1.0, 2, 3 }, new Random(1), statistic: ratio,
                alternative: Alternative.Greater);
            Check.Equal(3.75, r.Observed);
            Check.True(r.NullDistribution.Any(double.IsPositiveInfinity), "an infinite rearrangement");
            Check.True(r.PValue < 0.5, $"p = {r.PValue}");
        }

        public void PermutationTest_NaN_IsRejected()
        {
            // NaN data gave p = 0 (every comparison false): "significant".
            Check.Throws<ArgumentException>(() => PermutationTest.TwoSample(new[] { 1, double.NaN }, new[] { 2.0, 3 }, new Random(1)));
            Check.Throws<ArgumentException>(() => PermutationTest.Paired(new[] { 1, double.NaN }, new[] { 2.0, 3 }, new Random(1)));
            Check.Throws<InvalidOperationException>(() => PermutationTest.TwoSample(new[] { 1.0, 2 }, new[] { 2.0, 3 }, new Random(1),
                statistic: (g1, g2) => double.NaN));
        }

        public void Bootstrap_NaNConfidence_IsRejected()
        {
            // Was IndexOutOfRangeException from the percentile lookup.
            Check.Throws<ArgumentOutOfRangeException>(() =>
                Bootstrap.PercentileInterval(new[] { 1.0, 2, 3 }, v => v.Average(), new Random(1), confidence: double.NaN));
            Check.Throws<InvalidOperationException>(() =>
                Bootstrap.PercentileInterval(new[] { 1.0, 2, 3 }, v => double.NaN, new Random(1)));
        }

        public void ConfidenceInterval_NaNConfidence_NamesTheParameter()
        {
            // Was thrown from NormalQuantile with ParamName "p".
            foreach (Action a in new Action[]
            {
                () => ConfidenceInterval.ForMean(new[] { 1.0, 2, 3 }, double.NaN),
                () => ConfidenceInterval.ForMeanT(new[] { 1.0, 2, 3 }, double.NaN),
                () => ConfidenceInterval.ForVariance(new[] { 1.0, 2, 3 }, double.NaN),
                () => ConfidenceInterval.ForProportion(3, 10, double.NaN),
            })
            {
                string? name = null;
                try { a(); }
                catch (ArgumentOutOfRangeException e) { name = e.ParamName; }
                Check.Equal("confidence", name);
            }
        }

        // ---- DistributionFit ----

        public void DistributionFit_Gamma_HandlesTightData()
        {
            // CV ≈ 1.4e-8: ln x̄ − mean(ln x) cancelled to below 1e-14 and FitGamma (and so FitAll) threw "all values equal".
            double[] x = { 1, 1 + 1e-8, 1 - 1e-8, 1 + 2e-8, 1 - 2e-8 };
            double variance = x.Sum(v => (v - 1) * (v - 1)) / x.Length;
            var g = DistributionFit.FitGamma(x);
            Rel(1 / variance, g.Parameters["shape"], 1e-6, "shape ≈ mean²/var");
            Check.True(DistributionFit.FitAll(x).Count >= 2, "FitAll");
        }

        public void DistributionFit_HandlesValuesNear1e308()
        {
            double[] small = { 1, 1.7, 1.3, 1.1 };
            double[] big = small.Select(v => v * 1e308).ToArray();
            var n = DistributionFit.FitNormal(big);
            Rel(DistributionFit.FitNormal(small).Parameters["sd"] * 1e308, n.Parameters["sd"], 1e-14, "normal sd");
            Check.True(!double.IsInfinity(n.LogLikelihood) && !double.IsNaN(n.LogLikelihood), "normal LL");
            Rel(DistributionFit.FitExponential(small).Parameters["rate"] / 1e308, DistributionFit.FitExponential(big).Parameters["rate"], 1e-14, "rate");
            Rel(DistributionFit.FitGamma(small).Parameters["shape"], DistributionFit.FitGamma(big).Parameters["shape"], 1e-12, "gamma shape");
            Check.True(DistributionFit.FitAll(big).All(f => !double.IsNaN(f.Aic) && !double.IsInfinity(f.Aic)), "FitAll AIC finite");
        }

        // ---- Distributions ----

        public void Distributions_StudentTQuantile_ReachesTheExtremeTails()
        {
            // Pinned at √double.MaxValue = 1.34e154 (x² overflowed in the tail, then lo·hi in the bisection).
            Rel(-3.1830988618379066356e+299, Distributions.StudentTQuantile(1e-300, 1), 1e-14, "tq(1e-300; 1)");
            Rel(-1.0284911563163399707e+199, Distributions.StudentTQuantile(1e-100, 0.5), 1e-14, "tq(1e-100; ½)");
            Rel(-1.0331108360446529009e+100, Distributions.StudentTQuantile(1e-300, 3), 1e-14, "tq(1e-300; 3)");
            Rel(3.1830988618379065482e-300, Distributions.StudentTSurvival(1e299, 1), 1e-14, "S(1e299; 1)");
            Rel(3.2070097541422290519e-101, Distributions.StudentTSurvival(1e200, 0.5), 1e-14, "S(1e200; ½)");
            Check.Equal(double.NegativeInfinity, Distributions.StudentTQuantile(1e-20, 0.05));    // ≈ −1e400: overflows
        }

        public void Distributions_Pdfs_AtHugeAndInfiniteArguments()
        {
            // Log1p(∞) was NaN (∞·∞/∞), and x² overflowed: NaN densities.
            Rel(3.1830988618379066738e-321, Distributions.StudentTPdf(1e160, 1), 1e-3, "tpdf(1e160; 1) (subnormal)");
            Check.Equal(0.0, Distributions.StudentTPdf(1e160, 3));
            Check.Equal(0.0, Distributions.StudentTPdf(double.PositiveInfinity, 3));
            Check.Equal(0.0, Distributions.StudentTPdf(double.NegativeInfinity, 3));
            Check.Equal(0.0, Distributions.ChiSquarePdf(double.PositiveInfinity, 3));
            Check.Equal(0.0, Distributions.FPdf(double.PositiveInfinity, 3, 4));
        }

        public void Distributions_Exponential_IsAccurateForSmallArguments()
        {
            // 1 − e^(−1e-20) rounded to 0, and −ln(1 − 1e-20) to −0.
            Rel(1e-20, Distributions.ExponentialCdf(1e-20, 1), 1e-15, "ExponentialCdf");
            Rel(1e-20, Distributions.ExponentialQuantile(1e-20, 1), 1e-15, "ExponentialQuantile");
            Rel(-Math.Log(1e-12) / 2, Distributions.ExponentialQuantile(1 - 1e-12, 2), 1e-4, "upper quantile");
            Check.Equal(1.0, Distributions.ExponentialCdf(double.PositiveInfinity, 1));
        }

        public void Distributions_NormalQuantile_IsAccurateNearTheMedian()
        {
            // The Halley step measured its error as 1 − p − ½Q (absolute precision only): 1e-9 relative at ½ + 1e-10.
            Rel(2.5066284820303539022e-10, Distributions.NormalQuantile(0.5 + 1e-10), 1e-14, "½ + 1e-10");
            Rel(2.5065728237018604669e-12, Distributions.NormalQuantile(0.5 + 1e-12), 1e-14, "½ + 1e-12");
            Rel(-2.50462478220459023e-15, Distributions.NormalQuantile(0.5 - 1e-15), 1e-14, "½ − 1e-15");
            Rel(0.000075198848309739319681, Distributions.NormalQuantile(0.5 + 3e-5), 1e-14, "½ + 3e-5");
            Rel(0.52440051270804065631, Distributions.NormalQuantile(0.7), 1e-14, "0.7");
        }

        public void Distributions_ValidationGaps()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.ExponentialQuantile(double.NaN, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.UniformCdf(0, double.NegativeInfinity, double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.UniformCdf(0, double.NaN, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.NormalQuantile(0.3, 0, double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.ExponentialPdf(1, double.PositiveInfinity));
            try { Distributions.UniformPdf(0, 2, 1); Check.True(false, "did not throw"); }
            catch (ArgumentException e) { Check.Equal("b", e.ParamName); }
        }

        // ---- ControlChart ----

        public void ControlChart_NaN_DoesNotCompleteRunsOrTrends()
        {
            // NaN compares false both ways, so it counted as above, below, rising and falling at once.
            double[] runs = { 1, 1, 1, 1, double.NaN, 1, 1, 1, 1 };
            Check.Throws<ArgumentException>(() => ControlChart.RunsOnOneSide(runs, 0, 9));
            Check.Throws<ArgumentException>(() => ControlChart.Trends(new[] { 1, 2, double.NaN, 4, 5, 6 }, 6));
            Check.Throws<ArgumentException>(() => ControlChart.Individuals(new[] { 1, double.NaN, 3 }));
            var limits = ControlChart.Individuals(new[] { 1.0, 2, 3 });
            Check.Throws<ArgumentException>(() => ControlChart.PointsBeyondLimits(new[] { 1, double.NaN }, limits));
        }

        public void ControlChart_NaNCenterLine_IsRejected()
            // Every point was both "not ≤ NaN" and "not ≥ NaN": every window was a run.
            => Check.Throws<ArgumentOutOfRangeException>(() => ControlChart.RunsOnOneSide(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, double.NaN, 9));

        public void ControlChart_ConstantData_IsInControl()
        {
            // mean(0.1 ×3) was 0.10000000000000002 with a zero moving range: every point fell below the LCL.
            double[] data = { 0.1, 0.1, 0.1 };
            var limits = ControlChart.Individuals(data);
            Check.Equal(0.1, limits.CenterLine);
            Check.Equal(0, ControlChart.PointsBeyondLimits(data, limits).Count);
        }

        public void ControlChart_Rules_MatchTheWindowDefinition()
        {
            // The one-pass counters must flag exactly the windows the definition does.
            var rng = new Random(7);
            for (int trial = 0; trial < 300; trial++)
            {
                var data = new double[rng.Next(1, 40)];
                for (int i = 0; i < data.Length; i++) data[i] = rng.Next(-2, 3);
                int len = rng.Next(1, 8);
                var runs = new List<int>();
                for (int i = len - 1; i < data.Length; i++)
                {
                    bool above = true, below = true;
                    for (int k = i - len + 1; k <= i; k++) { if (data[k] <= 0) above = false; if (data[k] >= 0) below = false; }
                    if (above || below) runs.Add(i);
                }
                Check.True(runs.SequenceEqual(ControlChart.RunsOnOneSide(data, 0, len)), $"runs trial {trial}");
                int tl = Math.Max(2, len);
                var trends = new List<int>();
                for (int i = tl - 1; i < data.Length; i++)
                {
                    bool inc = true, dec = true;
                    for (int k = i - tl + 2; k <= i; k++) { if (data[k] <= data[k - 1]) inc = false; if (data[k] >= data[k - 1]) dec = false; }
                    if (inc || dec) trends.Add(i);
                }
                Check.True(trends.SequenceEqual(ControlChart.Trends(data, tl)), $"trends trial {trial}");
            }
        }

        public void ControlChart_RunsOnOneSide_IsLinear()
        {
            // Was O(n·runLength): 200,000 points with runLength 100,000 took over a minute.
            var data = Enumerable.Repeat(1.0, 200_000).ToArray();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Check.Equal(100_001, ControlChart.RunsOnOneSide(data, 0, 100_000).Count);
            Check.Equal(0, ControlChart.Trends(data, 100_000).Count);
            Check.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
        }

        // ---- ProcessCapability ----

        public void ProcessCapability_Ppm_KeepsTheUpperTail()
        {
            // 1 − Φ(z) rounds to 0 past z ≈ 8.3, so a far upper spec reported 0 ppm; the lower tail never did.
            double[] sample = { -1, 1, -1, 1 };                         // mean 0, sd = √(4/3)
            double sd = Math.Sqrt(4.0 / 3);
            var up = ProcessCapability.Compute(sample, -1000, 10 * sd);
            var down = ProcessCapability.Compute(sample, -10 * sd, 1000);
            Rel(7.6198530241605260659e-24 * 1e6, up.PpmDefective, 1e-12, "upper tail ppm");     // Q(10), mpmath
            Rel(down.PpmDefective, up.PpmDefective, 1e-12, "symmetric");
        }

        public void ProcessCapability_ValidatesInputs()
        {
            double[] sample = { 1, 2, 3, 4 };
            Check.Throws<ArgumentOutOfRangeException>(() => ProcessCapability.Compute(sample, double.NaN, 5));
            Check.Throws<ArgumentOutOfRangeException>(() => ProcessCapability.Compute(sample, 0, double.NaN));
            Check.Throws<ArgumentException>(() => ProcessCapability.Compute(new[] { 1, double.NaN, 3 }, 0, 5));   // NaN sigma passed "<= 0"
            Check.Throws<ArgumentException>(() => ProcessCapability.Compute(new[] { 1, double.PositiveInfinity, 3 }, 0, 5));
            // Constant data: the mean was an ulp off, so sigma ≈ 1e-17 and Cp ≈ 1e16 instead of "zero variation".
            Check.Throws<ArgumentException>(() => ProcessCapability.Compute(new[] { 0.1, 0.1, 0.1 }, 0, 1));
            var oneSided = ProcessCapability.Compute(sample, double.NegativeInfinity, 10);
            Check.True(double.IsPositiveInfinity(oneSided.Cp), "one-sided Cp");
            Check.True(oneSided.Cpk > 0 && !double.IsInfinity(oneSided.Cpk), "one-sided Cpk");
        }

        // ---- MultipleRegression / RobustRegression ----

        public void MultipleRegression_NaN_IsNotCalledCollinear()
        {
            var x = new double[,] { { 1, 2 }, { 2, 1 }, { 3, 5 }, { double.NaN, 4 }, { 5, 3 } };
            double[] y = { 1, 2, 3, 4, 5 };
            var e = Check.Throws<ArgumentException>(() => MultipleRegression.Fit(x, y));
            Check.True(e.Message.Contains("not finite"), e.Message);
            var x2 = new double[,] { { 1, 2 }, { 2, 1 }, { 3, 5 }, { 4, 4 }, { 5, 3 } };
            Check.Throws<ArgumentException>(() => MultipleRegression.Fit(x2, new[] { 1, 2, double.NaN, 4, 5 }));
        }

        public void MultipleRegression_ResultListsAreReadOnly()
        {
            var r = MultipleRegression.Fit(new double[,] { { 1 }, { 2 }, { 3 }, { 4 } }, new[] { 2.0, 4, 6, 8.5 });
            Check.True(!(r.Coefficients is double[]), "Coefficients");
            Check.True(!(r.Residuals is double[]) && !(r.Fitted is double[]) && !(r.PValues is double[]), "other lists");
        }

        public void RobustRegression_TheilSen_RejectsNaNAndDoesNotOverflow()
        {
            Check.Throws<ArgumentException>(() => RobustRegression.TheilSen(new[] { 1, 2, double.NaN }, new[] { 1.0, 2, 3 }));
            Check.Throws<ArgumentException>(() => RobustRegression.TheilSen(new[] { 1.0, 2, 3 }, new[] { 1, double.PositiveInfinity, 3 }));
            // Intercepts y − 0·x = ±1.5e308 pairs: their median's sum overflowed to ∞.
            var (slope, intercept) = RobustRegression.TheilSen(new[] { 1.0, 2, 3, 4 }, new[] { 1.5e308, 1.5e308, 1.6e308, 1.6e308 });
            Check.True(!double.IsInfinity(intercept) && !double.IsNaN(intercept), $"intercept {intercept}");
        }

        // ---- MeasurementAgreement ----

        public void MeasurementAgreement_ValidatesZAndValues()
        {
            double[] a = { 1, 2, 3 }, b = { 1.1, 2.2, 2.9 };
            Check.Throws<ArgumentOutOfRangeException>(() => MeasurementAgreement.BlandAltman(a, b, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => MeasurementAgreement.BlandAltman(a, b, -1.96));
            Check.Throws<ArgumentOutOfRangeException>(() => MeasurementAgreement.BlandAltman(a, b, double.PositiveInfinity));
            Check.Throws<ArgumentException>(() => MeasurementAgreement.BlandAltman(new[] { 1, double.NaN, 3 }, b));
            // A constant offset: exactly that bias with zero spread (was an ulp off with a 1e-17 sd).
            var r = MeasurementAgreement.BlandAltman(new[] { 0.3, 0.3, 0.3 }, new[] { 0.2, 0.2, 0.2 });
            Check.Equal(0.3 - 0.2, r.Bias);
            Check.Equal(0.0, r.StdDevOfDifferences);
        }
    }
}
