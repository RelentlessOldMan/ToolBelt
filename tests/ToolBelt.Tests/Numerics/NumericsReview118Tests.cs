using System;
using System.Reflection;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    // Regressions from review round 118 (distributions at the edges of the double range). References are 700-digit mpmath
    // (fewer digits gave wrong values at shapes near 1e308).
    public sealed class NumericsReview118Tests
    {
        private static void Rel(double expected, double actual, double tol, string what)
            => Check.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:R}, got {actual:R}");

        private static double Private(Type type, string name, params object[] args)
            => (double)type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

        // x, d1, d2, lower, upper (NaN: not referenced). d1·x overflows: FMasses formed d1·x + d2 = ∞ and returned NaN.
        private static readonly double[][] Overflow =
        {
            new[] { 1e308, 2, 0.001, 0.30120548893268129, 0.69879451106731871 },
            new[] { 1e308, 2, 0.01, double.NaN, 0.028086320670718552 },
            new[] { 2e307, 10, 0.05, double.NaN, 1.9159201948032989e-8 },
            new[] { 1e300, 1e9, 0.04, double.NaN, 9.3517465457726495e-7 },
            new[] { 1e308, 1e308, 0.001, double.NaN, 0.69899607405182564 },
            new[] { 1e305, 1e300, 1e-5, 0.0035632231719533847, 0.99643677682804662 },
            new[] { 1e306, 1e300, 1e-280, 6.7471539800508456e-278, double.NaN },
            new[] { 1e308, 1e300, 1e-300, 7.0004383402801911e-298, double.NaN },
            new[] { 1e308, 3, 1e-300, 7.0022832159508391e-298, double.NaN },
        };

        // Tiny shapes: 1 − yᵃ/(a·B(a, b)) cancelled (ln a + ln B kept eps·|ln a|), or d1·x was subnormal; upper tails.
        private static readonly double[][] Tiny =
        {
            new[] { 1e-300, 1e-100, 1e-20, 1.0000000000000001e-80 },
            new[] { 5e-324, 1e-100, 1e-20, 1.0000000000000001e-80 },
            new[] { 1e-10, 1e-100, 1e300, 1.2670014587250172e-98 },
            new[] { 1e-10, 1e-8, 1e300, 2.0781229437534002e-7 },
            new[] { 1e-300, 1e-6, 3.0, 0.00035247583843244281 },
        };

        public void FTails_AreAccurateWhenNumeratorTimesXOverflows()
        {
            foreach (double[] c in Overflow)
            {
                string at = $"({c[0]:R}, {c[1]:R}, {c[2]:R})";
                if (!double.IsNaN(c[3])) Rel(c[3], Distributions.FCdf(c[0], c[1], c[2]), 1e-12, "FCdf" + at);
                if (!double.IsNaN(c[4])) Rel(c[4], Distributions.FSurvival(c[0], c[1], c[2]), 1e-12, "FSurvival" + at);
                double sum = Distributions.FCdf(c[0], c[1], c[2]) + Distributions.FSurvival(c[0], c[1], c[2]);
                Check.True(Math.Abs(sum - 1) < 1e-14, $"masses sum to {sum:R} at {at}");
            }
        }

        public void FTails_AreAccurateAtTinyShapes()
        {
            foreach (double[] c in Tiny)
            {
                string at = $"({c[0]:R}, {c[1]:R}, {c[2]:R})";
                Rel(c[3], Distributions.FSurvival(c[0], c[1], c[2]), 1e-12, "FSurvival" + at);
                Rel(c[3], Anova.FUpperTailProbability(c[0], c[1], c[2]), 1e-12, "Anova upper" + at);
            }
        }

        public void FSurvival_IsNeverNegativeZero()
        {
            // −Expm1(0) is −0 (and Math.Max(0, −0.0) keeps it on .NET Framework).
            foreach (double d in new[] { 1e-300, 1e-200, 1e-100 })
            {
                double s = Distributions.FSurvival(1e-300, d, 1e-20), a = Anova.FUpperTailProbability(1e-300, d, 1e-20);
                Check.True(s >= 0 && !(1 / s < 0), $"FSurvival = {s:R} at d1 = {d:R}");
                Check.True(a >= 0 && !(1 / a < 0), $"Anova = {a:R} at d1 = {d:R}");
            }
        }

        public void FCopies_AgreeWhenNumeratorTimesXOverflows()
        {
            foreach (double[] c in Overflow)
            {
                double want = Distributions.FSurvival(c[0], c[1], c[2]);
                string at = $"({c[0]:R}, {c[1]:R}, {c[2]:R})";
                Rel(want, Anova.FUpperTailProbability(c[0], c[1], c[2]), 1e-13, "Anova" + at);
                Rel(want, Private(typeof(GaugeRR), "FOverflowUpper", c[0], c[1], c[2]), 1e-13, "GaugeRR" + at);
            }
        }

        public void LnAB_IsAccurateToTheShapeAbsolutely()
        {
            // a, b, ln(a·B(a, b)): the tails need the absolute error near eps·max(a, |value|).
            double[][] rows =
            {
                new[] { 1.9389674396996575e-87, 2.4357011098593206, -2.4204991879525621284e-87 },
                new[] { 1.3983012001609745e-187, 1.0875522146510242e-69, 1.2857324745641422072e-118 },
                new[] { 1.927341973172703e-192, 3.452505241698938e-57, 5.5824447415589731465e-136 },
                new[] { 6.848226860837954e-128, 11.48932370452556, -2.0369944090533277039e-127 },
                new[] { 6.065042352669189e-195, 9.172632582451649e-199, 8.7968087922887661879 },
                new[] { 7.281142838842409e-153, 0.10219011000508535, 7.0110760996071915474e-152 },
            };
            foreach (Type t in new[] { typeof(Distributions), typeof(Anova), typeof(GaugeRR) })
                foreach (double[] r in rows)
                {
                    double got = Private(t, "LnAB", r[0], r[1]);
                    Check.True(Math.Abs(got - r[2]) <= 1e-14 * Math.Max(r[0], Math.Abs(r[2])),
                        $"{t.Name}.LnAB({r[0]:R}, {r[1]:R}): expected {r[2]:R}, got {got:R}");
                }
        }

        public void FPdf_IsAccurateWhenNumeratorTimesXOverflows()
        {
            Rel(3.4939725553365936e-312, Distributions.FPdf(1e308, 2, 0.001), 1e-10, "FPdf(1e308; 2, 0.001)");
            Rel(1.8703493091545298e-308, Distributions.FPdf(1e300, 1e9, 0.04), 1e-10, "FPdf(1e300; 1e9, 0.04)");
        }

        public void FQuantile_PastMaxValue_IsInfinity_AndBelowIt_Inverts()
        {
            // Solve doubled its bracket past MaxValue and bisected between ∞ and ∞.
            Check.Equal(double.PositiveInfinity, Distributions.FQuantile(0.5, 2, 0.001));
            Check.Equal(double.PositiveInfinity, Distributions.FQuantile(1 - 1e-12, 10, 0.05));
            double q = Distributions.FQuantile(0.25, 2, 0.001);
            Check.True(q > 1e246 && q < 1e247, $"FQuantile(0.25; 2, 0.001) = {q:R}");
            Rel(0.25, Distributions.FCdf(q, 2, 0.001), 1e-12, "FCdf(FQuantile(0.25))");
        }

        public void ChiSquareQuantile_AtHugeDegreesOfFreedom_StaysNearK()
        {
            foreach (double k in new[] { 1e300, 8e307, 1e308, double.MaxValue })
                foreach (double p in new[] { 0.01, 0.5, 0.99 })
                {
                    double x = Distributions.ChiSquareQuantile(p, k), y = Distributions.ChiSquareInverseSurvival(1 - p, k);
                    Check.True(Math.Abs(x / k - 1) < 1e-12, $"ChiSquareQuantile({p}; {k:R}) = {x:R}");
                    Check.True(Math.Abs(y / k - 1) < 1e-12, $"ChiSquareInverseSurvival({1 - p}; {k:R}) = {y:R}");
                }
            // The true quantile is MaxValue + ~1e155, within half an ulp: it rounds to MaxValue, not ∞.
            Check.Equal(double.MaxValue, Distributions.ChiSquareQuantile(0.99, double.MaxValue));
            Check.Equal(double.MaxValue, Distributions.ChiSquareInverseSurvival(1e-300, double.MaxValue));
        }

        private static double[] Times(double[] x, double s)
        {
            var r = new double[x.Length];
            for (int i = 0; i < x.Length; i++) r[i] = x[i] * s;
            return r;
        }

        // Dyadic samples with few bits, so scaling by a power of two is exact even into the subnormals.
        private static readonly double[] A = { 1.5, 2.25, 3, 4.75, 2 }, B = { 3.5, 4, 2.5, 6.25, 5.75, 4.5 };

        public void TTests_AndFTest_AreScaleInvariantForTinyData()
        {
            // The sums of squares underflowed: t was ±∞ or NaN for data below ~1e-154.
            foreach (double s in new[] { Math.Pow(2, -1000), Math.Pow(2, -1060), Math.Pow(2, -1072) })
            {
                TestResult o = HypothesisTests.OneSampleT(A, 1), os = HypothesisTests.OneSampleT(Times(A, s), s);
                Rel(o.Statistic, os.Statistic, 1e-12, $"OneSampleT t at {s:R}");
                Rel(o.PValue, os.PValue, 1e-12, $"OneSampleT p at {s:R}");
                foreach (bool eq in new[] { false, true })
                {
                    TestResult w = HypothesisTests.TwoSampleT(A, B, eq), ws = HypothesisTests.TwoSampleT(Times(A, s), Times(B, s), eq);
                    Rel(w.Statistic, ws.Statistic, 1e-12, $"TwoSampleT({eq}) t at {s:R}");
                    Rel(w.PValue, ws.PValue, 1e-12, $"TwoSampleT({eq}) p at {s:R}");
                    Rel(w.DegreesOfFreedom, ws.DegreesOfFreedom, 1e-12, $"TwoSampleT({eq}) df at {s:R}");
                }
                TestResult f = HypothesisTests.FTestEqualVariances(A, B), fs = HypothesisTests.FTestEqualVariances(Times(A, s), Times(B, s));
                Rel(f.Statistic, fs.Statistic, 1e-12, $"F at {s:R}");
                Rel(f.PValue, fs.PValue, 1e-12, $"F p at {s:R}");
            }
        }

        public void ConfidenceIntervals_ScaleWithTinyData()
        {
            // Subnormal data: s/√n rounded on the subnormal grid before t or z multiplied it, so the margin was off by up to
            // t·ε/2 (ForMeanT of {0, 3ε} was (−11ε, 15ε); it is (−17.6ε, 20.6ε)). The bound at subnormal scales is the
            // rounding of the endpoint itself.
            var cases = new[] { (A, Math.Pow(2, -1000)), (A, Math.Pow(2, -1060)), (A, Math.Pow(2, -1072)),
                                (new[] { 0, 3.0 }, Math.Pow(2, -1000)), (new[] { 0, 3.0 }, double.Epsilon) };
            foreach (var (data, s) in cases)
            {
                double[] x = Times(data, s);
                var pairs = new[]
                {
                    ("ForMean", ConfidenceInterval.ForMean(data), ConfidenceInterval.ForMean(x)),
                    ("ForMeanT", ConfidenceInterval.ForMeanT(data), ConfidenceInterval.ForMeanT(x)),
                    ("ForStandardDeviation", ConfidenceInterval.ForStandardDeviation(data), ConfidenceInterval.ForStandardDeviation(x)),
                };
                foreach (var (name, o, t) in pairs)
                {
                    double lo = o.Lower * s, hi = o.Upper * s, tol = s < 1e-300 ? double.Epsilon : 1e-12 * Math.Max(Math.Abs(lo), Math.Abs(hi));
                    Check.True(Math.Abs(t.Lower - lo) <= tol, $"{name}(n = {data.Length}) lower at {s:R}: expected {lo:R}, got {t.Lower:R}");
                    Check.True(Math.Abs(t.Upper - hi) <= tol, $"{name}(n = {data.Length}) upper at {s:R}: expected {hi:R}, got {t.Upper:R}");
                }
            }
        }
    }
}
