using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 66 (statistics, fourth pass): each test failed before its fix. Scaled data use powers
    // of two, so the scaled sample is exactly the unscaled one times the factor.
    public sealed class StatisticsReview4Tests
    {
        private static void Near(double expected, double actual, double relative) =>
            Check.Close(expected, actual, relative * Math.Abs(expected));

        // ---------------- Chi-square with counts at the ends of the double range ----------------

        private static double[,] Scaled(double[,] t, double k)
        {
            var s = new double[t.GetLength(0), t.GetLength(1)];
            for (int i = 0; i < t.GetLength(0); i++)
                for (int j = 0; j < t.GetLength(1); j++) s[i, j] = t[i, j] * k;
            return s;
        }

        public void ChiSquareIndependence_HugeAndTinyCounts_ScaleWithTheTable()
        {
            var t = new double[,] { { 1, 1.25, 0.5 }, { 1.5, 1, 0.75 } };
            double chi2 = HypothesisTests.ChiSquareIndependence(t).Statistic;
            // 2^600: rowSum·colSum overflowed; 2^-600: d·d underflowed; 2^1021: the total itself overflows.
            foreach (int e in new[] { 600, -600, 1021 })
            {
                double k = Math.Pow(2, e);
                var r = HypothesisTests.ChiSquareIndependence(Scaled(t, k));
                Near(chi2 * k, r.Statistic, 1e-13);
            }
            var equal = HypothesisTests.ChiSquareIndependence(new double[,] { { 1e200, 1e200 }, { 1e200, 1e200 } });
            Check.Equal(0.0, equal.Statistic);
            Check.Equal(1.0, equal.PValue);
        }

        public void ChiSquareGoodnessOfFit_DifferencesNearMaxValue_StayFinite()
        {
            double k = Math.Pow(2, 1020);
            var r = HypothesisTests.ChiSquareGoodnessOfFit(new[] { 4 * k, 2 * k }, new[] { 2 * k, 4 * k });
            Near(3 * k, r.Statistic, 1e-15);      // (2²/2 + 2²/4)·k
        }

        // ---------------- F density at a huge numerator df ----------------

        // As d1 → ∞, F(d1, d2) → d2/χ²(d2), whose density is χ²pdf(d2/x; d2)·d2/x², exact to O(1/d1).
        public void FPdf_HugeNumeratorDf_MatchesTheInverseChiSquareLimit()
        {
            var rng = new Random(66);
            for (int trial = 0; trial < 400; trial++)
            {
                double d1 = Math.Pow(10, 17 + 280 * rng.NextDouble());
                double d2 = Math.Pow(10, -0.3 + 2.3 * rng.NextDouble());
                double x = Math.Pow(10, -1.5 + 3 * rng.NextDouble());
                double reference = Distributions.ChiSquarePdf(d2 / x, d2) * d2 / (x * x);
                if (reference < 1e-250) continue;
                Near(reference, Distributions.FPdf(x, d1, d2), 1e-10);
            }
        }

        public void FQuantile_HugeNumeratorDf_InvertsTheSurvival()
        {
            foreach (double d1 in new[] { 1e17, 1e20, 1e100 })
                foreach (double tail in new[] { 1e-10, 1e-5, 0.3 })
                {
                    double q = Distributions.FQuantile(1 - tail, d1, 3);
                    Near(tail, Distributions.FSurvival(q, d1, 3), 1e-6);
                }
        }

        // ---------------- Gamma fits at the ends of the double range ----------------

        public void FitGamma_DataNearMaxValue_MatchesTheScaledFit()
        {
            var x = new[] { 1.0, 1.7, 1.2, 1.5, 1.1 };
            double k = Math.Pow(2, 1023);
            var reference = DistributionFit.FitGamma(x);
            var r = DistributionFit.FitGamma(x.Select(v => v * k).ToArray());
            Near(reference.Parameters["shape"], r.Parameters["shape"], 1e-12);
            Near(reference.Parameters["scale"] * k, r.Parameters["scale"], 1e-12);
            Near(reference.KolmogorovSmirnovStatistic, r.KolmogorovSmirnovStatistic, 1e-12);
        }

        public void FitGamma_ScaleOutsideTheDoubleRange_IsRefusedForTheSample()
        {
            // Overflowing scale: a tiny shape over a mean near 1e308 (the CDF threw naming "cdf").
            var e = Check.Throws<ArgumentException>(() => DistributionFit.FitGamma(new[] { double.Epsilon, 1e308 }));
            Check.Equal("sample", e.ParamName);
            // Underflowing scale: tight subnormal data (the scale was 0, log-likelihood +∞, AIC −∞).
            var tight = new[] { 1000 * double.Epsilon, 1001 * double.Epsilon, 1002 * double.Epsilon, 999 * double.Epsilon };
            e = Check.Throws<ArgumentException>(() => DistributionFit.FitGamma(tight));
            Check.Equal("sample", e.ParamName);
            foreach (var fit in DistributionFit.FitAll(tight))
                Check.True(!double.IsInfinity(fit.Aic) && !double.IsNaN(fit.Aic), fit.ToString());
        }

        // ---------------- Smaller ones ----------------

        public void GaugeRR_LongFormat_PartTimesOperatorCountPastIntMax_IsUnbalanced()
        {
            // 65,536 parts × 65,538 operators = 2³² + 131,072 cells needed; only 131,072 present. The int product wrapped
            // to exactly that, so the design passed the check and the cube allocation failed.
            const int Parts = 65536;
            var records = new List<(int, int, double)>(4 * Parts);
            for (int p = 0; p < Parts; p++)
                for (int t = 0; t < 2; t++)
                {
                    records.Add((p, p, t));
                    records.Add((p, p + 2, t));
                }
            var e = Check.Throws<ArgumentException>(() => GaugeRR.Analyze(records));
            Check.Equal("measurements", e.ParamName);
        }

        public void FUpperTailProbability_SubnormalDf_IsRefused()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => Anova.FUpperTailProbability(1, double.Epsilon, 3));
            Check.Equal("df1", e.ParamName);
            e = Check.Throws<ArgumentOutOfRangeException>(() => Anova.FUpperTailProbability(1, 3, 1e-310));
            Check.Equal("df2", e.ParamName);
        }

        private sealed class ThrowingRandom : Random
        {
            public override int Next(int maxValue) => throw new InvalidOperationException("the test's Random was used");
        }

        public void PermutationTest_PermutationsPastArrayMaxLength_AreRefusedUpFront()
        {
            var a = Enumerable.Range(0, 20).Select(i => (double)i).ToArray();
            var b = Enumerable.Range(5, 20).Select(i => (double)i).ToArray();
            var e = Check.Throws<ArgumentOutOfRangeException>(() => PermutationTest.TwoSample(a, b, new ThrowingRandom(), int.MaxValue));
            Check.Equal("permutations", e.ParamName);
            var d = Enumerable.Range(0, 40).Select(i => (double)i).ToArray();
            e = Check.Throws<ArgumentOutOfRangeException>(() => PermutationTest.Paired(d, Enumerable.Reverse(d).ToArray(), new ThrowingRandom(), int.MaxValue));
            Check.Equal("permutations", e.ParamName);
            // Array.MaxLength itself is still accepted (checked without running it: a small exact case returns at once).
            var exact = PermutationTest.TwoSample(new[] { 1.0, 2 }, new[] { 3.0 }, new ThrowingRandom(), 0x7FFFFFC7);
            Check.True(exact.IsExact);
            Check.Equal(3, exact.Permutations);
        }

        // ---------------- Fourth-pass correctness: offsets, tight data, far tails ----------------

        // Data on a large offset, exactly representable both on it and shifted to 0 (multiples of the offset's ulp).
        public void AnovaOneWay_DataOnAnOffset_MatchesTheShiftedData()
        {
            double ulp = Math.Pow(2, -23);                       // of 1e9
            foreach (int m in new[] { 1, 4, 16, 64, 1024 })
            {
                double[] g1 = { 0, m * ulp, 2 * m * ulp }, g2 = { 10 * m * ulp, 11 * m * ulp, 12 * m * ulp };
                var shifted = Anova.OneWay(g1, g2);
                var r = Anova.OneWay(g1.Select(v => 1e9 + v).ToArray(), g2.Select(v => 1e9 + v).ToArray());
                Near(150, shifted.FStatistic, 1e-12);
                Near(shifted.FStatistic, r.FStatistic, 1e-9);
                Near(shifted.PValue, r.PValue, 1e-9);
            }
        }

        public void GaugeRR_DataOnAnOffset_MatchesTheShiftedData()
        {
            double ulp = Math.Pow(2, -19);                       // of 1e10
            var rng = new Random(661);
            var steps = new double[10, 3, 3];
            var onOffset = new double[10, 3, 3];
            for (int p = 0; p < 10; p++)
                for (int o = 0; o < 3; o++)
                    for (int t = 0; t < 3; t++)
                    {
                        steps[p, o, t] = (40 * p + 7 * o + rng.Next(-26, 27)) * ulp;
                        onOffset[p, o, t] = 1e10 + steps[p, o, t];
                    }
            var reference = GaugeRR.Analyze(steps, alphaToRemoveInteraction: 0);
            var r = GaugeRR.Analyze(onOffset, alphaToRemoveInteraction: 0);
            Check.True(r.Repeatability.Variance > 0, "repeatability was zeroed");
            Near(reference.Repeatability.Variance, r.Repeatability.Variance, 1e-9);
            Near(reference.PartToPart.Variance, r.PartToPart.Variance, 1e-9);
            Near(reference.Operator.Variance, r.Operator.Variance, 1e-9);
            Near(reference.InteractionP, r.InteractionP, 1e-9);
        }

        // 50 values 100 + c·(i − 24.5)/14.5 (CV ≈ c/100); the exact MLE from mpmath at 60 digits.
        private static double[] Tight(double c) => Enumerable.Range(0, 50).Select(i => 100 + c * (i - 24.5) / 14.5).ToArray();

        public void FitGamma_TightData_MatchesTheExactMle()
        {
            var r = DistributionFit.FitGamma(Tight(1e-3));
            Near(10096038414.6303, r.Parameters["shape"], 1e-9);
            Near(274.67978773385454, r.LogLikelihood, 1e-11);
            r = DistributionFit.FitGamma(Tight(1e-5));           // shape 2.9% off; log-likelihood 476 for 504.9
            Near(100960384146957.66, r.Parameters["shape"], 1e-7);
            Near(9.904875149290267e-13, r.Parameters["scale"], 1e-7);
            Near(504.93829703177056, r.LogLikelihood, 1e-11);
        }

        public void FitLogNormal_TightData_MatchesTheExactMle()
        {
            var r = DistributionFit.FitLogNormal(Tight(1e-3));
            Near(4.605170185938567, r.Parameters["mu"], 1e-15);
            Near(9.952323924595775e-06, r.Parameters["sigma"], 1e-12);
            r = DistributionFit.FitLogNormal(Tight(1e-5));
            Near(4.6051701859880865, r.Parameters["mu"], 1e-15);
            Near(9.95232392423515e-08, r.Parameters["sigma"], 1e-11);
        }

        // ---------------- KS one-sample p-value against SciPy's exact kstwo ----------------

        // (n, a, D, SciPy kstwo.sf(D, n)) for the sample ((i + ½)/n)^a, i < n, against the uniform CDF. Large n only where
        // the p-value is exact (D ≥ ½ or p < 1e-4).
        private static readonly (int N, double A, double D, double P)[] KsExact =
        {
            (1, 0.02, 0.9862327044933592, 0.027534591013281595),
            (1, 0.3, 0.8122523963562356, 0.37549520728752883),
            (1, 0.85, 0.5547847360339225, 0.8904305279321549),
            (1, 1.6, 0.6701230223067765, 0.6597539553864471),
            (1, 5, 0.96875, 0.0625),
            (1, 40, 0.9999999999990905, 1.8189894035458565e-12),
            (2, 0.02, 0.9726549474122855, 0.0014955038020497396),
            (2, 0.3, 0.6597539553864471, 0.23153474175033567),
            (2, 0.85, 0.3077861033362291, 0.9732861300897172),
            (2, 1.6, 0.3911811795879845, 0.8405429962411621),
            (2, 5, 0.7626953125, 0.11262702941894531),
            (2, 40, 0.9999899434148384, 2.0226981022590713e-10),
            (3, 0.02, 0.9647992895453287, 8.723369779713878e-05),
            (3, 0.3, 0.5841906810678655, 0.1676276919815855),
            (3, 0.85, 0.22145140270058922, 0.9921074024609098),
            (3, 1.6, 0.3367896889734431, 0.7685374321073783),
            (3, 5, 0.6354166666666666, 0.10064471209490745),
            (3, 40, 0.9993196221632034, 6.299128529799146e-10),
            (4, 0.02, 0.9592641193252643, 5.507296201207785e-06),
            (4, 0.3, 0.5358867312681466, 0.13487703490224398),
            (4, 0.85, 0.1844367714756261, 0.9952076109321648),
            (4, 1.6, 0.29181471547597626, 0.7957093768506177),
            (4, 5, 0.654632568359375, 0.0329972247547505),
            (4, 40, 0.995210147708972, 1.0527335872530633e-09),
            (5, 0.02, 0.954992586021436, 3.693603696524962e-07),
            (5, 0.3, 0.5011872336272724, 0.11043417392529009),
            (5, 0.85, 0.15937930668721073, 0.997165301742781),
            (5, 1.6, 0.27012302230677643, 0.7772951367776458),
            (5, 5, 0.6319300000000001, 0.018553144925984397),
            (5, 40, 0.9852191170585654, 1.411015821731093e-09),
            (6, 0.02, 0.9515166126215611, 2.5976924105849645e-08),
            (6, 0.3, 0.49308728871978047, 0.07201957829095162),
            (6, 0.85, 0.14180540981894774, 0.9981583649485454),
            (6, 1.6, 0.25358790945744614, 0.7548261668523157),
            (6, 5, 0.59912310313786, 0.013715882319954648),
            (6, 40, 0.9692066056014986, 1.7052002286439406e-09),
            (8, 0.02, 0.9460576467255959, 1.4337357260969173e-10),
            (8, 0.3, 0.48020203770967285, 0.03223155831645763),
            (8, 0.85, 0.12206817017662547, 0.9983636561618257),
            (8, 1.6, 0.2335811853096219, 0.6932425062915203),
            (8, 5, 0.596409797668457, 0.0030230221436543946),
            (8, 40, 0.924342662105164, 2.147031635391225e-09),
            (10, 0.02, 0.9418449208830277, 8.849250817638588e-13),
            (10, 0.3, 0.466014266387059, 0.01644920081540413),
            (10, 0.85, 0.10969233401336786, 0.9984071707912328),
            (10, 1.6, 0.2212980405799977, 0.6355291995467136),
            (10, 5, 0.5839709375, 0.0009043568330137373),
            (10, 40, 0.8984976987501085, 2.3216079489555263e-10),
            (15, 0.02, 0.9342381005179308, 3.72087628378188e-18),
            (15, 0.3, 0.45085734773453223, 0.00265543052106898),
            (15, 0.85, 0.09288361560675329, 0.9979324887909782),
            (15, 1.6, 0.20429595328282257, 0.49539266474932875),
            (15, 5, 0.5652633333333333, 4.711248785518242e-05),
            (15, 40, 0.9185524503918987, 9.208742766782036e-17),
            (20, 0.02, 0.9288782635358024, 2.1931939422795057e-23),
            (20, 0.3, 0.4428043947288186, 0.00043200180163482926),
            (20, 0.85, 0.08468110671953416, 0.9962571637334131),
            (20, 1.6, 0.1961146418095916, 0.3758568935140476),
            (20, 5, 0.5598739550781249, 1.952554399131483e-06),
            (20, 40, 0.9057748505127101, 6.087158107358288e-21),
            (30, 0.02, 0.9213761685145376, 1.4712056020108006e-33),
            (30, 0.3, 0.43446890875229904, 1.1479747107321034e-05),
            (30, 0.85, 0.07635900068003454, 0.9892343372942639),
            (30, 1.6, 0.18796470724666436, 0.21122522423478896),
            (30, 5, 0.5510079719650205, 4.381294847050914e-09),
            (30, 40, 0.902539938934832, 9.257103245598355e-31),
            (50, 0.02, 0.912271537066652, 2.8758058573271705e-53),
            (50, 0.3, 0.42767156352112556, 8.077430522235665e-09),
            (50, 0.85, 0.0697057815518492, 0.9542822014569963),
            (50, 1.6, 0.1812980405799977, 0.06553489760609577),
            (50, 5, 0.5449874893, 1.8855450665914854e-14),
            (50, 40, 0.8970038203460047, 8.848585394198524e-50),
            (80, 0.02, 0.911049176586732, 1.7190587205312286e-84),
            (80, 0.3, 0.42407804597420756, 1.4421696191497922e-13),
            (80, 0.85, 0.06596540368682913, 0.8543690311675932),
            (80, 1.6, 0.17757890552464806, 0.011182290722386079),
            (80, 5, 0.5412422437000275, 1.7949507105323319e-22),
            (80, 40, 0.8930051102630675, 4.63157918934521e-78),
            (100, 0.02, 0.9094366793234251, 1.0007230331491398e-104),
            (100, 0.3, 0.4228043947288186, 9.972941754475423e-17),
            (100, 0.85, 0.0647190491670766, 0.7716761352065553),
            (100, 1.6, 0.17632684778054236, 0.0034365824643004528),
            (100, 5, 0.539950637834375, 8.13356922333927e-28),
            (100, 40, 0.8915522600380945, 7.037695139398139e-97),
            (140, 0.02, 0.9083626995372397, 9.970555157951408e-146),
            (140, 0.3, 0.42139043222029693, 4.709927847403581e-23),
            (140, 0.85, 0.06329291919705421, 0.6062046152216505),
            (140, 1.6, 0.1748934077512982, 0.0003241235126239881),
            (140, 5, 0.5385613429608245, 1.6164848345822896e-38),
            (140, 40, 0.8905563576423438, 6.739090059537191e-135),
            (141, 0.02, 0.908332687600411, 9.570728468356557e-147),
            (141, 0.3, 0.42137720020620795, 3.2623138722711334e-23),
            (141, 5, 0.5385318447238603, 8.745265350158055e-39),
            (141, 40, 0.8904952589648282, 7.98965320368097e-136),
            (300, 0.3, 0.4195028393845909, 2.3322976930298367e-48),
            (300, 1.6, 0.1729944404280554, 2.54307777189283e-08),
            (300, 5, 0.5366584156121271, 2.60357434394882e-81),
            (300, 40, 0.88862972672863, 2.7961576795531e-286),
            (1000, 0.3, 0.4183368891493235, 4.574797100937236e-159),
            (1000, 1.6, 0.17182907062939456, 2.756525606760327e-26),
            (1000, 5, 0.5354920713409232, 1.5550042687763145e-268)
        };

        public void KolmogorovSmirnovOneSample_MatchesTheExactDistribution()
        {
            foreach (var (n, a, d, p) in KsExact)
            {
                var sample = Enumerable.Range(0, n).Select(i => Math.Pow((i + 0.5) / n, a)).ToArray();
                var r = HypothesisTests.KolmogorovSmirnovOneSample(sample, x => x);
                Near(d, r.Statistic, 1e-13);
                Near(p, r.PValue, 1e-9);
            }
        }

        // ---------------- Smaller ones, continued ----------------

        public void MultipleRegression_PredictorOnAnOffset_MatchesTheExactFit()
        {
            // x = 1.7e9 + i/16, y = (i² mod 7) + 3i: exact values from rational arithmetic. The slope was 7e-7 off.
            int n = 10;
            var x = new double[n, 1];
            var y = new double[n];
            for (int i = 0; i < n; i++) { x[i, 0] = 1.7e9 + i * 0.0625; y[i] = (i * i) % 7 + 3 * i; }
            var r = MultipleRegression.Fit(x, y);
            Near(49.45454545454545, r.Coefficients[1], 1e-12);
            Near(-84072727271.23636, r.Coefficients[0], 1e-12);
            Near(2.9356364612306627, r.StandardErrors[1], 1e-12);
            Near(4990581984.917774, r.StandardErrors[0], 1e-12);
            Near(0.9725836848245535, r.RSquared, 1e-13);
            Near(16.327270369096237, r.Predict(new[] { 1.7e9 + 0.3 }), 1e-9);
        }

        public void ChangePointCusum_HugeValues_StayFinite_AndNonFiniteValuesAreRefused()
        {
            var c = ChangePoint.Cusum(new[] { 1.7e308, -1.7e308, 1.7e308 });
            Near(1.7e308 * (2.0 / 3), c[0], 1e-15);
            Near(-1.7e308 * (2.0 / 3), c[1], 1e-15);
            Check.True(Math.Abs(c[2]) < 1e293, c[2].ToString("R"));
            // A cumulative sum that is itself past the double range (2.04e308 at [0]) is refused, not returned as ∞.
            var e = Check.Throws<ArgumentException>(() => ChangePoint.Cusum(new[] { 1.7e308, 1.7e308, -1.7e308, -1.7e308, -1.7e308 }));
            Check.Equal("series", e.ParamName);
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
            {
                e = Check.Throws<ArgumentException>(() => ChangePoint.Cusum(new[] { 1, bad }));
                Check.Equal("series", e.ParamName);
            }
        }

        public void LogNormalPdf_UnderflowingDenominator_IsComputedInLogs()
        {
            Check.Equal(0.0, Distributions.LogNormalPdf(5e-324, 0, 0.001));      // was 0/0 = NaN
            // mu = ln x − 37σ: the density is moderate while x·σ·√2π is subnormal (mpmath at 60 digits).
            Near(2.1200301534303777e+22, Distributions.LogNormalPdf(1e-320, -773.8272408909739, 1), 1e-11);
            Near(4.2400131094768794e+17, Distributions.LogNormalPdf(1e-315, -743.8143042946427, 0.5), 1e-11);
            // z = 0.37/0.01 magnifies ln x's own half-ulp rounding: 4e-10 relative is the conditioning, not the method.
            Near(70666885036402.38, Distributions.LogNormalPdf(3e-310, -713.0727665394861, 0.01), 1e-9);
        }

        public void NormalQuantile_SubnormalP_IsRefined()
        {
            // Φ⁻¹ of the double p (mpmath, solved on ln Φ); the unrefined estimate was 1.5e-9 relative off.
            var cases = new[]
            {
                (1e-305, -37.3563460930671), (1e-310, -37.663060331949524), (1e-315, -37.96730035106736),
                (1e-320, -38.26912534303265), (2.5e-322, -38.36512688916019), (5e-324, -38.467405617144344),
            };
            foreach (var (p, want) in cases) Near(want, Distributions.NormalQuantile(p), 1e-15);
        }

        public void Gamma_FarNegativeArguments_AreSubnormalNotZero()
        {
            // mpmath; Γ(1 − x) overflows here, and the reflection returned 0. Subnormal results carry fewer digits.
            Near(1.9316265431712e-310, SpecialFunctions.Gamma(-171.5), 1e-11);
            Near(-5.742252072596e-312, SpecialFunctions.Gamma(-172.25), 1e-9);
            Near(-1.4378615e-317, SpecialFunctions.Gamma(-174.75), 1e-6);
            Near(-1.196e-321, SpecialFunctions.Gamma(-176.5), 1e-2);
            Check.Equal(5e-324, SpecialFunctions.Gamma(-177.5));
            Check.Equal(0.0, Math.Abs(SpecialFunctions.Gamma(-190.5)));
        }

        public void TTests_NearlyEqualMeansOnAnOffset_MatchTheExactStatistic()
        {
            // Exact from rational arithmetic; the means' separate rounding left t 1e-8 relative off.
            var a = Enumerable.Range(0, 12).Select(i => 1e6 + (i * 7 % 11) * Math.Pow(2, -20)).ToArray();
            var b = Enumerable.Range(0, 9).Select(i => 1e6 + (i * 5 % 13) * Math.Pow(2, -20) + Math.Pow(2, -30)).ToArray();
            Near(-0.5635459195868876, HypothesisTests.TwoSampleT(a, b).Statistic, 1e-13);
            Near(-0.5799687577317012, HypothesisTests.TwoSampleT(a, b, equalVariance: true).Statistic, 1e-13);
            Near(-0.41522739926869984, HypothesisTests.OneSampleT(a, 1e6 + 5 * Math.Pow(2, -20)).Statistic, 1e-13);
        }

        // The drop-in LnGamma copies reflected with Math.Sin(πx), which near a negative integer loses digits (3.5e-9 at
        // −20.000001); SpecialFunctions reduces the argument first. ln|Γ(x)| from mpmath.
        public void LnGammaCopies_NegativeArguments_MatchTheReference()
        {
            var cases = new[] { (-20.000001, -28.520108924339542), (-0.5, 1.2655121234846454), (-3.7, -1.3797399049658245),
                (-100.25, -363.4009232278215), (-1e-5, 11.512931237209125) };
            foreach (var type in new[] { typeof(Distributions), typeof(Anova), typeof(GaugeRR), typeof(DistributionFit) })
            {
                var m = type.GetMethod("LnGamma", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(double) }, null)
                    ?? throw new InvalidOperationException(type.Name + ".LnGamma not found");
                foreach (var (x, want) in cases)
                    Near(want, (double)m.Invoke(null, new object[] { x })!, 1e-13);
            }
        }

#if NETFRAMEWORK
        // Only .NET Framework caps an array at 2 GB: 25,000 points need 312 million slopes (2.5 GB). The documented point
        // limit is an ArgumentException; there it was OutOfMemoryException.
        public void SlopeArraysPastTwoGigabytes_OnNetFramework_ThrowArgumentException()
        {
            var y = Enumerable.Range(0, 25000).Select(i => (double)i).ToArray();
            var e = Check.Throws<ArgumentException>(() => Trend.MannKendall(y));
            Check.Equal("series", e.ParamName);
            e = Check.Throws<ArgumentException>(() => RobustRegression.TheilSen(y, y));
            Check.Equal("x", e.ParamName);
        }
#endif
    }
}
