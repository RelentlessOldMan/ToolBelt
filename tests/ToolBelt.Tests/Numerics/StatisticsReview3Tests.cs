using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 45 (statistics, third pass): each test failed before its fix. References are mpmath
    // or scipy unless noted. Scaled data use powers of two, so the scaled sample is exactly the unscaled one times the
    // factor and the statistics must match the unscaled ones.
    public sealed class StatisticsReview3Tests
    {
        private static readonly double Big = Math.Pow(2, 1023);       // two of these sum past double.MaxValue
        private static readonly double Small = Math.Pow(2, -565);     // squares underflow
        private static readonly double Large = Math.Pow(2, 565);      // squares overflow

        private static double[] Times(double[] x, double k) => x.Select(v => v * k).ToArray();

        // ---------------- Sums of values near MaxValue ----------------

        public void OneSampleT_ValuesNearMaxValue_MatchTheScaledReference()
        {
            var reference = HypothesisTests.OneSampleT(new[] { 0.0, 1, 1 }, 0);
            var r = HypothesisTests.OneSampleT(new[] { 0.0, Big, Big }, 0);
            Check.Close(reference.Statistic, r.Statistic, 1e-14);
            Check.Close(reference.PValue, r.PValue, 1e-14);
        }

        public void TwoSampleT_MeansOfOppositeSignNearMaxValue_MatchTheScaledReference()
        {
            foreach (bool pooled in new[] { false, true })
            {
                var reference = HypothesisTests.TwoSampleT(new[] { 1.0, 1.75 }, new[] { -1.0, -1.75 }, pooled);
                var r = HypothesisTests.TwoSampleT(new[] { Big, 1.75 * Big }, new[] { -Big, -1.75 * Big }, pooled);
                Check.Close(reference.Statistic, r.Statistic, 1e-12);
                Check.Close(reference.PValue, r.PValue, 1e-12);
            }
        }

        public void PairedT_DifferenceOverflows_UsesTheHalves()
        {
            var reference = HypothesisTests.OneSampleT(new[] { 0.0, Big, -0.5 }, 0);
            var r = HypothesisTests.PairedT(new[] { 1.0, Big, 3 }, new[] { 1.0, -Big, 4 });
            Check.Close(reference.Statistic, r.Statistic, 1e-14);
        }

        public void PairedT_BadSecondSample_IsNamed()
        {
            var e = Check.Throws<ArgumentException>(() => HypothesisTests.PairedT(new[] { 1.0, 2, 3 }, new[] { 1.0, double.NaN, 3 }));
            Check.Equal("second", e.ParamName);
            e = Check.Throws<ArgumentException>(() => HypothesisTests.PairedT(new[] { 1.0, double.PositiveInfinity, 3 }, new[] { 1.0, 2, 3 }));
            Check.Equal("first", e.ParamName);
        }

        public void Pearson_ValuesNearMaxValue_IsOne()
        {
            Check.Close(1.0, Correlation.Pearson(new[] { 0.0, Big, Big }, new[] { 0.0, 1, 1 }), 1e-15);
        }

        public void LinearRegression_ValuesNearMaxValue_FitTheLine()
        {
            var fit = LinearRegression.Fit(new[] { 0.0, 1, 1 }, new[] { 0.0, Big, Big });
            Check.Close(Big, fit.Slope, 1e-15 * Big);
            Check.Close(0.0, fit.Intercept, 1e-15 * Big);
            Check.Close(1.0, fit.RSquared, 1e-15);
        }

        public void IndividualsChart_ValuesNearMaxValue_HaveFiniteLimits()
        {
            var reference = ControlChart.Individuals(new[] { 0.0, 1, 1 });
            var limits = ControlChart.Individuals(new[] { 0.0, Big, Big });
            Check.Close(reference.CenterLine * Big, limits.CenterLine, 1e-15 * Big);
            Check.Close(reference.UpperControlLimit / 2 * Big, limits.UpperControlLimit / 2, 1e-15 * Big);
        }

        public void DetectShift_ValuesNearMaxValue_FindsTheStep()
        {
            var (index, magnitude) = ChangePoint.DetectShift(new[] { Big, Big, Big, 0, 0, 0 });
            Check.Equal(3, index);
            Check.Close(Big, Math.Abs(magnitude), 1e-15 * Big);
            (index, _) = ChangePoint.DetectShift(new[] { 0, 0, 0, Big, Big, Big });
            Check.Equal(3, index);
        }

        public void PairedPermutation_DifferencesNearMaxValue_NullsStayFinite()
        {
            var r = PermutationTest.Paired(new[] { Big, Big }, new[] { 0.0, 0 }, new Random(1), alternative: Alternative.Less);
            Check.Equal(1.0, r.PValue);
        }

        // ---------------- Squares near 1e±160 ----------------

        public void ConfidenceIntervals_ScaledData_ScaleExactly()
        {
            var x = new[] { 1.0, 2, 4 };
            foreach (double k in new[] { Small, Large })
            {
                var t = ConfidenceInterval.ForMeanT(x, 0.95);
                var tk = ConfidenceInterval.ForMeanT(Times(x, k), 0.95);
                Check.Close(t.Lower * k, tk.Lower, 1e-14 * k);
                Check.Close(t.Upper * k, tk.Upper, 1e-14 * k);
                var z = ConfidenceInterval.ForMean(x, 0.95);
                var zk = ConfidenceInterval.ForMean(Times(x, k), 0.95);
                Check.Close(z.Upper * k, zk.Upper, 1e-14 * k);
                var s = ConfidenceInterval.ForStandardDeviation(x, 0.95);
                var sk = ConfidenceInterval.ForStandardDeviation(Times(x, k), 0.95);
                Check.Close(s.Lower * k, sk.Lower, 1e-14 * k);
                Check.Close(s.Upper * k, sk.Upper, 1e-14 * k);
            }
        }

        public void ConfidenceIntervals_ConstantData_AreAPoint()
        {
            var x = new[] { 0.1, 0.1, 0.1 };
            Check.Equal((0.0, 0.0), ConfidenceInterval.ForVariance(x));
            Check.Equal((0.1, 0.1), ConfidenceInterval.ForMeanT(x));
            Check.Equal((0.1, 0.1), ConfidenceInterval.ForMean(x));
        }

        public void ProcessCapability_TinyData_MatchesTheScaledReference()
        {
            var x = new[] { 1.0, 2, 3 };
            var reference = ProcessCapability.Compute(x, 0, 4);
            foreach (double k in new[] { Small, Large })
            {
                var r = ProcessCapability.Compute(Times(x, k), 0, 4 * k);
                Check.Close(reference.Cp, r.Cp, 1e-14);
                Check.Close(reference.Cpk, r.Cpk, 1e-14);
            }
        }

        public void BlandAltman_TinyDifferences_KeepTheirSpread()
        {
            var a = new[] { 1.0, 2, 4 };
            var reference = MeasurementAgreement.BlandAltman(a, new[] { 0.0, 0, 0 });
            foreach (double k in new[] { Small, Large })
            {
                var r = MeasurementAgreement.BlandAltman(Times(a, k), new[] { 0.0, 0, 0 });
                Check.Close(reference.StdDevOfDifferences * k, r.StdDevOfDifferences, 1e-14 * k);
            }
            var e = Check.Throws<ArgumentException>(() => MeasurementAgreement.BlandAltman(a, new[] { 0.0 }));
            Check.Equal("methodB", e.ParamName);
        }

        public void MultipleRegression_ScaledResponseOrPredictor_GivesTheSameFit()
        {
            var x = new double[6, 1];
            for (int i = 0; i < 6; i++) x[i, 0] = i + 1;
            var y = new[] { 3.0, 1, 4, 1, 5, 9 };
            var reference = MultipleRegression.Fit(x, y);
            Check.Close(0.48465, reference.RSquared, 1e-5);
            foreach (double k in new[] { Small, Large })
            {
                var r = MultipleRegression.Fit(x, Times(y, k));
                Check.Close(reference.RSquared, r.RSquared, 1e-14);
                Check.Close(reference.AdjustedRSquared, r.AdjustedRSquared, 1e-14);
                Check.Close(reference.ResidualStandardError * k, r.ResidualStandardError, 1e-14 * k);
                Check.Close(reference.PValues[1], r.PValues[1], 1e-14);
                Check.Close(reference.Coefficients[1] * k, r.Coefficients[1], 1e-14 * k);

                var xk = new double[6, 1];
                for (int i = 0; i < 6; i++) xk[i, 0] = x[i, 0] * k;
                r = MultipleRegression.Fit(xk, y);
                Check.Close(reference.PValues[1], r.PValues[1], 1e-14);
                Check.Close(reference.StandardErrors[1] / k, r.StandardErrors[1], 1e-14 / k);
                var ci = reference.PredictionInterval(new[] { 3.5 });
                var cik = r.PredictionInterval(new[] { 3.5 * k });
                Check.Close(ci.Lower, cik.Lower, 1e-13);
                Check.Close(ci.Upper, cik.Upper, 1e-13);
            }
        }

        public void Anova_ZeroSumOfSquaresNearMaxValue_IsZeroNotNaN()
        {
            double k = Math.Pow(2, 600);
            var r = Anova.OneWay(new[] { k, k }, new[] { 2 * k, 2 * k });
            Check.Equal(0.0, r.SumSquaresWithin);
            Check.Equal(0.0, r.MeanSquareWithin);
        }

        public void Anova_LargeCommonOffset_KeepsTheEffects()
        {
            var r = Anova.OneWay(new[] { 1e9 + 0.1, 1e9 + 0.2, 1e9 + 0.3 }, new[] { 1e9 + 0.4, 1e9 + 0.5, 1e9 + 0.6 });
            Check.Close(13.500000894067741, r.FStatistic, 1e-12 * 13.5);
        }

        public void GaugeRR_ZeroSumOfSquaresNearMaxValue_IsZeroNotNaN()
        {
            double k = Math.Pow(2, 600);
            var m = new double[2, 2, 2];
            for (int o = 0; o < 2; o++)
            {
                m[0, o, 0] = 1 * k; m[0, o, 1] = 1.5 * k;
                m[1, o, 0] = 3 * k; m[1, o, 1] = 3.25 * k;
            }
            var r = GaugeRR.Analyze(m);
            foreach (var row in r.Anova)
                Check.False(double.IsNaN(row.SumOfSquares), row.Source);
            Check.Equal(0.0, r.Anova.First(row => row.Source == "Operator").SumOfSquares);
        }

        // ---------------- Distributions and special functions ----------------

        public void BetaI_ArgumentRoundingToOne_PicksTheLowerTail()
        {
            Check.Close(4.1437e-69, Distributions.FCdf(0.5, 1e20, 1000), 1e-3 * 4.1437e-69);
            Check.Equal(1.0, Distributions.FSurvival(0.5, 1e20, 1000));
        }

        public void BetaI_TinyShapes_StayInsideTheUnitInterval()
        {
            foreach (double x in new[] { 1e-10, 0.5, 1e10 })
            {
                double c = Distributions.FCdf(x, 1e-100, 1e-20), s = Distributions.FSurvival(x, 1e-20, 1e-100);
                Check.True(c >= 0 && c <= 1, $"FCdf {c}");
                Check.True(s >= 0 && s <= 1, $"FSurvival {s}");
            }
            Check.True(Distributions.FCdf(1e15, 1e-20, 0.01) <= 1);
        }

        public void SubnormalShape_GivesTheLimitOrIsRejected()
        {
            Check.Equal(1.0, SpecialFunctions.RegularizedGammaP(5e-324, 0.5));
            Check.Equal(0.0, SpecialFunctions.RegularizedGammaQ(1e-310, 0.5) > 1e-300 ? 1.0 : 0.0);
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.StudentTCdf(1, 5e-324));
            Check.Throws<ArgumentOutOfRangeException>(() => Distributions.ChiSquareCdf(1, 5e-324));
        }

        public void UniformAndNormal_BoundsNearMaxValue_DoNotOverflow()
        {
            double m = 1e308;
            Check.Close(0.5, Distributions.UniformCdf(0, -m, m), 1e-15);
            Check.Equal(1.0, Distributions.UniformCdf(m, -m, m));
            Check.Close(5e-309, Distributions.UniformPdf(0, -m, m), 1e-15 * 5e-309);
            Check.Close(0.9772498680518208, Distributions.NormalCdf(m, -m, m), 1e-15);
            Check.Close(0.022750131948179195, Distributions.NormalSurvival(m, -m, m), 1e-14);
            Check.Close(5.399096651318805e-310, Distributions.NormalPdf(m, -m, m), 1e-14 * 5.4e-310);
        }

        public void FPdf_LargeDf_KeepsItsDigits()
        {
            Check.Close(18.116366248898405, Distributions.FPdf(1.01, 1e5, 1e5), 1e-13 * 18.1);
            Check.Close(180.76746717365164, Distributions.FPdf(1.001, 1e7, 1e7), 1e-12 * 180.8);
            Check.Close(0.07442150351359645, Distributions.FPdf(1.0001, 1e10, 1e10), 1e-9 * 0.0744);
        }

        // Reflection references are at the double nearest −20.000001 (and −100.000001), not the decimal.
        public void Gamma_IntegersAreExactAndReflectionIsAccurate()
        {
            Check.Equal(24.0, SpecialFunctions.Gamma(5));
            Check.Equal(362880.0, SpecialFunctions.Gamma(10));
            Check.Close(4.2690680090047053e304, SpecialFunctions.Gamma(170), 1e-14 * 4.27e304);
            Check.Close(1e300, SpecialFunctions.Gamma(1e-300), 1e-15 * 1e300);
            Check.Close(-4.110305203799352e-13, SpecialFunctions.Gamma(-20.000001), 1e-13 * 4.11e-13);
            Check.Close(-1.0715053510080591e-152, SpecialFunctions.Gamma(-100.000001), 1e-12 * 1.07e-152);
        }

        public void InfiniteShapes_GiveTheLimits()
        {
            Check.Equal(0.0, SpecialFunctions.Beta(double.PositiveInfinity, 1));
            Check.Equal(0.0, SpecialFunctions.RegularizedBetaI(0.5, double.PositiveInfinity, 1));
            Check.Equal(1.0, SpecialFunctions.RegularizedBetaI(0.5, 1, double.PositiveInfinity));
        }

        // ---------------- Fits ----------------

        public void FitWeibull_TinyShape_KeepsARepresentableScale()
        {
            var x = Enumerable.Range(0, 999).Select(i => 1e-300 * (1 + i / 1000.0)).Concat(new[] { 1e300 }).ToArray();
            var fit = DistributionFit.FitWeibull(x);
            double scale = fit.Parameters["scale"];
            Check.True(scale > 0 && !double.IsInfinity(scale), $"scale {scale}");
            Check.False(double.IsNaN(fit.LogLikelihood));
        }

        public void FitExponential_SubnormalMean_NamesTheSample()
        {
            var e = Check.Throws<ArgumentException>(() => DistributionFit.FitExponential(new[] { 1e-310, 2e-310 }));
            Check.Equal("sample", e.ParamName);
        }

        // ---------------- Argument names ----------------

        public void ArgumentNames_AreReported()
        {
            var one = new[] { 1.0 };
            Check.Equal("a", Check.Throws<ArgumentException>(() => PermutationTest.TwoSample(new double[0], one, new Random(1))).ParamName);
            Check.Equal("b", Check.Throws<ArgumentException>(() => PermutationTest.TwoSample(one, new double[0], new Random(1))).ParamName);
            Check.Equal("second", Check.Throws<ArgumentException>(() => PermutationTest.Paired(one, new[] { 1.0, 2 }, new Random(1))).ParamName);
            Check.Equal("first", Check.Throws<ArgumentException>(() => PermutationTest.Paired(new double[0], new double[0], new Random(1))).ParamName);
            Check.Equal("y", Check.Throws<ArgumentException>(() => RobustRegression.TheilSen(new[] { 1.0, 2 }, one)).ParamName);
        }
    }
}
