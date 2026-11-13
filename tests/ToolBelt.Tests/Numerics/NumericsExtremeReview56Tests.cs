using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ToolBelt.Collections;
using ToolBelt.Control;
using ToolBelt.Documents;
using ToolBelt.Grids;
using ToolBelt.IO;
using ToolBelt.Identifiers;
using ToolBelt.Intervals;
using ToolBelt.Numerics;
using ToolBelt.Objects;
using ToolBelt.Quality;
using ToolBelt.Signal;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;
using ToolBelt.Visualization;
using NumHistogram = ToolBelt.Numerics.Histogram;
using VisHistogram = ToolBelt.Visualization.Histogram;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Regressions from review round 56 (cross-cutting: non-finite and extreme numerics). Values near ±double.MaxValue,
    /// subnormals, infinite bracket ends and sizes past Array.MaxLength must give the right finite answer or an
    /// ArgumentException naming the parameter, never a hang, a silent ∞/NaN/0, or an OutOfMemoryException. Also the two
    /// open gaps from round 53 (53-1, 53-2) and the sweep that gave argument checks their ParamName. Each test failed
    /// before its fix; the hang regressions run on a task with a timeout so the old code fails instead of spinning.
    /// </summary>
    public sealed class NumericsExtremeReview56Tests
    {
        private const double Max = double.MaxValue;
        private const double Eps = double.Epsilon;
        private const int ArrayMaxLength = 0x7FFFFFC7;

        private static void CloseRel(double expected, double actual, double rel, string? message = null)
            => Check.Close(expected, actual, Math.Abs(expected) * rel, message);

        private static void Finite(double v, string message)
            => Check.True(!double.IsNaN(v) && !double.IsInfinity(v), $"{message}: {v:R} is not finite");

        // The old Pow2Scale spun for ever on a value whose log2 rounded to 1024: run on a task so the old code fails.
        // A documented ArgumentException (a spread that overflows a double) is an acceptable outcome; anything else is not.
        private static Exception? Finishes(string name, Action action)
        {
            Exception? error = null;
            var task = Task.Run(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            Check.True(task.Wait(TimeSpan.FromSeconds(10)), name + " hung");
            if (error != null && !(error is ArgumentException))
                throw new CheckFailedException($"{name} threw {error.GetType().Name}: {error.Message}");
            return error;
        }

        private static readonly double[] WithMax = { 0, Max, 1 };

        // ---------------------------------------------------------------- 53-1, 53-2

        public void PidController_ScaledDerivativeOnErrorAfterOverflow_IsFinite()
        {
            // The scaled retry used the stored error, which had overflowed to ∞, so the second output was −∞.
            var pid = new PidController(1e-300, 0, 1e-300, derivativeOnMeasurement: false) { Setpoint = Max };
            double first = pid.Update(-Max, 1);
            CloseRel(2 * (1e-300 * Max), first, 1e-12, "first output");
            double second = pid.Update(Max, 1);
            // P = 0 (error 0); D = kd·(0 − 2·Max)/1.
            CloseRel(-2 * (1e-300 * Max), second, 1e-12, "second output");
        }

        public void PidController_ScaledDerivativeOnErrorWithSetpointChange_UsesLastSetpoint()
        {
            // D only: error goes from ∞ (Max − (−Max)) to (−Max − Max) after the setpoint moves.
            var pid = new PidController(0, 0, 1e-300, derivativeOnMeasurement: false) { Setpoint = Max };
            pid.Update(-Max, 1);
            pid.Setpoint = -Max;
            double d = pid.Update(Max, 1);
            // (−2·Max) − (2·Max) = −4·Max, times 1e-300.
            CloseRel(-4 * (1e-300 * Max), d, 1e-12, "derivative output");
        }

        public void IirFilter_EllipticExtremeRipple_NamesASpecParameter()
        {
            var ex = Check.Throws<ArgumentException>(() => IirFilter.EllipticLowPass(20, 1e-300, 300, 0.25, 1));
            Check.True(ex.ParamName == "passRippleDb" || ex.ParamName == "stopAttenuationDb", "ParamName " + ex.ParamName);
            var bp = Check.Throws<ArgumentException>(() => IirFilter.EllipticBandPass(20, 1e-300, 300, 0.2, 0.3, 1));
            Check.True(bp.ParamName == "passRippleDb" || bp.ParamName == "stopAttenuationDb", "band-pass ParamName " + bp.ParamName);
        }

        public void IirFilter_Chebyshev1TinyRipple_NamesRippleNotCutoff()
        {
            var ex = Check.Throws<ArgumentException>(() => IirFilter.Chebyshev1LowPass(1, 1e-300, 0.25, 1));
            Check.Equal("rippleDb", ex.ParamName);
        }

        public void IirFilter_Chebyshev2TinyAttenuation_NamesAttenuationNotStopEdge()
        {
            var ex = Check.Throws<ArgumentException>(() => IirFilter.Chebyshev2LowPass(1, 1e-300, 0.25, 1));
            Check.Equal("stopAttenuationDb", ex.ParamName);
        }

        public void IirFilter_EllipticOrderOneTinyRipple_NamesRippleNotCutoff()
        {
            var ex = Check.Throws<ArgumentException>(() => IirFilter.EllipticLowPass(1, 1e-300, 40, 0.25, 1));
            Check.True(ex.ParamName == "passRippleDb" || ex.ParamName == "stopAttenuationDb", "ParamName " + ex.ParamName);
        }

        public void IirFilter_OrderForTooNarrowTransition_NamesStopAttenuation()
        {
            var ex = Check.Throws<ArgumentException>(() => IirFilter.ButterworthOrder(0.1, 0.1 + 1e-14, 1, 60, 1));
            Check.Equal("stopAttenuationDb", ex.ParamName);
        }

        // ---------------------------------------------------------------- root finding, paths, axis ticks

        public void RootFinding_BisectionInfiniteEnd_FailsInsteadOfReportingInfinity()
        {
            var a = RootFinding.Bisection(x => x, double.NegativeInfinity, 1);
            Check.False(a.Converged, "−∞ bracket converged at " + a.Root);
            Check.True(double.IsNaN(a.Root), "root " + a.Root);
            var b = RootFinding.Bisection(x => x - 3, 0, double.PositiveInfinity);
            Check.False(b.Converged, "+∞ bracket converged at " + b.Root);
        }

        public void RootFinding_BrentInfiniteEnd_FailsInsteadOfReportingAnEndpoint()
        {
            var a = RootFinding.Brent(x => x, double.NegativeInfinity, 1);
            Check.False(a.Converged, "−∞ bracket converged at " + a.Root);
            Check.True(double.IsNaN(a.Root), "root " + a.Root);
            var b = RootFinding.Brent(x => x - 3, 0, double.PositiveInfinity);
            Check.False(b.Converged, "+∞ bracket converged at " + b.Root);
            var c = RootFinding.Brent(x => x, double.NaN, 1);
            Check.False(c.Converged, "NaN bracket converged at " + c.Root);
        }

        public void GridPathfinding_DijkstraNaNCost_Throws()
        {
            var ex = Check.Throws<InvalidOperationException>(() => GridPathfinding.Dijkstra(2, 3, new Cell(0, 0), new Cell(0, 2),
                (r, c) => true, (r, c) => r == 0 && c == 1 ? double.NaN : 1));
            Check.True(ex.Message.Contains("NaN"), ex.Message);
        }

        public void AxisTicks_TimeTinyRange_MatchesLinearTicks()
        {
            foreach (bool loose in new[] { true, false })
            {
                var t = AxisTicks.Time(1.6e-19, 1.7e-19, 6, loose);
                var l = AxisTicks.Linear(1.6e-19, 1.7e-19, 6, loose);
                Check.True(t.Count >= 2, $"loose={loose}: {t.Count} ticks");
                Check.Equal(l.Count, t.Count, "count");
                for (int i = 0; i < t.Count; i++)
                {
                    Check.Equal(l.Values[i], t.Values[i], "value " + i);
                    Check.Equal(l.Labels[i] + "s", t.Labels[i], "label " + i);
                }
                Check.Equal(t.Labels.Count, t.Labels.Distinct().Count(), "labels distinct");
                if (loose) Check.True(t.Min <= 1.6e-19 && t.Max >= 1.7e-19, $"[{t.Min:R}, {t.Max:R}] does not bracket the data");
            }
        }

        public void AxisTicks_FlatRangeAtMaxValue_Finishes()
        {
            foreach (double v in new[] { Max, -Max, 1.7e308, -1.7e308 })
                foreach (bool loose in new[] { true, false })
                {
                    TickSet? ticks = null;
                    var task = Task.Run(() => ticks = AxisTicks.Linear(v, v, 6, loose));
                    Check.True(task.Wait(TimeSpan.FromSeconds(10)), $"Linear({v:R}, {v:R}) hung");
                    if (task.IsFaulted) throw new CheckFailedException($"Linear({v:R}) threw {task.Exception!.InnerException!.Message}");
                    Check.True(ticks!.Count >= 1, "no ticks");
                    foreach (double t in ticks.Values) Finite(t, "tick");
                    if (loose) Check.True(ticks.Min <= v && ticks.Max >= v, $"[{ticks.Min:R}, {ticks.Max:R}] misses {v:R}");
                }
        }

        public void AxisTicks_MaxTicksPastTenThousand_Throws()
        {
            Check.Equal("maxTicks", Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.Linear(0, 1, 10001)).ParamName);
            Check.Equal("maxTicks", Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.Time(0, 1, 10001)).ParamName);
            Check.Equal("maxTicks", Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.Linear(0, 1, int.MaxValue)).ParamName);
            Check.True(AxisTicks.Linear(0, 1, 10000).Count <= 10000, "10,000 is allowed");
        }

        public void AxisTicks_FormatTimeNonFinite_Throws()
        {
            foreach (double s in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Check.Equal("seconds", Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.FormatTime(s, 10)).ParamName);
        }

        public void AxisTicks_FormatTimeHuge_UsesExponentForm()
        {
            Check.Equal("1e+300s", AxisTicks.FormatTime(1e300, 1e5));
            Check.Equal("1e+16s", AxisTicks.FormatTime(1e16, 1));
            Check.Equal("1000000000000000s", AxisTicks.FormatTime(1e15, 1));   // below 2^53 s: exact, kept in full
            Check.Equal("-2.5e+20s", AxisTicks.FormatTime(-2.5e20, 1));
            Check.True(AxisTicks.FormatTime(Max, Max).Length < 20, AxisTicks.FormatTime(Max, Max));
        }

        public void AxisTicks_TimeNearMaxValue_GivesFiniteTicksThatBracketTheData()
        {
            // k·step overflowed to ∞: the old code labelled ticks "Infinitys" (and then threw from FormatTime).
            foreach (var (lo, hi) in new[] { (Max, Max), (-Max, -Max), (0.0, Max), (-Max, Max), (1e16, 3e16) })
            {
                var t = AxisTicks.Time(lo, hi, 8, loose: true);
                Check.True(t.Values.Count > 0, $"[{lo:R}, {hi:R}]: no ticks");
                foreach (double v in t.Values) Check.True(!double.IsInfinity(v) && !double.IsNaN(v), $"[{lo:R}, {hi:R}]: tick {v:R}");
                foreach (string l in t.Labels) Check.False(l.Contains("Infinity") || l.Contains("NaN"), l);
                Check.True(t.Min <= lo && t.Max >= hi, $"[{lo:R}, {hi:R}] in [{t.Min:R}, {t.Max:R}]");
            }
        }

        // ---------------------------------------------------------------- histogram, capability, medians

        public void Histogram_SubnormalRange_HasEqualBins()
        {
            var h = new NumHistogram(0, 4 * Eps, 4);
            Check.Equal(Eps, h.BinWidth, "width");
            for (int i = 0; i < 4; i++) Check.Equal(i * Eps, h.BinLowerBound(i), "bound " + i);
            foreach (double v in new[] { 0, Eps, 2 * Eps, 3 * Eps }) h.Add(v);
            for (int i = 0; i < 4; i++) Check.Equal(1L, h.Bins[i], "bin " + i);
        }

        public void Histogram_RangeTooNarrowForBinCount_ThrowsNamingBinCount()
        {
            // [0, 4ε) in 8 bins: half-ε bins can't have distinct bounds.
            Check.Equal("binCount", Check.Throws<ArgumentException>(() => new NumHistogram(0, 4 * Eps, 8)).ParamName);
            Check.Equal("binCount", Check.Throws<ArgumentException>(() => new NumHistogram(0, Eps, 1 << 20)).ParamName);
            // A finite range much wider still bins (strictly increasing bounds).
            var ok = new NumHistogram(0, 1e-300, 1000);
            for (int i = 1; i < 1000; i++) Check.True(ok.BinLowerBound(i - 1) < ok.BinLowerBound(i), "bound " + i);
        }

        public void ProcessCapability_SpecsNearMaxValue_CpIsFinite()
        {
            var a = ProcessCapability.Compute(new[] { -1e300, 1e300, 0, 5e299, -5e299 }, -1.7e308, 1.7e308);
            Finite(a.Cp, "Cp");
            CloseRel(a.Cpk, a.Cp, 1e-12, "centred: Cp = Cpk");
            var b = ProcessCapability.Compute(new[] { 1.0, 2, 3, 4, 5 }, -Max, Max);
            Finite(b.Cp, "Cp at ±MaxValue");
            CloseRel(Max / 3 / b.Sigma, b.Cp, 1e-12, "Cp at ±MaxValue");
        }

        public void Hampel_SubnormalEvenWindow_MedianIsExact()
        {
            Check.True(new[] { Eps, Eps }.SequenceEqual(Hampel.Filter(new[] { Eps, Eps }, 1)), "[ε, ε] changed");
            Check.Equal(0, Hampel.CountOutliers(new[] { Eps, Eps }, 1));
            var three = new[] { 3 * Eps, 3 * Eps, 3 * Eps, 3 * Eps };
            Check.True(three.SequenceEqual(Hampel.Filter(three, 2)), "[3ε ×4] changed");
            // Near MaxValue the halves-first midpoint still holds.
            Check.True(new[] { Max, Max }.SequenceEqual(Hampel.Filter(new[] { Max, Max }, 1)), "[Max, Max] changed");
        }

        public void MedianFilter_SubnormalEdgeWindow_MedianIsExact()
        {
            Check.True(new[] { Eps, Eps }.SequenceEqual(MedianFilter.Apply(new[] { Eps, Eps }, 3)), "[ε, ε]");
            var r = MedianFilter.Apply(new[] { 3 * Eps, 3 * Eps, 3 * Eps, 3 * Eps }, 3);
            Check.True(r.All(v => v == 3 * Eps), "[3ε ×4] → " + string.Join(",", r));
            Check.True(new[] { -Max, -Max }.SequenceEqual(MedianFilter.Apply(new[] { -Max, -Max }, 3)), "[−Max, −Max]");
        }

        public void Trend_SensSlopeOfSubnormalSeries_IsEpsilon()
        {
            var r = Trend.MannKendall(new[] { 0, Eps, 2 * Eps, 3 * Eps });
            Check.Equal(6, r.S);
            Check.Equal(Eps, r.SensSlope);
        }

        public void RobustRegression_TheilSenSubnormalIntercept_IsExact()
        {
            var (slope, intercept) = RobustRegression.TheilSen(new[] { 0.0, 1, 2, 3 }, new[] { 3 * Eps, 3 * Eps, 3 * Eps, 3 * Eps });
            Check.Equal(0.0, slope);
            Check.Equal(3 * Eps, intercept);
        }

        // ---------------------------------------------------------------- Pow2Scale hangs at double.MaxValue

        public void LinearRegression_DataWithMaxValue_Finishes()
            => Finishes("LinearRegression.Fit", () => LinearRegression.Fit(new[] { 0.0, 1, 2 }, WithMax));

        public void Correlation_DataWithMaxValue_Finishes()
            => Finishes("Correlation.Pearson", () => Correlation.Pearson(new[] { 0.0, 1, 2 }, WithMax));

        public void ConfidenceInterval_DataWithMaxValue_Finishes()
        {
            Finishes("ForMean", () => ConfidenceInterval.ForMean(WithMax));
            Finishes("ForMeanT", () => ConfidenceInterval.ForMeanT(WithMax));
            Finishes("ForVariance", () => ConfidenceInterval.ForVariance(WithMax));
            Finishes("ForStandardDeviation", () => ConfidenceInterval.ForStandardDeviation(WithMax));
        }

        public void ChangePoint_DataWithMaxValue_Finishes()
        {
            Finishes("Cusum", () => ChangePoint.Cusum(WithMax));
            Finishes("DetectShift", () => ChangePoint.DetectShift(WithMax));
        }

        public void Anova_DataWithMaxValue_Finishes()
            => Finishes("Anova.OneWay", () => Anova.OneWay(WithMax, new[] { 1.0, 2, 3 }));

        public void HypothesisTests_DataWithMaxValue_Finish()
        {
            Finishes("OneSampleT", () => HypothesisTests.OneSampleT(WithMax, 0));
            Finishes("PairedT", () => HypothesisTests.PairedT(WithMax, new[] { 1.0, 2, 3 }));
            Finishes("TwoSampleT", () => HypothesisTests.TwoSampleT(WithMax, new[] { 1.0, 2, 3 }));
            Finishes("TwoSampleT pooled", () => HypothesisTests.TwoSampleT(WithMax, new[] { 1.0, 2, 3 }, equalVariance: true));
            Finishes("FTestEqualVariances", () => HypothesisTests.FTestEqualVariances(WithMax, new[] { 1.0, 2, 3 }));
        }

        public void MultipleRegression_DataWithMaxValue_Finishes()
        {
            Finishes("Fit (y)", () => MultipleRegression.Fit(new double[,] { { 0 }, { 1 }, { 2 }, { 3 } }, new[] { 0, Max, 1, 2 }));
            Finishes("Fit (x)", () => MultipleRegression.Fit(new double[,] { { 0 }, { Max }, { 1 }, { 3 } }, new[] { 0.0, 1, 2, 3 }));
        }

        public void ControlChart_DataWithMaxValue_Finishes()
            => Finishes("Individuals", () => ControlChart.Individuals(WithMax));

        public void GaugeRR_DataWithMaxValue_Finishes()
        {
            var m = new double[2, 2, 2];
            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) for (int k = 0; k < 2; k++) m[i, j, k] = i + j + 0.1 * k;
            m[1, 1, 1] = Max;
            Finishes("GaugeRR.Analyze", () => GaugeRR.Analyze(m));
        }

        public void MeasurementAgreement_DataWithMaxValue_Finishes()
            => Finishes("BlandAltman", () => MeasurementAgreement.BlandAltman(WithMax, new[] { 0.0, 1, 2 }));

        public void ProcessCapability_DataWithMaxValue_Finishes()
            => Finishes("ProcessCapability.Compute", () => ProcessCapability.Compute(WithMax, -1, 2));

        // ---------------------------------------------------------------- calculus, signal

        public void HexBin_RadiusOverflowingSqrt3_Throws()
        {
            var frame = new PlotFrame(0, 0, 640, 480, 0, 1, 0, 1);
            var points = new[] { (0.5, 0.5) };
            Check.Equal("radius", Check.Throws<ArgumentOutOfRangeException>(() => HexBin.Compute(points, frame, double.PositiveInfinity)).ParamName);
            Check.Equal("radius", Check.Throws<ArgumentOutOfRangeException>(() => HexBin.Compute(points, frame, 1.5e308)).ParamName);
            Check.Equal(1, HexBin.Compute(points, frame, 1e300).Count);
        }

        public void Differentiation_SampleHugeSpacing_IsHalfEverywhere()
        {
            var d = Differentiation.Sample(new[] { 0, 5e307, 1e308 }, 1e308);
            foreach (double v in d) CloseRel(0.5, v, 1e-12, "[" + string.Join(", ", d) + "]");
            var e = Differentiation.Sample(new[] { 0, 5e307, 1e308, 1.5e308 }, 1e308);
            foreach (double v in e) CloseRel(0.5, v, 1e-12, "[" + string.Join(", ", e) + "]");
        }

        public void Differentiation_UnevenSampleNearMaxValue_IsFinite()
        {
            var flat = Differentiation.Sample(new[] { 0.0, 1, 2 }, new[] { 1e308, 1e308, 1e308 });
            foreach (double v in flat) Check.Close(0, v, 1e293, "[" + string.Join(", ", flat) + "]");
            var line = Differentiation.Sample(new[] { 0.0, 1, 3 }, new[] { 1e308, 1.2e308, 1.6e308 });
            foreach (double v in line) CloseRel(2e307, v, 1e-12, "[" + string.Join(", ", line) + "]");
        }

        public void Differentiation_DerivativeAtMaxValue_IsOne()
        {
            CloseRel(1, Differentiation.Derivative(x => x, Max), 1e-9, "+Max");
            CloseRel(1, Differentiation.Derivative(x => x, -Max), 1e-9, "−Max");
            CloseRel(0.5, Differentiation.Derivative(x => x / 2, Max), 1e-9, "x/2 at +Max");
        }

        public void Differentiation_RichardsonAtMaxValue_IsOne()
        {
            CloseRel(1, Differentiation.RichardsonDerivative(x => x, Max), 1e-9, "+Max");
            CloseRel(1, Differentiation.RichardsonDerivative(x => x, -Max), 1e-9, "−Max");
            CloseRel(0.5, Differentiation.RichardsonDerivative(x => x / 2, Max), 1e-9, "x/2 at +Max");
        }

        public void Differentiation_StepThatOverflows_ThrowsNamingH()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Differentiation.Derivative(x => x, 0, Max));
            Check.Equal("h", ex.ParamName);
            Check.True(ex.Message.Contains("overflow"), ex.Message);
        }

        public void Integration_TrapezoidEndsNearMaxValue_IsFinite()
        {
            CloseRel(2e298, Integration.Trapezoid(new[] { -1e308, 1e308 }, new[] { 1e-10, 1e-10 }), 1e-12);
            CloseRel(3e298, Integration.Trapezoid(new[] { -1e308, 1e308, 1.5e308 }, new[] { 1e-10, 1e-10, 3e-10 }), 1e-12);
        }

        public void Integration_TrapezoidSubnormalPanel_ScalesBeforeHalving()
        {
            double twoTo100 = Math.Pow(2, 100);
            Check.Equal(Math.Pow(2, -975), Integration.Trapezoid(new[] { Eps, 0 }, twoTo100));
            Check.Equal(Math.Pow(2, -975), Integration.Trapezoid(new[] { 0, twoTo100 }, new[] { Eps, 0 }));
            Check.Equal(Math.Pow(2, -975), Integration.CumulativeTrapezoid(new[] { Eps, 0 }, twoTo100).Last());
        }

        public void LinearRegression_SubnormalX_HasFiniteSlope()
        {
            var fit = LinearRegression.Fit(new[] { 0, Eps }, new[] { 0, 1e-300 });
            CloseRel(1e-300 / Eps, fit.Slope, 1e-12, "slope");
            Check.Close(0, fit.Intercept, 1e-310, "intercept");
        }

        public void LinearRegression_YNearMaxValue_HasFiniteSlope()
        {
            var fit = LinearRegression.Fit(new[] { 1e300, 0, 0, 0 }, new[] { 1.7e308, -1.7e308, -1.7e308, -1.7e308 });
            CloseRel(3.4e8, fit.Slope, 1e-12, "slope");
            CloseRel(-1.7e308, fit.Intercept, 1e-12, "intercept");
        }

        public void PeakInterpolation_ParabolicNearMaxValue_MatchesScaled()
        {
            foreach (var (l, c, r) in new[] { (1.0, 1.5, 0.5), (-1.0, 1.0, 1.5), (1.7, 1.79, 1.0) })
            {
                var want = PeakInterpolation.Parabolic(l, c, r);
                var got = PeakInterpolation.Parabolic(l * 1e308, c * 1e308, r * 1e308);
                Check.Close(want.Offset, got.Offset, 1e-12, $"offset ({l}, {c}, {r})");
                CloseRel(want.Peak * 1e308, got.Peak, 1e-12, $"peak ({l}, {c}, {r})");
            }
        }

        public void Goertzel_HugeAndTinySamples_MagnitudeIsFinite()
        {
            CloseRel(4e200, Goertzel.Estimate(new[] { 1e200, 1e200, 1e200, 1e200 }, 0, 8).Magnitude, 1e-12, "1e200");
            CloseRel(4e-170, Goertzel.Estimate(new[] { 1e-170, 1e-170, 1e-170, 1e-170 }, 0, 8).Magnitude, 1e-12, "1e-170");
            // Off-DC the same scaling must hold: compare with the unit-scale estimate.
            var unit = Goertzel.Estimate(new[] { 1.0, 0, -1, 0, 1, 0, -1, 0 }, 2, 8);
            var big = Goertzel.Estimate(new[] { 1e300, 0, -1e300, 0, 1e300, 0, -1e300, 0 }, 2, 8);
            CloseRel(unit.Magnitude * 1e300, big.Magnitude, 1e-9, "1e300 tone");
        }

        public void Goertzel_HugeFrequencyAndRate_OmegaIsFinite()
        {
            var samples = new[] { 1.0, 0, -1, 0 };
            var huge = Goertzel.Estimate(samples, 1e308, 1.7e308);
            var unit = Goertzel.Estimate(samples, 1e308 / 1.7e308, 1);
            Finite(huge.Magnitude, "magnitude");
            Check.Equal(unit.Magnitude, huge.Magnitude, "magnitude");
            Check.Equal(unit.Phase, huge.Phase, "phase");
        }

        public void Fft_BluesteinSubnormalInput_MatchesScaledDft()
        {
            // Reference: the naive DFT of the input scaled by 2^1000 (exact), scaled back once (one rounding).
            double up = Math.Pow(2, 1000), down = Math.Pow(2, -1000);
            var rng = new Random(56);
            foreach (int n in new[] { 3, 5, 7 })
                for (int trial = 0; trial < 20; trial++)
                {
                    var x = new double[n];
                    for (int i = 0; i < n; i++) x[i] = rng.Next(-40, 41) * Eps;
                    if (n == 3 && trial == 0) x = new[] { 7 * Eps, 3 * Eps, 0 };
                    var got = Fft.Forward(x);
                    for (int k = 0; k < n; k++)
                    {
                        double re = 0, im = 0;
                        for (int j = 0; j < n; j++)
                        {
                            double angle = -2 * Math.PI * ((long)k * j % n) / n;
                            re += x[j] * up * Math.Cos(angle);
                            im += x[j] * up * Math.Sin(angle);
                        }
                        // Correctly rounded to the ε grid: within half an ε of the exact value (ties go either way).
                        double halfUlp = Eps * up * 0.5 * (1 + 1e-9);   // ε·up first: ε/2 itself rounds to 0
                        Check.True(Math.Abs(got[k].Real * up - re) <= halfUlp && Math.Abs(got[k].Imaginary * up - im) <= halfUlp,
                            $"n={n} trial={trial} bin {k}: expected ({re * down:R}, {im * down:R}), got ({got[k].Real:R}, {got[k].Imaginary:R})");
                    }
                }
        }

        // ---------------------------------------------------------------- sizes past what can be built

        public void PdfWriter_FontSizePastTenThousand_Throws()
        {
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(fontSize: 10001)).ParamName);
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(fontSize: 1e300)).ParamName);
            var pdf = new PdfWriter(fontSize: 10000);   // the bound itself is allowed
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => pdf.Heading("h", size: 1e300)).ParamName);
            // The default heading size is 1.5 × the font size: 15,000 points.
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => pdf.Heading("h")).ParamName);
            var small = new PdfWriter();
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => small.Code("x", 1e5)).ParamName);
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(
                () => small.Table(new[] { "a" }, new[] { new[] { "1" } }, fontSize: 1e300)).ParamName);
        }

        public void DirectoryUtils_RetryDelayOutOfRange_ThrowsBeforeTouchingFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r56-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "keep.txt");
            File.WriteAllText(file, "x");
            try
            {
                foreach (var delay in new[] { TimeSpan.FromMilliseconds(-1), TimeSpan.FromDays(30), TimeSpan.MaxValue, TimeSpan.MinValue })
                {
                    Check.Equal("retryDelay", Check.Throws<ArgumentOutOfRangeException>(() => DirectoryUtils.Delete(dir, retryDelay: delay)).ParamName);
                    Check.True(File.Exists(file), $"Delete with {delay} removed the file");
                    Check.Equal("retryDelay", Check.Throws<ArgumentOutOfRangeException>(() => DirectoryUtils.EnsureEmpty(dir, retryDelay: delay)).ParamName);
                    Check.True(File.Exists(file), $"EnsureEmpty with {delay} removed the file");
                }
                Check.True(DirectoryUtils.Delete(dir, retryDelay: TimeSpan.Zero), "a zero delay is allowed");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        public void TextAlign_WidthPastLongestString_Throws()
        {
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => TextAlign.PadLeft("a", int.MaxValue)).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => TextAlign.PadRight("a", int.MaxValue - 1)).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => TextAlign.Center("a", int.MaxValue)).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => TextAlign.Fit("abc", 0x3FFFFFE0)).ParamName);
        }

        public void SizeChecks_PastArrayMaxLength_ThrowArgumentOutOfRange()
        {
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new DisjointSet(int.MaxValue)).ParamName, "DisjointSet");
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new DisjointSet(ArrayMaxLength + 1)).ParamName, "DisjointSet");
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new FenwickTree(int.MaxValue - 1)).ParamName, "FenwickTree");
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new FenwickTree(ArrayMaxLength)).ParamName, "FenwickTree");
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new NanoId(new Random(1)).New(int.MaxValue)).ParamName, "NanoId");
            Check.Equal("resamples", Check.Throws<ArgumentOutOfRangeException>(
                () => Bootstrap.PercentileInterval(new[] { 1.0 }, s => s[0], new Random(1), int.MaxValue)).ParamName, "Bootstrap");
            Check.Equal("binCount", Check.Throws<ArgumentOutOfRangeException>(() => new NumHistogram(0, 1, int.MaxValue)).ParamName, "Histogram");
            Check.Equal("n", Check.Throws<ArgumentOutOfRangeException>(() => LinearAlgebra.Identity(46341)).ParamName, "Identity");
            Check.Equal("n", Check.Throws<ArgumentOutOfRangeException>(() => LinearAlgebra.Identity(1 << 30)).ParamName, "Identity 2^30");
            Check.Equal("windowSize", Check.Throws<ArgumentOutOfRangeException>(() => new MovingAverage(int.MaxValue)).ParamName, "MovingAverage");
            Check.Equal("length", Check.Throws<ArgumentOutOfRangeException>(() => Window.Create(WindowType.Hann, int.MaxValue)).ParamName, "Window");
            Check.Equal("newLength", Check.Throws<ArgumentOutOfRangeException>(() => Resample.Linear(new[] { 1.0 }, int.MaxValue)).ParamName, "Resample");
            Check.Equal("bins", Check.Throws<ArgumentOutOfRangeException>(() => VisHistogram.Bin(new[] { 1.0 }, int.MaxValue - 1)).ParamName, "Bin");
            Check.Equal("bins", Check.Throws<ArgumentOutOfRangeException>(() => VisHistogram.Bin(new[] { 1.0 }, ArrayMaxLength)).ParamName, "Bin");
        }

        public void Histogram_RenderMoreBinsThanPixels_Throws()
        {
            // 100 px wide, margin 10: 80 pixel columns.
            Check.Equal("bins", Check.Throws<ArgumentOutOfRangeException>(() => VisHistogram.Render(100, 100, new[] { 1.0, 2, 3 }, 81)).ParamName);
            Check.Equal("bins", Check.Throws<ArgumentOutOfRangeException>(() => VisHistogram.Render(100, 100, new[] { 1.0, 2, 3 }, int.MaxValue - 1)).ParamName);
            Check.NotNull(VisHistogram.Render(100, 100, new[] { 1.0, 2, 3 }, 80));
        }

        // ---------------------------------------------------------------- the ParamName sweep

        public void ParamNameSweep_IntervalsAndIo_NameTheArgument()
        {
            Check.Equal("start", Check.Throws<ArgumentException>(() => new Interval<double>(2, 1)).ParamName, "Interval");
            Check.Equal("start", Check.Throws<ArgumentException>(() => new RangeMap<double, int>().Set(2, 1, 0)).ParamName, "RangeMap.Set");
            Check.Equal("start", Check.Throws<ArgumentException>(() => new RangeMap<double, int>().Remove(2, 1)).ParamName, "RangeMap.Remove");
            Check.Equal("decimalSeparator", Check.Throws<ArgumentException>(() => new CsvDialect(',', ',')).ParamName, "CsvDialect");
            Check.Equal("widths", Check.Throws<ArgumentException>(() => FixedWidth.Format(new[] { "a" }, new[] { 1, 2 })).ParamName, "FixedWidth");
        }

        public void ParamNameSweep_Numerics_NameTheArgument()
        {
            Check.Equal("y", Check.Throws<ArgumentException>(() => new AkimaSpline(new[] { 0.0, 1 }, new[] { 0.0 })).ParamName, "Akima lengths");
            Check.Equal("x", Check.Throws<ArgumentException>(() => new AkimaSpline(new[] { 0.0, double.PositiveInfinity }, new[] { 0.0, 1 })).ParamName, "Akima x");
            Check.Equal("y", Check.Throws<ArgumentException>(() => new AkimaSpline(new[] { 0.0, 1 }, new[] { 0.0, double.NaN })).ParamName, "Akima y");
            Check.Equal("y", Check.Throws<ArgumentException>(() => new MonotoneCubic(new[] { 0.0, 1 }, new[] { 0.0 })).ParamName, "MonotoneCubic");
            Check.Equal("b", Check.Throws<ArgumentException>(() => LinearAlgebra.Multiply(new double[2, 3], new double[2, 3])).ParamName, "Multiply");
            Check.Equal("x", Check.Throws<ArgumentException>(() => LinearAlgebra.Multiply(new double[2, 3], new double[2])).ParamName, "Multiply vector");
            Check.Equal("b", Check.Throws<ArgumentException>(() => LinearAlgebra.Solve(new double[,] { { 1, 0 }, { 0, 1 } }, new double[3])).ParamName, "Solve");
            Check.Equal("y", Check.Throws<ArgumentException>(() => Polynomial.Fit(new[] { 0.0, 1 }, new[] { 0.0, 1, 2 }, 1)).ParamName, "Fit lengths");
            Check.Equal("x", Check.Throws<ArgumentException>(() => Polynomial.Fit(new[] { 0.0, 1 }, new[] { 0.0, 1 }, 2)).ParamName, "Fit degree");
            Check.Equal("toUnit", Check.Throws<ArgumentException>(() => UnitConvert.Convert(1, "m", "kg")).ParamName, "UnitConvert");
            Check.Equal("y", Check.Throws<ArgumentException>(() => Integration.Trapezoid(new[] { 0.0, 1 }, new[] { 0.0 })).ParamName, "Trapezoid");
            Check.Equal("y", Check.Throws<ArgumentException>(() => Differentiation.Sample(new[] { 0.0, 1, 2 }, new[] { 0.0, 1 })).ParamName, "Sample");
        }

        private sealed class Holder
        {
            public string Name { get; set; } = "n";
            public List<int> Items { get; } = new List<int> { 1 };
        }

        public void ParamNameSweep_PropertyPath_NamesPath()
        {
            var h = new Holder();
            Check.Equal("path", Check.Throws<ArgumentException>(() => PropertyPath.Get(h, "Nope")).ParamName, "missing property");
            Check.Equal("path", Check.Throws<ArgumentException>(() => PropertyPath.Get(h, "Items[")).ParamName, "unclosed bracket");
            Check.Equal("path", Check.Throws<ArgumentException>(() => PropertyPath.Get(h, "Items[x]")).ParamName, "bad index");
            Check.Equal("path", Check.Throws<ArgumentException>(() => PropertyPath.Get(h, "Name[0]")).ParamName, "not a list");
            Check.Equal("path", Check.Throws<ArgumentException>(() => PropertyPath.Set(h, "Items", null)).ParamName, "read-only");
        }

        public void ParamNameSweep_Charts_NameTheArgument()
        {
            var series = new[] { new Series(new[] { 1.0, 2 }, Rgba.Black) };
            Check.Equal("options", Check.Throws<ArgumentException>(() => LinePlot.Render(20, 20, series)).ParamName, "LinePlot");
            Check.Equal("options", Check.Throws<ArgumentException>(() => BoxPlot.Render(20, 20, new[] { new[] { 1.0, 2 } })).ParamName, "BoxPlot");
            Check.Equal("options", Check.Throws<ArgumentException>(() => VisHistogram.Render(20, 20, new[] { 1.0, 2 }, 1)).ParamName, "Histogram");
            Check.Equal("x", Check.Throws<ArgumentException>(() => new Series(new[] { 1.0, 2 }, Rgba.Black, x: new[] { 1.0 })).ParamName, "Series");
            Check.Equal("columns", Check.Throws<ArgumentException>(
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, new IReadOnlyList<double>[] { new[] { 1.0 } })).ParamName, "ScatterMatrix count");
            Check.Equal("columns", Check.Throws<ArgumentException>(
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, new IReadOnlyList<double>[] { new[] { 1.0, 2 }, new[] { 1.0 } })).ParamName, "ScatterMatrix lengths");
            Check.Equal("options", Check.Throws<ArgumentException>(
                () => ScatterMatrix.RenderSvg(new[] { "a" }, new IReadOnlyList<double>[] { new[] { 1.0, 2 } }, new ScatterMatrixOptions { Groups = new[] { "g" } })).ParamName, "ScatterMatrix groups");
            Check.Equal("period", Check.Throws<ArgumentException>(() => TimingSignal.Clock("clk", 1e-9, 0, 1)).ParamName, "Clock cycles");
            Check.Equal("start", Check.Throws<ArgumentException>(() => TimingSignal.Clock("clk", 1, double.NaN, 1)).ParamName, "Clock start");
            Check.Equal("end", Check.Throws<ArgumentException>(() => TimingSignal.Clock("clk", 1, 0, double.PositiveInfinity)).ParamName, "Clock end");
            Check.Equal("highThreshold", Check.Throws<ArgumentException>(() => TimingSignal.FromSamples("s", new[] { 1.0 }, 1, 2, 1)).ParamName, "FromSamples");
            Check.Equal("svg", Check.Throws<ArgumentException>(() => new ReportBuilder("t").Figure("c")).ParamName, "ReportBuilder.Figure");
        }
    }
}
