using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 23 (statistics, second pass). References are 50-digit mpmath unless noted. Scaled
    // data use powers of two, so the scaled sample is exactly the unscaled one times the factor.
    public sealed class StatisticsReview2Tests
    {
        private static readonly double Tiny = Math.Pow(2, -565);   // ≈ 1.4e-170: squares underflow
        private static readonly double Huge = Math.Pow(2, 665);    // ≈ 1.2e200: squares overflow

        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        private static double[] Times(double[] x, double s) => x.Select(v => v * s).ToArray();

        // ---- tests at extreme scales ----

        public void TwoSampleT_Welch_IsScaleInvariant()
        {
            // The Welch–Satterthwaite df squared the variances of the means: 0/0 at 1e-100, ∞/∞ at 1e100, and the NaN df
            // threw ArgumentOutOfRangeException("degreesOfFreedom").
            double[] a = { 1, 2, 3 }, b = { 4, 6, 9 };
            foreach (double s in new[] { 1.0, Math.Pow(2, -332), Math.Pow(2, 332) })
            {
                var r = HypothesisTests.TwoSampleT(Times(a, s), Times(b, s));
                Rel(-2.7716093126229356459, r.Statistic, 1e-13, $"t at {s:G3}");
                Rel(2.6162162162162162162, r.DegreesOfFreedom, 1e-13, $"df at {s:G3}");
                Rel(0.081071588289661192128, r.PValue, 1e-11, $"p at {s:G3}");
            }
        }

        public void OneSampleT_IsScaleInvariant()
        {
            // The squared deviations underflowed: t = +∞, p = 0 at 1e-170.
            foreach (double s in new[] { 1.0, Tiny, Huge })
            {
                var r = HypothesisTests.OneSampleT(Times(new[] { 1.0, 2, 3 }, s), 0);
                Rel(3.4641016151377545871, r.Statistic, 1e-14, $"t at {s:G3}");
                Rel(0.074179900227448538433, r.PValue, 1e-12, $"p at {s:G3}");
            }
        }

        public void Pearson_IsScaleInvariant()
        {
            // NaN at 1e-170 (sums of squares underflowed), 0 at 1e200 (∞/∞ read as... a zero correlation).
            foreach (double s in new[] { Tiny, Huge })
                Check.Equal(1.0, Correlation.Pearson(Times(new[] { 0.0, 1, 2 }, s), new[] { 0.0, 1, 2 }), $"at {s:G3}");
        }

        public void LinearRegression_IsScaleInvariant()
        {
            // "All x values are identical" at 1e-170; slope 0 at 1e200.
            foreach (double s in new[] { Tiny, Huge })
            {
                var fit = LinearRegression.Fit(Times(new[] { 0.0, 1, 2 }, s), new[] { 0.0, 1, 2 });
                Rel(1 / s, fit.Slope, 1e-15, $"slope at {s:G3}");
                Rel(1.0, fit.RSquared, 1e-15, $"R² at {s:G3}");
            }
        }

        public void ChangePoint_DetectShift_IsScaleInvariant()
        {
            // The scores k(n − k)/n·Δ² under- or overflowed into ties, and the first split won.
            foreach (double s in new[] { 1.0, Tiny, Huge })
                Check.Equal(3, ChangePoint.DetectShift(Times(new[] { 0.0, 0, 0, 1, 1 }, s)).Index, $"at {s:G3}");
        }

        public void Anova_OneWay_IsScaleInvariant()
        {
            // F = 0, p = 1 at 1e170: the round-off floor (1e-14·max)² overflowed to ∞ and zeroed every sum of squares.
            var reference = Anova.OneWay(new[] { 1.0, 1.25 }, new[] { 2.0, 2.25 });
            foreach (double s in new[] { Tiny, Math.Pow(2, 565) })
            {
                var r = Anova.OneWay(new[] { s, 1.25 * s }, new[] { 2 * s, 2.25 * s });
                Rel(reference.FStatistic, r.FStatistic, 1e-14, $"F at {s:G3}");
                Rel(reference.PValue, r.PValue, 1e-14, $"p at {s:G3}");
            }
        }

        public void GaugeRR_IsScaleInvariant()
        {
            // "Every measurement is the same" at 1e±170: the sums of squares underflowed, or the floor overflowed.
            double[,,] Study(double s) => new double[,,]
            {
                { { 1.0 * s, 1.25 * s }, { 1.125 * s, 1.375 * s } },
                { { 2.0 * s, 2.125 * s }, { 2.375 * s, 2.25 * s } },
                { { 3.0 * s, 3.5 * s }, { 3.25 * s, 3.125 * s } },
            };
            var reference = GaugeRR.Analyze(Study(1));
            foreach (double s in new[] { Tiny, Math.Pow(2, 565) })
            {
                var r = GaugeRR.Analyze(Study(s));
                Rel(reference.TotalGaugeRR.PercentStudyVariation, r.TotalGaugeRR.PercentStudyVariation, 1e-13, $"%SV at {s:G3}");
                Rel(reference.PartToPart.PercentContribution, r.PartToPart.PercentContribution, 1e-13, $"%Contrib at {s:G3}");
                Rel(reference.InteractionP, r.InteractionP, 1e-13, $"interaction p at {s:G3}");
                Check.Equal(reference.DistinctCategories, r.DistinctCategories, $"ndc at {s:G3}");
                Rel(reference.TotalGaugeRR.StudyVariation * s, r.TotalGaugeRR.StudyVariation, 1e-13, $"study variation at {s:G3}");
            }
        }

        // ---- distribution fitting ----

        public void FitGamma_HandlesValuesFarBelowTheMean()
        {
            // d = v/x̄ − 1 rounded to −1, ln(1 + d) = −∞, and the NaN shape threw ArgumentOutOfRangeException("degreesOfFreedom").
            var fit = DistributionFit.FitGamma(new[] { 1e-20, 1, 2 });
            Rel(0.057160894379169902327, fit.Parameters["shape"], 1e-10, "shape");
            Rel(17.49447783945822377, fit.Parameters["scale"], 1e-10, "scale");
            Check.True(DistributionFit.FitAll(new[] { 1e-20, 1, 2 }).Any(f => f.Family == DistributionFamily.Gamma), "FitAll keeps Gamma");
        }

        public void FitWeibull_HandlesARangeBeyondTheDoubleRange()
        {
            // ln(v/max) was ln(0) = −∞ for v/max below 1e-308.
            var fit = DistributionFit.FitWeibull(new[] { 1e-300, 1e-200, 1, 1e200, 1e300 });
            double k = fit.Parameters["shape"];
            Check.True(k > 0 && !double.IsInfinity(k), $"shape {k}");
        }

        // ---- quantiles far in the lower tail ----

        public void ChiSquareQuantile_ConvergesForTinyP()
        {
            // The Newton steps shrank x by a constant factor and ran out of iterations: 4.55e-98 for any p below ~1e-250.
            Rel(3.2334077805831283472e-120, Distributions.ChiSquareQuantile(1e-300, 5), 1e-12, "χ²(5) at 1e-300");
            foreach (double k in new[] { 5.0, 10, 50, 300 })
                foreach (double p in new[] { 1e-150, 1e-250, 1e-300 })
                    Rel(p, Distributions.ChiSquareCdf(Distributions.ChiSquareQuantile(p, k), k), 1e-10, $"round trip χ²({k}) at {p:G3}");
        }

        public void FQuantile_ConvergesForTinyP()
        {
            Rel(1.3825832025643808207e-14, Distributions.FQuantile(1e-200, 30, 30), 1e-12, "F(30, 30) at 1e-200");
            foreach (var (d1, d2) in new[] { (10.0, 10.0), (100.0, 100.0), (3.0, 40.0), (40.0, 3.0) })
                foreach (double p in new[] { 1e-150, 1e-200, 1e-300 })
                    Rel(p, Distributions.FCdf(Distributions.FQuantile(p, d1, d2), d1, d2), 1e-9, $"round trip F({d1}, {d2}) at {p:G3}");
        }

        // ---- special functions ----

        public void LnGamma_IsRelativelyAccurateNearOneAndTwo()
        {
            // The Lanczos sum is accurate only absolutely: ~1e-5 relative error where ln Γ crosses zero at 1 and 2.
            Rel(-5.7726697970945634782e-13, SpecialFunctions.LnGamma(1.000000000001), 1e-14, "lnΓ(1 + 1e-12)");
            Rel(-5.772157125783244041e-11, SpecialFunctions.LnGamma(1.0000000001), 1e-14, "lnΓ(1 + 1e-10)");
            Rel(4.2278437011204658672e-11, SpecialFunctions.LnGamma(2.0000000001), 1e-14, "lnΓ(2 + 1e-10)");
            Rel(-4.2278436975733279119e-10, SpecialFunctions.LnGamma(1.999999999), 1e-14, "lnΓ(2 − 1e-9)");
            Rel(-0.049872441259839761785, SpecialFunctions.LnGamma(1.1), 1e-14, "lnΓ(1.1)");
            Rel(0.066376239734742954426, SpecialFunctions.LnGamma(0.9), 1e-14, "lnΓ(0.9)");
            Rel(-0.038984275923083361674, SpecialFunctions.LnGamma(1.9), 1e-14, "lnΓ(1.9)");
            Rel(0.070455733704111769367, SpecialFunctions.LnGamma(2.15), 1e-14, "lnΓ(2.15)");
            Rel(0.1065951164781176567, SpecialFunctions.LnGamma(0.85), 1e-14, "lnΓ(0.85)");
            Rel(-0.098271836421813161464, SpecialFunctions.LnGamma(1.25), 1e-14, "lnΓ(1.25)");
            Rel(-0.085374090003315836884, SpecialFunctions.LnGamma(1.2), 1e-14, "lnΓ(1.2)");
            Rel(-0.071083872914372154332, SpecialFunctions.LnGamma(1.8), 1e-14, "lnΓ(1.8)");
            Rel(0.096947466790638873178, SpecialFunctions.LnGamma(2.2), 1e-14, "lnΓ(2.2)");
            Rel(0.15205967839983754592, SpecialFunctions.LnGamma(0.8), 1e-14, "lnΓ(0.8)");
        }

        public void ChiSquareSurvival_IsAccurateForTinyDegreesOfFreedom()
        {
            // Q was formed as 1 − P: P ≈ 1 for a tiny shape, so Q lost relative precision (3e-8 at df 1e-8).
            Rel(3.512093306454666206e-8, Distributions.ChiSquareSurvival(0.001, 1e-8), 1e-12, "χ²(1e-8) at 0.001");
            Rel(2.1938617914074710748e-6, Distributions.ChiSquareSurvival(2, 2e-5), 1e-12, "χ²(2e-5) at 2");
            Rel(0.15604916709686203475, Distributions.ChiSquareSurvival(0.5, 0.3), 1e-13, "χ²(0.3) at 0.5");
            Rel(0.024452116006979748012, Distributions.ChiSquareSurvival(0.1, 0.02), 1e-13, "χ²(0.02) at 0.1");
            Rel(3.512093306454666206e-8, SpecialFunctions.RegularizedGammaQ(5e-9, 0.0005), 1e-12, "Q(5e-9, 0.0005)");
        }

        // ---- confidence intervals ----

        public void ForProportion_HandlesMoreThanAbillionTrials()
        {
            // 2·trials overflowed int: a negative divisor (OverflowException under checked arithmetic).
            var (lo, hi) = ConfidenceInterval.ForProportion(600_000_000, 1_200_000_000);
            Check.True(lo < 0.5 && hi > 0.5 && hi - lo < 1e-4, $"[{lo}, {hi}]");
            var (lo1, hi1) = ConfidenceInterval.ForProportion(1, int.MaxValue);
            Check.True(lo1 >= 0 && hi1 > lo1 && hi1 < 1e-8, $"[{lo1}, {hi1}]");
        }

        public void ForProportion_StaysInsideZeroToOne()
        {
            // center − half rounded to −1.4e-17 at 0 successes, and center + half to 1.0000000000000002 at all successes.
            foreach (int n in new[] { 19, 31, 38 }) Check.Equal(0.0, ConfidenceInterval.ForProportion(0, n).Lower, $"0/{n}");
            foreach (int n in new[] { 9, 38 }) Check.Equal(1.0, ConfidenceInterval.ForProportion(n, n).Upper, $"{n}/{n}");
        }

        // ---- validation ----

        public void Anova_OneWay_RejectsNonFiniteValues()
        {
            Check.Throws<ArgumentException>(() => Anova.OneWay(new[] { 1, double.NaN }, new[] { 3.0, 4 }));
            Check.Throws<ArgumentException>(() => Anova.OneWay(new[] { 1, double.PositiveInfinity }, new[] { 3.0, 4 }));
        }

        public void MultipleRegression_RejectsANullFirstRow()
        {
            // rows[0].Count ran before the per-row null check: NullReferenceException.
            Check.Throws<ArgumentException>(() => MultipleRegression.Fit(new IReadOnlyList<double>[] { null!, new[] { 1.0 } }, new[] { 1.0, 2 }));
        }

        public void PermutationTest_Paired_RejectsOverflowingDifferences()
        {
            // ∞ − ∞ in the mean surfaced as "The statistic returned NaN on the observed data".
            Check.Throws<ArgumentException>(() => PermutationTest.Paired(new[] { 1e308, -1e308 }, new[] { -1e308, 1e308 }, new Random(1)));
        }

        public void PermutationTest_MeanDifference_DoesNotOverflowToNaN()
        {
            Check.Equal(0.0, PermutationTest.MeanDifference(new[] { 1e308, 1e308 }, new[] { 1e308, 1e308 }));
            Check.Equal(1e308, PermutationTest.MeanDifference(new[] { 1e308, 1e308 }, new[] { 0.0, 0 }));
        }

        // ---- regression summaries ----

        public void MultipleRegression_ConstantResponseHasPerfectFit()
        {
            // ȳ = 0.30000000000000004/3 left sst ≈ 1e-33, so R² was −3.3; and an exactly constant y gave adjusted R² NaN.
            var r = MultipleRegression.Fit(new double[,] { { 1 }, { 2 }, { 3 } }, new[] { 0.1, 0.1, 0.1 });
            Check.Equal(1.0, r.RSquared, "R² for 0.1 ×3");
            Check.Equal(1.0, r.AdjustedRSquared, "adjusted R² for 0.1 ×3");
            var r2 = MultipleRegression.Fit(new double[,] { { 1 }, { 2 }, { 3 }, { 4 }, { 5 } }, new[] { 0.7, 0.7, 0.7, 0.7, 0.7 });
            Check.Equal(1.0, r2.RSquared, "R² for 0.7 ×5");
            Check.Equal(1.0, r2.AdjustedRSquared, "adjusted R² for 0.7 ×5");
        }

        // ---- quantile interpolation ----

        public void Bootstrap_ConstantStatisticHasAZeroWidthInterval()
        {
            // a·(1 − f) + b·f put 0.3 at 0.29999999999999993 (the copy in EmpiricalDistribution was fixed in round 3).
            var r = Bootstrap.PercentileInterval(new[] { 0.3, 0.3, 0.3 }, s => s[0], new Random(1), 10, 0.9);
            Check.Equal(0.3, r.Lower, "lower");
            Check.Equal(0.3, r.Upper, "upper");
            var r2 = Bootstrap.PercentileInterval(new[] { 123.456, 123.456 }, s => s[0], new Random(1), 10, 0.9);
            Check.Equal(123.456, r2.Lower, "lower");
            Check.Equal(123.456, r2.Upper, "upper");
        }

        public void Quantile_BetweenOppositeInfinities_IsNotNaN()
        {
            var e = new EmpiricalDistribution(new[] { double.NegativeInfinity, double.PositiveInfinity });
            Check.Equal(double.NegativeInfinity, e.Quantile(0.25));
            Check.Equal(double.PositiveInfinity, e.Quantile(0.75));
            Check.False(double.IsNaN(e.Quantile(0.5)), "median");
            var r = Bootstrap.PercentileInterval(new[] { double.NegativeInfinity, double.PositiveInfinity }, s => s[0], new Random(1), 10, 0.5);
            Check.False(double.IsNaN(r.Lower) || double.IsNaN(r.Upper), $"[{r.Lower}, {r.Upper}]");
        }

        public void TheilSen_RejectsMorePointsThanItsPairsCanHold()
        {
            // 65,537 points have 2,147,516,416 pairs, past int.MaxValue: the slope list overflowed while growing. It is
            // refused before any allocation (run only after the fix: at HEAD it tried to hold ~16 GB of slopes).
            var x = Enumerable.Range(0, 65537).Select(i => (double)i).ToArray();
            Check.Throws<ArgumentException>(() => RobustRegression.TheilSen(x, x));
        }
    }
}
