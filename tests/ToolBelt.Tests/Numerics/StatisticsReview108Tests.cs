using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 108 (statistics, sixth pass): each test failed before its fix.
    public sealed class StatisticsReview108Tests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        // Data on 1e6 with a spread of 1e-5 (the round-66 HypothesisTests data): the mean rounds to an ulp of 1e6.
        private static double[] OnOffset() => Enumerable.Range(0, 12).Select(i => 1e6 + (i * 7 % 11) * Math.Pow(2, -20)).ToArray();

        // Exact (mpmath / Fraction) sample sd of OnOffset().
        private const double OffsetSd = 3.3150758130731918e-06;

        // ---------------- χ² and F densities and tails ----------------

        // (x/2)^(k/2)·e^(−x/2)/Γ(k/2) was formed before the division by x and underflowed for k > 2.
        public void ChiSquarePdfForSmallNormalXDoesNotUnderflow()
        {
            Rel(3.9894228040143268e-151, Distributions.ChiSquarePdf(1e-300, 3), 1e-12, "pdf(1e-300, 3)");
            Rel(4.6386480428950042e-76, Distributions.ChiSquarePdf(1e-300, 2.5), 1e-12, "pdf(1e-300, 2.5)");
            Rel(2.5e-201, Distributions.ChiSquarePdf(1e-200, 4), 1e-12, "pdf(1e-200, 4)");
            Rel(2.5e-161, Distributions.ChiSquarePdf(1e-160, 4), 1e-12, "pdf(1e-160, 4)");
            Rel(4.9611304392509017e-16, Distributions.ChiSquarePdf(1e-300, 2.1), 1e-12, "pdf(1e-300, 2.1)");
        }

        // y = d1x/(d1x + d2) underflowed under a huge d2, and the tails and density came back as 0 (or lost digits). The
        // limit is χ²(d1)/d1.
        public void FTailsWhereTheBetaArgumentUnderflows()
        {
            Rel(Distributions.ChiSquareCdf(0.5 * 4.94e-24, 0.5), Distributions.FCdf(4.94e-24, 0.5, 1e300), 1e-10, "FCdf(4.94e-24, 0.5, 1e300)");
            Rel(Distributions.ChiSquareCdf(1e-20, 1), Distributions.FCdf(1e-20, 1, 1e300), 1e-10, "FCdf(1e-20, 1, 1e300)");
            Check.True(Distributions.FCdf(10.74, 2.3e-308, 1e17) > 0.999, "FCdf(10.74, 2.3e-308, 1e17)");
            Rel(1 - Distributions.ChiSquareCdf(0.5 * 4.94e-24, 0.5), Distributions.FSurvival(4.94e-24, 0.5, 1e300), 1e-12, "FSurvival");
            Rel(1 - Distributions.ChiSquareCdf(0.5 * 4.94e-24, 0.5), Anova.FUpperTailProbability(4.94e-24, 0.5, 1e300), 1e-12, "Anova copy");
            Rel(Distributions.ChiSquareQuantile(1e-17, 0.5) / 0.5, Distributions.FQuantile(1e-17, 0.5, 1e300), 1e-8, "FQuantile");
            Rel(0.5 * Distributions.ChiSquarePdf(0.5 * 4.94e-24, 0.5), Distributions.FPdf(4.94e-24, 0.5, 1e300), 1e-10, "FPdf");
            Rel(Distributions.ChiSquarePdf(1e-20, 1), Distributions.FPdf(1e-20, 1, 1e300), 1e-10, "FPdf(1e-20, 1, 1e300)");
        }

        // ---------------- Corrected two-pass standard deviations ----------------

        public void ConfidenceIntervalSdUsesTheCorrectedSum()
            => Rel(2.348383630721832e-06, ConfidenceInterval.ForStandardDeviation(OnOffset()).Lower, 1e-13, "lower bound");

        public void BlandAltmanSdUsesTheCorrectedSum()
            => Rel(OffsetSd, MeasurementAgreement.BlandAltman(OnOffset(), new double[12]).StdDevOfDifferences, 1e-13, "sd of differences");

        public void ProcessCapabilitySigmaUsesTheCorrectedSum()
            => Rel(OffsetSd, ProcessCapability.Compute(OnOffset(), 0, 2e6).Sigma, 1e-13, "sigma");

        public void FitNormalSdIsAccurateOnAnOffset()
            => Rel(3.173943447703657e-06, DistributionFit.FitNormal(OnOffset()).Parameters["sd"], 1e-13, "MLE sd");

        // ---------------- Rounding floors follow each group's own spread ----------------

        public void AnovaKeepsWithinVariationOfASmallGroupNextToALargeOne()
        {
            var r = Anova.OneWay(new[] { 0, 1e-7, 2e-7 }, new[] { 1e7, 1e7, 1e7 });
            Rel(2e-14, r.SumSquaresWithin, 1e-12, "SSW");
            Check.True(!double.IsInfinity(r.FStatistic) && r.FStatistic > 1e28, $"F {r.FStatistic:R}");
            var r2 = Anova.OneWay(new[] { 1, 1 + 1e-12, 1 + 2e-12 }, new[] { 1e3, 1e3, 1e3 });
            Rel(1.999911522315556e-24, r2.SumSquaresWithin, 1e-9, "SSW near 1");
        }

        public void GaugeRRKeepsRepeatabilityOfAPartNextToLargerParts()
        {
            var m = new double[3, 2, 2];
            m[0, 0, 0] = 1; m[0, 0, 1] = 1 + 1e-12; m[0, 1, 0] = 1 + 2e-12; m[0, 1, 1] = 1;
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 2; k++) { m[1, j, k] = 1000; m[2, j, k] = 1001; }
            var r = GaugeRR.Analyze(m);
            // Exact (Fraction) sums: repeatability 2.5000004186322528e-24, interaction 1.6662228828036045e-25 (pooled into
            // repeatability, since the interaction is not significant), operator 8.331114414018023e-26.
            Check.True(r.InteractionRemoved, "interaction pooled");
            var rep = r.Anova.Single(a => a.Source == "Repeatability");
            Rel(2.6666227069126132e-24, rep.SumOfSquares, 1e-9, "pooled repeatability SS");
            Check.True(r.Repeatability.Variance > 0, "repeatability variance");
            var op = r.Anova.Single(a => a.Source == "Operator");
            Rel(8.331114414018023e-26, op.SumOfSquares, 1e-6, "operator SS");
        }

        // ---------------- Permutation test ----------------

        private static double Var(IReadOnlyList<double> v)
        {
            double mean = v.Average();
            return v.Sum(t => (t - mean) * (t - mean)) / (v.Count - 1);
        }

        // One near-tied split (a variance ratio near 1e20) set the tie tolerance for every rearrangement.
        public void OneHugeRearrangementDoesNotMakeEverythingATie()
        {
            double[] a = { 0, 10, 20, 30, 5 + 1e-9 }, b = { 5, 15 };
            Func<IReadOnlyList<double>, IReadOnlyList<double>, double> ratio = (x, y) => Var(x) / Var(y);
            var g = PermutationTest.TwoSample(a, b, new DeterministicRandom(1), 100000, ratio, Alternative.Greater);
            Check.True(g.IsExact, "exact enumeration");
            Check.Close(9.0 / 21, g.PValue, 1e-12, "greater");
            var l = PermutationTest.TwoSample(a, b, new DeterministicRandom(1), 100000, ratio, Alternative.Less);
            Check.Close(13.0 / 21, l.PValue, 1e-12, "less");
        }

        public void UndefinedAlternativeIsRefused()
        {
            double[] a = { 1, 2, 3 }, b = { 4, 5, 6 };
            Check.Equal("alternative", Check.Throws<ArgumentOutOfRangeException>(() =>
                PermutationTest.TwoSample(a, b, new DeterministicRandom(1), 100, null, (Alternative)7)).ParamName);
            Check.Equal("alternative", Check.Throws<ArgumentOutOfRangeException>(() =>
                PermutationTest.Paired(a, b, new DeterministicRandom(1), 100, (Alternative)7)).ParamName);
        }

        public void UndefinedSigmaEstimatorIsRefused()
            => Check.Equal("estimator", Check.Throws<ArgumentOutOfRangeException>(() =>
                ProcessCapability.Compute(new double[] { 1, 2, 3, 4 }, 0, 5, (SigmaEstimator)9)).ParamName);

        // ---------------- Theil–Sen ----------------

        // A pairwise slope past double.MaxValue gave an infinite slope and a NaN intercept, or a NaN line.
        public void TheilSenRefusesASlopeThatOverflows()
        {
            Check.Equal("x", Check.Throws<ArgumentException>(() =>
                RobustRegression.TheilSen(new[] { 0, 1e-300 }, new[] { 0, 1e10 })).ParamName);
            Check.Equal("x", Check.Throws<ArgumentException>(() =>
                RobustRegression.TheilSen(new[] { 0, 1e-300, 0 }, new[] { -1.7e308, 0, 1.7e308 })).ParamName);
        }
    }
}
