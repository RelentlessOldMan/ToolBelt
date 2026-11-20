using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 167 (charts, tenth pass): each test failed before its fix.
    public sealed class ChartsReview167Tests
    {
        private static readonly Rgba Ink = new Rgba(0, 0, 255);

        // ---------- AxisTicks, tight ----------

        // Ticks of a tight axis: the multiples of the step inside [min, max].
        private static long Inside(double min, double max, double step)
            => (long)Math.Floor(max / step + 1e-9) - (long)Math.Ceiling(min / step - 1e-9) + 1;

        // The smallest 1-2-5 step with at most maxTicks ticks inside the range.
        private static double SmallestFit(double min, double max, int maxTicks)
        {
            for (int e = -12; e <= 12; e++)
                foreach (int d in new[] { 1, 2, 5 })
                {
                    double s = double.Parse(d.ToString(CultureInfo.InvariantCulture) + "E" + e.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    if (Inside(min, max, s) <= maxTicks) return s;
                }
            throw new InvalidOperationException("no step");
        }

        // The search began at the magnitude of span / (maxTicks − 1), so Linear(0.03, 0.13, 2, false) took step 0.1 (one
        // tick) over 0.05; and it stopped once the step passed the range, so Linear(−1, 1, 2, false) kept three ticks.
        public void Linear_TightTakesTheSmallestStepThatFits()
        {
            var t = AxisTicks.Linear(0.03, 0.13, 2, loose: false);
            Check.Equal("0.05,0.10", string.Join(",", t.Labels), "0.03..0.13");
            Check.Equal(1, AxisTicks.Linear(-1, 1, 2, loose: false).Count, "-1..1");
            Check.Equal(1, AxisTicks.Time(-1, 1, 2, loose: false).Count, "time -1..1");
            Check.True(AxisTicks.Linear(0, 0, 2, loose: false).Count <= 2, "flat");

            var rng = new Random(167);
            for (int trial = 0; trial < 20000; trial++)
            {
                double scale = Math.Pow(10, rng.Next(-6, 7));
                double a = (rng.NextDouble() * 2 - 1) * scale, b = (rng.NextDouble() * 2 - 1) * scale;
                if (rng.Next(3) == 0) b = a + rng.NextDouble() * scale * 0.1;
                double min = Math.Min(a, b), max = Math.Max(a, b);
                if (!(max - min > scale * 1e-6)) continue;
                int maxTicks = rng.Next(2, 9);
                TickSet ticks = AxisTicks.Linear(min, max, maxTicks, loose: false);
                string what = "trial " + trial + ": [" + min.ToString("R", CultureInfo.InvariantCulture) + ", " + max.ToString("R", CultureInfo.InvariantCulture) + "] max " + maxTicks;
                Check.True(ticks.Count <= maxTicks, what + " count " + ticks.Count);
                Check.Equal(SmallestFit(min, max, maxTicks), ticks.Step, what);
            }
        }

        // ---------- SvgChart bar padding ----------

        // The auto x range was padded by the closest spacing of any bar, drawn or not: a NaN-y sample 2e-12 from a bar
        // shrank the padding to 1e-12, cutting the end bars to half width.
        public void Svg_BarPadding_CountsOnlyDrawnBars()
        {
            var o = new SvgChartOptions { Legend = false };
            double[] xs = { 0, 2, 4, 6, 8 };
            string plain = SvgChart.Render(new[] { new Series(new[] { 1.0, 1, 1, 1, 1 }, Ink, PlotStyle.Bar, xs) }, o);
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                string withBad = SvgChart.Render(new[] { new Series(new[] { 1.0, bad, 1, 1, 1, 1 }, Ink, PlotStyle.Bar, new[] { 0, 2e-12, 2, 4, 6, 8 }) }, o);
                Check.Equal(plain, withBad, "y = " + bad.ToString(CultureInfo.InvariantCulture));
            }
            var log = new SvgChartOptions { Legend = false, YAxis = AxisKind.Log };
            string logPlain = SvgChart.Render(new[] { new Series(new[] { 1.0, 1, 1, 1, 1 }, Ink, PlotStyle.Bar, xs) }, log);
            string logBad = SvgChart.Render(new[] { new Series(new[] { 1.0, 0, 1, 1, 1, 1 }, Ink, PlotStyle.Bar, new[] { 0, 2e-12, 2, 4, 6, 8 }) }, log);
            Check.Equal(logPlain, logBad, "y = 0 on a log axis");
        }

        // ---------- markers on the edge ----------

        private static void NothingInMargin(ImageBuffer img, int margin, Rgba color, string what)
        {
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    bool inside = x >= margin && x <= img.Width - 1 - margin && y >= margin && y <= img.Height - 1 - margin;
                    if (!inside && img.GetPixel(x, y).Equals(color))
                        Check.True(false, what + ": marker colour at (" + x + ", " + y + ")");
                }
        }

        // A 3×3 marker on the plot edge painted a pixel into the margin on every side.
        public void ErrorBarMarkers_StayInsideThePlot()
        {
            var eb = new ErrorBarOptions { Margin = 10, DrawFrame = false };
            NothingInMargin(ErrorBarChart.Render(60, 60, new double[] { 0, 10 }, new double[] { 0, 10 }, new[] { double.NaN, double.NaN }, eb), 10, eb.Color, "error bars");
        }

        // BoxPlot's outliers had the same 3×3 square.
        public void BoxPlotOutliers_StayInsideThePlot()
        {
            var bp = new BoxPlotOptions { Margin = 10, DrawFrame = false, Min = -1, Max = 10, OutlierColor = new Rgba(1, 2, 3) };
            NothingInMargin(BoxPlot.Render(60, 60, new[] { (IReadOnlyList<double>)new double[] { 0, 0, 0, 0, 0, 0, 10 } }, bp), 10, bp.OutlierColor, "outlier top");
            var bpLow = new BoxPlotOptions { Margin = 10, DrawFrame = false, Min = 0, Max = 11, OutlierColor = new Rgba(1, 2, 3) };
            NothingInMargin(BoxPlot.Render(60, 60, new[] { (IReadOnlyList<double>)new double[] { 0, 10, 10, 10, 10, 10, 10 } }, bpLow), 10, bpLow.OutlierColor, "outlier bottom");
        }

        // ---------- ScatterMatrix ----------

        // Math.Round(r, 2) rounds halves to even, so an exact r = −0.125 read "r = −0.12".
        public void ScatterMatrix_CorrelationHalvesRoundAwayFromZero()
        {
            double[] x = { 3, 5, 3, 5, 5, 5 }, y = { 0, 1, 3, 2, 1, 1 };
            Check.Equal(-0.125, ScatterMatrix.Pearson(x, y)!.Value, "exact r");
            var o = new ScatterMatrixOptions { ShowCorrelation = true };
            string Label(double[] a, double[] b) => XDocument.Parse(ScatterMatrix.RenderSvg(new[] { "x", "y" }, new IReadOnlyList<double>[] { a, b }, o))
                .Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).First(v => v.StartsWith("r = ", StringComparison.Ordinal));
            Check.Equal("r = -0.13", Label(x, y), "negative");
            Check.Equal("r = 0.13", Label(x, y.Select(v => -v).ToArray()), "positive");
        }
        // ---------- AxisTicks, robustness ----------

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        // max / 6 − min / 6 lost a span of one ulp at 1e15 (it came out 0): Log10(0) cast to int.MinValue and the step
        // search walked 2³¹ candidates for minutes, then threw IndexOutOfRangeException. SvgChart's auto axis reached it.
        public void Linear_SpanOfOneUlpAtLargeValues_Returns()
        {
            double a = 1000000000000008.25, b = 1000000000000008.125;
            foreach (bool loose in new[] { true, false })
            {
                TickSet t = AxisTicks.Linear(a, b, 7, loose);
                Check.True(t.Count >= 1 && t.Count <= 7, "linear loose " + loose + ": " + t.Count);
                TickSet u = AxisTicks.Time(a, b, 7, loose);
                Check.True(u.Count >= 1 && u.Count <= 7, "time loose " + loose + ": " + u.Count);
            }
            string svg = SvgChart.Render(new[] { new Series(new[] { b, a }, Ink, PlotStyle.Line) }, new SvgChartOptions { MaxYTicks = 7 });
            Check.True(svg.Length > 0, "svg");
        }

        // A day step past double.MaxValue gave 0 · ∞ = NaN, which passed the ∞-only guard and threw from FormatTime
        // naming "seconds", a parameter Time does not have.
        public void Time_DayStepPastMaxValue_FallsBackToLinear()
        {
            TickSet t = AxisTicks.Time(0, 1.7337163410534425E+308, 2, loose: true);
            Check.True(t.Count >= 2 && t.Values.All(v => !double.IsNaN(v) && !double.IsInfinity(v)), "ticks " + t.Count);
        }

        // From 2^53 s the labels kept four significant digits, so distinct ticks read "5.028e+306s" twice.
        public void Time_LabelsPast2To53Seconds_AreDistinct()
        {
            foreach (var (lo, hi, n) in new[] { (5.026341653157651E+306, 5.031268186430447E+306, 8), (-1E+35, -9.998202101596726E+34, 7) })
                foreach (bool loose in new[] { true, false })
                {
                    TickSet t = AxisTicks.Time(lo, hi, n, loose);
                    Check.Equal(t.Count, t.Labels.Distinct().Count(), R(lo) + " loose " + loose + ": " + string.Join("|", t.Labels));
                }
        }

        // The 1e-9 slack dropped a bracketing tick: Time(−3.5e-8, 8054.7, 10) gave Min 0, above the data.
        public void Time_LooseRange_BracketsTheData()
        {
            var t = AxisTicks.Time(-3.5018828015317594E-08, 8054.722243945455, 10, loose: true);
            Check.True(t.Min <= -3.5018828015317594E-08 && t.Max >= 8054.722243945455, "min " + R(t.Min));
            var rng = new Random(1671);
            for (int trial = 0; trial < 20000; trial++)
            {
                double scale = Math.Pow(10, rng.Next(-9, 300));
                double a = (rng.NextDouble() * 2 - 1) * scale, b = (rng.NextDouble() * 2 - 1) * scale * Math.Pow(10, -rng.Next(0, 12));
                int n = rng.Next(2, 11);
                TickSet u = AxisTicks.Time(a, b, n, loose: true);
                Check.True(u.Min <= Math.Min(a, b) && u.Max >= Math.Max(a, b), "trial " + trial + ": [" + R(a) + ", " + R(b) + "] " + n + " gave [" + R(u.Min) + ", " + R(u.Max) + "]");
            }
        }

        // CountTicks counted the floating quotient, the loop kept the exact decimal tick: 6 ticks for maxTicks 5.
        public void Linear_Tight_NeverExceedsMaxTicks()
        {
            Check.True(AxisTicks.Linear(-1E+100, -1.00000000000001E+100, 5, loose: false).Count <= 5, "-1e100");
            var rng = new Random(1672);
            for (int trial = 0; trial < 20000; trial++)
            {
                double a = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(-300, 300));
                double b = a * (1 + (rng.NextDouble() - 0.5) * Math.Pow(10, -rng.Next(1, 16)));
                int n = rng.Next(2, 9);
                Check.True(AxisTicks.Linear(a, b, n, loose: false).Count <= n, "trial " + trial + ": [" + R(a) + ", " + R(b) + "] " + n);
                Check.True(AxisTicks.Time(a, b, n, loose: false).Count <= n, "time trial " + trial + ": [" + R(a) + ", " + R(b) + "] " + n);
            }
        }

        // A tight range a few ulps wide had no tick inside at any step and came back empty: an unlabelled axis.
        public void Tight_RangeOfAFewUlps_IsLabelled()
        {
            foreach (var (lo, hi, n) in new[] { (253583.40202531937, 253583.40202531934, 6), (1000000000000009.2, 1000000000000009.1, 4) })
            {
                Check.True(AxisTicks.Linear(lo, hi, n, loose: false).Count >= 1, "linear " + R(lo));
                Check.True(AxisTicks.Time(lo, hi, n, loose: false).Count >= 1, "time " + R(lo));
            }
        }

        // Log10 of a subnormal is off by far more than the slack, so the loose axis started a decade below the data.
        public void Log10_SubnormalDecades_AreNotWidened()
        {
            TickSet t = AxisTicks.Log10(1E-320, 1E-319, loose: true);
            Check.Equal("1e-320|1e-319", string.Join("|", t.Labels), "labels");
            Check.Equal(1E-320, t.Min, "min");
        }

        // "R" printed double.Epsilon as "5E-324" on .NET Core and "4.94065645841247E-324" on .NET Framework.
        public void Within_Labels_AreShortestOnEveryFramework()
        {
            Check.Equal("5E-324", string.Join("|", AxisTicks.Log10(double.Epsilon, double.Epsilon, loose: false).Labels), "epsilon");
        }

        // ---------- TimingSignal ----------

        // NaN is neither above nor below the thresholds, so a dropout held the last level and drew as valid.
        public void FromSamples_NaN_IsUnknown()
        {
            var sig = TimingSignal.FromSamples("s", new[] { 0, 5, double.NaN, double.NaN, 0, double.NaN }, 1, 1, 4);
            Check.Equal("0:0|1:1|2:?|4:0|5:?", string.Join("|", sig.Changes.Select(c => c.Time.ToString(CultureInfo.InvariantCulture) + ":" + (c.Value ?? "?"))), "changes");
        }
    }
}
