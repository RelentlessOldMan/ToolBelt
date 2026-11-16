using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 87 (statistics, fifth pass): each test failed before its fix.
    public sealed class StatisticsReview87Tests
    {
        private const double Eps = double.Epsilon;

        // The largest confidence below 1: (1 + c)/2 rounds to 1.
        private const double AlmostOne = 0.99999999999999989;

        // ---------------- Quantiles in far lower tails at many degrees of freedom ----------------

        // Newton approached the root from above by ~sd/|z| a step and ran out of iterations: χ²(1e9) at 1e-300 came back
        // with a CDF of 2e-172.
        public void FarLowerTailQuantilesConvergeAtManyDegreesOfFreedom()
        {
            foreach (double p in new[] { 1e-300, 1e-200, 1e-150 })
            {
                foreach (double k in new[] { 1e3, 1e4, 1e6, 1e7, 1e9, 1e12, 1e15 })
                {
                    double x = Distributions.ChiSquareQuantile(p, k);
                    double back = Distributions.ChiSquareCdf(x, k);
                    Check.True(Math.Abs(Math.Log(back / p)) < 1e-3, $"χ²({k:R}) at {p:R}: x {x:R}, CDF {back:R}");
                }
                foreach (var (d1, d2) in new[] { (1e5, 1e5), (1e9, 1e13), (1e6, 1e3), (30.0, 1e8) })
                {
                    double x = Distributions.FQuantile(p, d1, d2);
                    double back = Distributions.FCdf(x, d1, d2);
                    Check.True(Math.Abs(Math.Log(back / p)) < 1e-3, $"F({d1:R}, {d2:R}) at {p:R}: x {x:R}, CDF {back:R}");
                }
            }
        }

        // ---------------- Confidences next to 1 ----------------

        // (1 + c)/2 rounded to 1 and the quantile threw ArgumentOutOfRangeException for "p".
        public void AConfidenceJustBelowOneGivesAFiniteInterval()
        {
            var sample = new[] { 1.0, 4, 2, 8, 5, 7 };
            foreach (var (lo, hi) in new[]
            {
                ConfidenceInterval.ForMean(sample, AlmostOne), ConfidenceInterval.ForMeanT(sample, AlmostOne),
                ConfidenceInterval.ForStandardDeviation(sample, AlmostOne), ConfidenceInterval.ForVariance(sample, AlmostOne),
                ConfidenceInterval.ForProportion(3, 10, AlmostOne),
            })
                Check.True(lo < hi && !double.IsInfinity(lo) && !double.IsInfinity(hi), $"({lo:R}, {hi:R})");
            // The tails against those of a confidence one ulp lower: a little wider, never narrower.
            double lower = 0.99999999999999978;
            Check.True(ConfidenceInterval.ForMean(sample, AlmostOne).Upper > ConfidenceInterval.ForMean(sample, lower).Upper);
            Check.True(ConfidenceInterval.ForStandardDeviation(sample, AlmostOne).Upper > ConfidenceInterval.ForStandardDeviation(sample, lower).Upper);
            var x = new double[,] { { 1 }, { 2 }, { 3 }, { 4 }, { 5 }, { 6 } };
            var fit = MultipleRegression.Fit(x, new[] { 1.1, 1.9, 3.2, 3.9, 5.1, 6.0 });
            foreach (var (lo, hi) in new[] { fit.PredictionInterval(new[] { 3.5 }, AlmostOne), fit.ConfidenceInterval(new[] { 3.5 }, AlmostOne) })
                Check.True(lo < hi && !double.IsInfinity(lo) && !double.IsInfinity(hi), $"({lo:R}, {hi:R})");
        }

        // The new upper-tail quantile against the survival function, including tails that 1 − q can't hold.
        public void ChiSquareInverseSurvivalRoundTrips()
        {
            var rng = new Random(8701);
            for (int trial = 0; trial < 2000; trial++)
            {
                double k = Math.Pow(10, rng.NextDouble() * 8 - 2);
                double q = trial % 3 == 0 ? rng.NextDouble() : Math.Pow(10, -rng.NextDouble() * 300);
                if (!(q > 0 && q < 1)) continue;
                double x = Distributions.ChiSquareInverseSurvival(q, k);
                double back = Distributions.ChiSquareSurvival(x, k);
                Check.True(Math.Abs(back - q) <= 1e-9 * q || Math.Abs(Math.Log(back / q)) < 1e-6, $"k {k:R}, q {q:R}: x {x:R}, Q {back:R}");
                if (q < 0.5 && q > 1e-4) Check.Close(Distributions.ChiSquareQuantile(1 - q, k), x, 1e-9 * x, $"k {k:R}, q {q:R}");
            }
            Check.Equal("q", Check.Throws<ArgumentOutOfRangeException>(() => Distributions.ChiSquareInverseSurvival(1, 3)).ParamName);
            Check.Equal("q", Check.Throws<ArgumentOutOfRangeException>(() => Distributions.ChiSquareInverseSurvival(double.NaN, 3)).ParamName);
        }

        // ---------------- One-way ANOVA over an aliased group ----------------

        // The observation count was an int: one 2^20-element array passed 2100 times wrapped it negative.
        public void AnovaCountsPastIntMaxValue()
        {
            var g = Enumerable.Range(0, 1 << 20).Select(i => (double)(i % 7)).ToArray();
            var groups = Enumerable.Repeat(g, 2100).ToArray();
            var r = Anova.OneWay(groups);
            Check.Equal(2100.0 * (1 << 20) - 2100, r.DfWithin);
            Check.True(r.FStatistic < 1e-6, r.FStatistic.ToString("R"));
            Check.True(r.PValue > 0.99, r.PValue.ToString("R"));
        }

        // ---------------- Weibull fits of very tight data ----------------

        // The shape bracket stopped at 1e12 although the equal-values guard admits shapes near 1e14. For half the sample
        // at 1 and half at 1 + d the shape equation is u·tanh(u) = 1 with u = k·ln(1 + d)/2.
        public void WeibullShapeOfVeryTightData()
        {
            const double U = 1.1996786402577338;
            foreach (double d in new[] { 1e-6, 1e-9, 1e-11, 1e-12, 1e-13 })
            {
                var x = Enumerable.Range(0, 10).Select(i => i % 2 == 0 ? 1 : 1 + d).ToArray();
                double delta = Math.Log(1 + d);
                var fit = DistributionFit.FitWeibull(x);
                Check.Close(2 * U / delta, fit.Parameters["shape"], 1e-6 * (2 * U / delta), "d = " + d);
            }
        }

        // ---------------- Chi-square tests at the ends of the double range ----------------

        // Overflowing totals compared ∞ with ∞ (NaN or false) and mismatched counts were accepted.
        public void GoodnessOfFitChecksOverflowingTotals()
        {
            Check.Equal("expected", Check.Throws<ArgumentException>(() =>
                HypothesisTests.ChiSquareGoodnessOfFit(new[] { 1e308, 1e308 }, new[] { 1.0, 1 })).ParamName);
            Check.Equal("expected", Check.Throws<ArgumentException>(() =>
                HypothesisTests.ChiSquareGoodnessOfFit(new[] { 1e308, 1e308 }, new[] { 1e308, 1.7e308 })).ParamName);
            var same = HypothesisTests.ChiSquareGoodnessOfFit(new[] { 1e308, 1.5e308 }, new[] { 1.5e308, 1e308 });
            Check.Close(0.5e308 * (0.5e308 / 1.5e308) + 0.5e308 * (0.5e308 / 1e308), same.Statistic, 1e-12 * same.Statistic);
        }

        // Rescaled for an overflowing total, a subnormal row became 0 and was refused; and an expected count that
        // underflowed gave ∞ for a χ² of 1.
        public void IndependenceWithSubnormalMarginals()
        {
            var r = HypothesisTests.ChiSquareIndependence(new[,] { { 1e308, 1e308 }, { Eps, Eps } });
            Check.True(r.Statistic >= 0 && r.Statistic < 1e-300, r.Statistic.ToString("R"));
            var t = HypothesisTests.ChiSquareIndependence(new[,] { { Eps, 0 }, { 0, 1 } });
            Check.Close(1, t.Statistic, 1e-12);
        }

        // ---------------- Gamma fits ----------------

        // ln k − ψ(k) stopped its asymptotic series at the 1/(132k¹⁰) term, 1e-10 relative at k = 6: the fitted shape was
        // 1e-11 to 1e-10 off. References: the mpmath root of ln k − ψ(k) = ln(mean) − mean(ln x).
        public void GammaShapeMatchesTheDigammaRoot()
        {
            foreach (var (sample, shape) in new[]
            {
                (new[] { 0.3, 1.7, 0.9, 2.4, 0.1, 1.1, 0.6, 3.2, 0.8, 1.5 }, 1.5394104490738932615),
                (new[] { 1.2, 4.7, 2.2, 7.9, 3.8, 1.1, 6.3, 2.9, 3.5, 9.2 }, 2.4830270497742800412),
                (new[] { 3.1, 4.7, 2.2, 5.9, 3.8, 4.1, 6.3, 2.9, 3.5, 5.2, 1.9, 6.6 }, 7.2269773967882918271),
            })
                Check.Close(shape, DistributionFit.FitGamma(sample).Parameters["shape"], 1e-13 * shape);
        }

        // ---------------- Change points ----------------

        // The sums were of the raw values: a 2-ulp shift on 1e8 was lost (index 1, magnitude 0) and constant data gave noise.
        public void CusumAndShiftWorkInOffsetsFromTheFirstValue()
        {
            double up = 1e8 + 2 * 1.4901161193847656e-8;
            var series = Enumerable.Repeat(1e8, 37).Concat(Enumerable.Repeat(up, 23)).ToArray();
            var shift = ChangePoint.DetectShift(series);
            Check.Equal(37, shift.Index);
            Check.Equal(up - 1e8, shift.Magnitude);
            var cusum = ChangePoint.Cusum(series);
            Check.True(Math.Abs(cusum[cusum.Length - 1]) < 1e-15, cusum[cusum.Length - 1].ToString("R"));
            Check.Close(-37 * 23 / 60.0 * (up - 1e8), cusum[36], 1e-12);
            foreach (int n in new[] { 3, 6, 1000 })
            {
                var flat = Enumerable.Repeat(0.1, n).ToArray();
                Check.True(ChangePoint.Cusum(flat).All(c => c == 0), "Cusum of " + n);
                Check.Equal(0.0, ChangePoint.DetectShift(flat).Magnitude, "shift of " + n);
            }
        }

        // ---------------- Subnormal arguments ----------------

        // 3009·2^-1074 (built from its bits: .NET Framework parses "1.4866e-320" as 3008·2^-1074). Halving it rounds.
        private static readonly double Sub = BitConverter.Int64BitsToDouble(3009);

        // x/2 and d1·x rounded in the subnormal range, 8e-5 relative in the tails and densities.
        public void ChiSquareAndFAtSubnormalArguments()
        {
            Check.Close(1.024408394249132938e-80, Distributions.ChiSquareCdf(Sub, 0.5), 1e-13 * 1.02e-80);
            Check.Close(1.7226866675220033818e+239, Distributions.ChiSquarePdf(Sub, 0.5), 1e-13 * 1.72e239);
            Check.Close(4.8209105533110815657e-481 / 1e-481, Distributions.ChiSquareCdf(Sub, 3) / 1e-481, 1e-13 * 4.82);
            Check.Close(4.8642231255391651143e-161, Distributions.ChiSquarePdf(Sub, 3), 1e-13 * 4.86e-161);
            Check.Close(0.5, Distributions.ChiSquarePdf(Sub, 2), 1e-15);
            Check.Close(1 - 1.024408394249132938e-80, Distributions.ChiSquareSurvival(Sub, 0.5), 1e-16);
            Check.Close(8.2070905940979429425e-81, Distributions.FCdf(Sub, 0.5, 4), 1e-13 * 8.2e-81);
            Check.Close(1.3801376116173631103e+239, Distributions.FPdf(Sub, 0.5, 4), 1e-13 * 1.38e239);
            Check.Close(3.1677821604823299853e-10, Distributions.FPdf(Sub, 3, 1e-300), 1e-13 * 3.17e-10);
            Check.Close(3.2719498876658566812e+159, Distributions.FPdf(Sub, 1, 1e300), 1e-12 * 3.27e159);   // mpmath at 400 digits
        }

        // ---------------- Process capability near double.MaxValue ----------------

        // 6σ overflowed (∞/∞: Cp NaN) and so did 3σ (Cpk 0).
        public void CapabilityOfAProcessNearMaxValue()
        {
            var r = ProcessCapability.Compute(new[] { -1e308, 1e308 }, -1.7e308, 1.7e308);
            double expected = 3.4 / (6 * Math.Sqrt(2));
            Check.Close(expected, r.Cp, 1e-12);
            Check.Close(expected, r.Cpk, 1e-12);
        }

        // ---------------- Uniform ----------------

        // NaN passed both range comparisons and the density was 1/(b − a); the documented quantile was missing.
        public void UniformDensityOfNaNAndTheQuantile()
        {
            Check.True(double.IsNaN(Distributions.UniformPdf(double.NaN, 0, 1)));
            var rng = new Random(8702);
            for (int trial = 0; trial < 1000; trial++)
            {
                double a = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-5, 300)), b = a + rng.NextDouble() * Math.Pow(10, rng.Next(-5, 300));
                if (!(b > a) || double.IsInfinity(b)) continue;
                double p = rng.NextDouble(), x = Distributions.UniformQuantile(p, a, b);
                Check.True(x >= a && x <= b, $"{p:R} in [{a:R}, {b:R}]: {x:R}");
                // x − a carries an ulp of |x|: conditioning, not error.
                double tol = 1e-15 + 4e-16 * Math.Max(Math.Abs(a), Math.Abs(b)) / (b - a);
                Check.Close(p, Distributions.UniformCdf(x, a, b), tol, $"{p:R} in [{a:R}, {b:R}]");
            }
            Check.Equal(-3.0, Distributions.UniformQuantile(0, -3, 5));
            Check.Equal(5.0, Distributions.UniformQuantile(1, -3, 5));
            Check.Equal(0.0, Distributions.UniformQuantile(0.5, -1.6e308, 1.6e308));
            Check.Equal("p", Check.Throws<ArgumentOutOfRangeException>(() => Distributions.UniformQuantile(double.NaN, 0, 1)).ParamName);
            Check.Equal("p", Check.Throws<ArgumentOutOfRangeException>(() => Distributions.UniformQuantile(1.5, 0, 1)).ParamName);
            Check.Equal("b", Check.Throws<ArgumentException>(() => Distributions.UniformQuantile(0.5, 1, 1)).ParamName);
        }

        // ---------------- Quantiles below the smallest double ----------------

        // The solver stopped a few ulps up in the subnormals, where the CDF was 1e150 times p; documented: underflows to 0.
        public void QuantilesBelowTheSmallestDoubleAreZero()
        {
            Check.Equal(0.0, Distributions.ChiSquareQuantile(8.6e-175, 0.106));
            Check.Equal(0.0, Distributions.FQuantile(1e-300, 0.05, 3));
            double x = Distributions.ChiSquareQuantile(1e-300, 3);
            Check.True(x > 0 && Math.Abs(Math.Log(Distributions.ChiSquareCdf(x, 3) / 1e-300)) < 1e-6, x.ToString("R"));
        }
    }
}
