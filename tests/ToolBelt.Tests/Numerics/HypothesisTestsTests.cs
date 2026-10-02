using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class HypothesisTestsTests
    {
        public void OneSampleT_NullCaseAndKnownStatistic()
        {
            // Mean equals the hypothesized mean -> t = 0, p = 1.
            var flat = HypothesisTests.OneSampleT(new double[] { 1, 2, 3, 4, 5 }, 3);
            Check.Close(0.0, flat.Statistic, 1e-12);
            Check.Close(1.0, flat.PValue, 1e-12);
            Check.Equal(4.0, flat.DegreesOfFreedom);

            // {2,4,6,8} vs 0: mean 5, s^2 = 20/3, se = sqrt((20/3)/4), t = 5/se = 3.872983...
            var r = HypothesisTests.OneSampleT(new double[] { 2, 4, 6, 8 }, 0);
            Check.Close(3.8729833462074166, r.Statistic, 1e-9);
            Check.Equal(3.0, r.DegreesOfFreedom);
            Check.True(r.PValue > 0 && r.PValue < 0.05, $"p={r.PValue}");
        }

        public void PairedT_KnownStatistic()
        {
            // Differences {1,2,3}: mean 2, s = 1, t = 2 / (1/sqrt(3)) = 3.4641016.
            var r = HypothesisTests.PairedT(new double[] { 2, 4, 6 }, new double[] { 1, 2, 3 });
            Check.Close(3.4641016151377544, r.Statistic, 1e-9);
            Check.Equal(2.0, r.DegreesOfFreedom);
        }

        public void TwoSampleT_PooledAndWelch_KnownStatistic()
        {
            var a = new double[] { 1, 2, 3, 4, 5 }; // mean 3, var 2.5
            var b = new double[] { 6, 7, 8, 9, 10 }; // mean 8, var 2.5
            var pooled = HypothesisTests.TwoSampleT(a, b, equalVariance: true);
            Check.Close(-5.0, pooled.Statistic, 1e-9);
            Check.Equal(8.0, pooled.DegreesOfFreedom);

            var welch = HypothesisTests.TwoSampleT(a, b); // default Welch
            Check.Close(-5.0, welch.Statistic, 1e-9);
            Check.Close(8.0, welch.DegreesOfFreedom, 1e-9); // equal n & variance -> df = 8
            Check.True(welch.PValue < 0.01, $"p={welch.PValue}");
        }

        public void ChiSquareGoodnessOfFit_KnownValues()
        {
            var perfect = HypothesisTests.ChiSquareGoodnessOfFit(
                new double[] { 10, 10, 10, 10 }, new double[] { 10, 10, 10, 10 });
            Check.Close(0.0, perfect.Statistic, 1e-12);
            Check.Close(1.0, perfect.PValue, 1e-12);

            // obs {20,30} vs exp {25,25}: chi2 = 1 + 1 = 2, df = 1, p = erfc(1) = 0.1572992.
            var r = HypothesisTests.ChiSquareGoodnessOfFit(new double[] { 20, 30 }, new double[] { 25, 25 });
            Check.Close(2.0, r.Statistic, 1e-12);
            Check.Equal(1.0, r.DegreesOfFreedom);
            Check.Close(0.15729920705028513, r.PValue, 1e-9);
        }

        public void ChiSquareIndependence_KnownStatistic()
        {
            // 2x2 with equal margins: expected 15 everywhere, chi2 = 4 * 25/15 = 100/15.
            var r = HypothesisTests.ChiSquareIndependence(new double[,] { { 10, 20 }, { 20, 10 } });
            Check.Close(100.0 / 15.0, r.Statistic, 1e-12);
            Check.Equal(1.0, r.DegreesOfFreedom);
            Check.True(r.PValue > 0.009 && r.PValue < 0.011, $"p={r.PValue}"); // ~0.00982
        }

        public void KolmogorovSmirnovOneSample_KnownDistance()
        {
            Func<double, double> uniform = x => x < 0 ? 0 : (x > 1 ? 1 : x);
            // Single point 0.5 vs U(0,1): D+ = 1 - 0.5, D- = 0.5 - 0 -> D = 0.5.
            Check.Close(0.5, HypothesisTests.KolmogorovSmirnovOneSample(new double[] { 0.5 }, uniform).Statistic, 1e-12);
            // {.25,.5,.75} vs U(0,1): D = 0.25.
            Check.Close(0.25, HypothesisTests.KolmogorovSmirnovOneSample(new double[] { 0.25, 0.5, 0.75 }, uniform).Statistic, 1e-12);
        }

        public void KolmogorovSmirnovTwoSample_Extremes()
        {
            var same = HypothesisTests.KolmogorovSmirnovTwoSample(new double[] { 1, 2, 3 }, new double[] { 1, 2, 3 });
            Check.Close(0.0, same.Statistic, 1e-12);
            Check.Close(1.0, same.PValue, 1e-12);

            var disjoint = HypothesisTests.KolmogorovSmirnovTwoSample(new double[] { 1, 2, 3 }, new double[] { 4, 5, 6 });
            Check.Close(1.0, disjoint.Statistic, 1e-12);
        }

        public void MannWhitneyU_KnownStatistic()
        {
            // a={1,3,5}, b={2,4,6}: ranks of a = 1+3+5 = 9, U1 = 9 - 6 = 3, U2 = 6, U = 3.
            var r = HypothesisTests.MannWhitneyU(new double[] { 1, 3, 5 }, new double[] { 2, 4, 6 });
            Check.Close(3.0, r.Statistic, 1e-12);

            // Complete separation: a all below b -> U = 0.
            var sep = HypothesisTests.MannWhitneyU(new double[] { 1, 2, 3 }, new double[] { 4, 5, 6 });
            Check.Close(0.0, sep.Statistic, 1e-12);
            Check.True(sep.PValue > 0 && sep.PValue <= 1, $"p={sep.PValue}");
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentException>(() => HypothesisTests.OneSampleT(new double[] { 1 }, 0));
            Check.Throws<ArgumentException>(() => HypothesisTests.PairedT(new double[] { 1, 2 }, new double[] { 1, 2, 3 }));
            Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareGoodnessOfFit(new double[] { 1, 2 }, new double[] { 1 }));
            Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareGoodnessOfFit(new double[] { 1, 2 }, new double[] { 1, 0 }));
            Check.Throws<ArgumentException>(() => HypothesisTests.ChiSquareIndependence(new double[,] { { 1, 2, 3 } }));
            Check.Throws<ArgumentNullException>(() => HypothesisTests.OneSampleT(null!, 0));
        }
    }
}
