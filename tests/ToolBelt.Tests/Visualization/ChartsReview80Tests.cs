using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 80 (charts, fifth pass): each test failed before its fix.
    public sealed class ChartsReview80Tests
    {
        private static readonly Rgba Blue = new Rgba(0, 0, 255);

        private static string Labels(TickSet t) => string.Join(",", t.Labels);

        private static IEnumerable<XElement> Texts(string svg)
            => XDocument.Parse(svg).Descendants().Where(e => e.Name.LocalName == "text");

        // ---------- AxisTicks ----------

        public void Linear_TightTinyRangeKeepsTheCallersBounds()
        {
            // Widened to ±1e-250 before: non-positive ticks outside the request, on a log axis too.
            TickSet lin = AxisTicks.Linear(1e-300, 2e-300, 6, loose: false);
            Check.Equal(1e-300, lin.Min);
            Check.Equal(2e-300, lin.Max);
            Check.True(lin.Values.All(v => v >= 1e-300 && v <= 2e-300), Labels(lin));

            TickSet log = AxisTicks.Log10(5e-324, 1e-323, loose: false);
            Check.True(log.Count >= 1, "a tight log axis keeps at least one tick");
            Check.True(log.Values.All(v => v > 0 && v >= 5e-324 && v <= 1e-323), Labels(log));

            TickSet time = AxisTicks.Time(0, 1e-300, 6, loose: false);
            Check.Equal(0.0, time.Min);
            Check.Equal(1e-300, time.Max);
        }

        public void Linear_TightStepSearchCountsTicksInsideTheRange()
        {
            // The bracketing count chose step 10 and left no tick at all.
            Check.Equal("4,6", Labels(AxisTicks.Linear(3.4, 6.68, 2, loose: false)));
            Check.Equal("1,2,3,4,5", Labels(AxisTicks.Linear(0.9, 5.1, 6, loose: false)));
            Check.True(AxisTicks.Linear(13, 17, 2, loose: false).Count >= 1);
        }

        public void Linear_ExponentLabelsResolveEveryTick()
        {
            // 15 significant digits printed "1E+0" for five distinct ticks.
            foreach (var (lo, hi) in new[] { (1.0, 1 + 4e-15), (123.0, 123.00000000000003), (0.1, 0.10000000000000014) })
            {
                TickSet t = AxisTicks.Linear(lo, hi);
                Check.True(t.Count >= 2, $"{lo}..{hi}");
                Check.Equal(t.Count, t.Labels.Distinct().Count(), Labels(t));
                // Each label parses back to its tick (to the step's resolution).
                double step = t.Values[1] - t.Values[0];
                for (int i = 0; i < t.Count; i++)
                    Check.True(Math.Abs(double.Parse(t.Labels[i], System.Globalization.CultureInfo.InvariantCulture) - t.Values[i]) <= step * 1e-3, t.Labels[i]);
            }
        }

        // ---------- SvgChart, MultiPanel ----------

        public void SvgChart_LogLimitAndReversedLimitsNameOptions()
        {
            var s = new[] { new Series(new[] { 1.0, 2, 3 }, Blue) };
            // Named "explicitMin", a private helper's parameter.
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(s, new SvgChartOptions { YAxis = AxisKind.Log, YMax = 0 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(s, new SvgChartOptions { XAxis = AxisKind.Log, XMin = -1 })).ParamName);
            // Swapped without a word.
            Check.Equal("options", Check.Throws<ArgumentException>(() => SvgChart.Render(s, new SvgChartOptions { XMin = 5, XMax = 1 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => SvgChart.Render(s, new SvgChartOptions { YMin = 5, YMax = 1 })).ParamName);
        }

        public void MultiPanel_SharedAxisAcrossKindsIsRejected()
        {
            var lin = new ChartPanel(new[] { new Series(new[] { -5.0, 5 }, Blue, x: new[] { -5.0, 5 }) });
            var log = new ChartPanel(new[] { new Series(new[] { 1.0, 10 }, Blue, x: new[] { 1.0, 10 }) }, new SvgChartOptions { XAxis = AxisKind.Log });
            var ex = Check.Throws<ArgumentException>(() => MultiPanel.RenderSvg(new[] { lin, log }, new MultiPanelOptions { ShareX = true }));
            Check.Equal("panels", ex.ParamName);
            MultiPanel.RenderSvg(new[] { lin, log }, new MultiPanelOptions { ShareY = true }); // y is linear in both
        }

        // ---------- LinePlot, Histogram, PlotFrame ----------

        public void LinePlot_NonFiniteLimitsNameOptions()
        {
            var s = new[] { new Series(new[] { 1.0, 2, 3 }, Blue) };
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => LinePlot.Frame(100, 100, s, new PlotOptions { MinX = double.NaN })).ParamName);
            // ∞ fell to the [0, 1] no-data default and hid the data.
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => LinePlot.Frame(100, 100, s, new PlotOptions { MinX = double.PositiveInfinity })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => LinePlot.Render(100, 100, s, new PlotOptions { MaxY = double.NegativeInfinity })).ParamName);
        }

        public void Histogram_RenderNamesOptionsForBadLimits()
        {
            var data = new[] { 1.0, 2, 3 };
            Check.Equal("options", Check.Throws<ArgumentException>(() => Histogram.Render(100, 100, data, 5, new HistogramOptions { Min = 5, Max = 1 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => Histogram.Render(100, 100, data, 5, new HistogramOptions { Min = double.NaN })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => Histogram.Render(100, 100, data, 5, new HistogramOptions { Max = double.PositiveInfinity })).ParamName);
        }

        public void PlotFrame_OverflowingEdgesAreRejected()
        {
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => new PlotFrame(1e308, 0, 1e308, 10, 0, 1, 0, 1)).ParamName);
            Check.Equal("height", Check.Throws<ArgumentOutOfRangeException>(() => new PlotFrame(0, 1e308, 10, 1e308, 0, 1, 0, 1)).ParamName);
            Check.Equal(1.5e308, new PlotFrame(1e308, 0, 0.5e308, 10, 0, 1, 0, 1).Right);
        }

        // ---------- HexBin ----------

        public void HexBin_CellCountDoesNotWrap()
        {
            // Past int.MaxValue points in one hexagon the count wrapped to int.MinValue; 2^31 points take minutes, so
            // the increment is checked directly.
            MethodInfo? bump = typeof(HexBin).GetMethod("Bump", BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(bump);
            Check.Equal(8, (int)bump!.Invoke(null, new object[] { 7 })!);
            var ex = Check.Throws<TargetInvocationException>(() => bump.Invoke(null, new object[] { int.MaxValue }));
            Check.True(ex.InnerException is ArgumentException a && a.ParamName == "points", ex.InnerException?.ToString() ?? "");
        }

        // ---------- Waterfall ----------

        private static double[,] Grid(double v)
        {
            var d = new double[4, 4];
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) d[i, j] = v;
            return d;
        }

        public void Waterfall_InfiniteSizesAndFewTicksNameOptions()
        {
            var d = Grid(1);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderSvg(d, new WaterfallOptions { Width = double.PositiveInfinity })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderSvg(d, new WaterfallOptions { Height = double.PositiveInfinity })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderSvg(d, new WaterfallOptions { FontSize = double.PositiveInfinity })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderSvg(d, new WaterfallOptions { MaxFrequencyTicks = 1 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.Layout(d, new WaterfallOptions { MaxTimeTicks = -5 })).ParamName);
        }

        public void Waterfall_ReversedValueLimitsAreRejected()
        {
            var ex = Check.Throws<ArgumentException>(() => Waterfall.ValueRange(Grid(1), new WaterfallOptions { ValueMin = 5, ValueMax = 1 }));
            Check.Equal("options", ex.ParamName);
            Check.Equal((1.0, 5.0), Waterfall.ValueRange(Grid(1), new WaterfallOptions { ValueMin = 1, ValueMax = 5 }));
        }

        public void Waterfall_FlatColourBarLabelRoundTrips()
        {
            // "G4" printed 1.235E+04, and "-0" on .NET 8 only.
            List<string> flat = Texts(Waterfall.RenderSvg(Grid(12345.678))).Select(t => t.Value).ToList();
            Check.True(flat.Contains("12345.678"), string.Join("|", flat));
            List<string> zero = Texts(Waterfall.RenderSvg(Grid(-0.0))).Select(t => t.Value).ToList();
            Check.False(zero.Contains("-0"), string.Join("|", zero));
            Check.True(zero.Contains("0"), string.Join("|", zero));
        }

        public void Waterfall_StepBelowTheStartsResolutionIsRejected()
        {
            // The axes collapsed to [1e17, 1e17] and were labelled over a 2e16 span the data do not have.
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderSvg(Grid(1), new WaterfallOptions { FrequencyStart = 1e17, TimeStart = 1e17 }));
            Check.Equal("options", ex.ParamName);
            Waterfall.RenderSvg(Grid(1), new WaterfallOptions { FrequencyStart = 1e12, TimeStart = 1e12 });
        }

        // ---------- TimingDiagram ----------

        public void Clock_InfiniteOrUnresolvablePeriodThrows()
        {
            // ∞ returned an empty clock; at 1e15 a 0.01 period kept 800 of 20000 edges.
            Check.Equal("period", Check.Throws<ArgumentOutOfRangeException>(() => TimingSignal.Clock("c", double.PositiveInfinity, 0, 10)).ParamName);
            Check.Equal("period", Check.Throws<ArgumentException>(() => TimingSignal.Clock("c", 0.01, 1e15, 1e15 + 100)).ParamName);
            Check.Equal("period", Check.Throws<ArgumentException>(() => TimingSignal.Clock("c", 1, 1e17, 1e17 + 100)).ParamName);
            Check.Equal(200, TimingSignal.Clock("c", 1, 1e6, 1e6 + 100).Changes.Count);
        }

        public void FromSamples_UnresolvableTimesAndNaNThresholdNameTheirParameter()
        {
            var samples = new[] { 0.0, 1, 0, 1 };
            Check.Equal("sampleRate", Check.Throws<ArgumentOutOfRangeException>(() => TimingSignal.FromSamples("s", samples, double.PositiveInfinity, 0.3, 0.7)).ParamName);
            Check.Equal("sampleRate", Check.Throws<ArgumentOutOfRangeException>(() => TimingSignal.FromSamples("s", samples, 1, 0.3, 0.7, startTime: 1e300)).ParamName);
            // Reported as highThreshold.
            Check.Equal("lowThreshold", Check.Throws<ArgumentOutOfRangeException>(() => TimingSignal.FromSamples("s", samples, 1, double.NaN, 0.7)).ParamName);
            Check.Equal(4, TimingSignal.FromSamples("s", samples, 1, 0.3, 0.7).Changes.Count);
        }

        public void Segments_NaNOrReversedWindowThrows()
        {
            var sig = TimingSignal.Bus("b", new (double, string?)[] { (0, "0"), (1, "1"), (2, "0") });
            Check.Equal("t0", Check.Throws<ArgumentOutOfRangeException>(() => TimingDiagram.Segments(sig, double.NaN, 3)).ParamName);
            Check.Equal("t1", Check.Throws<ArgumentOutOfRangeException>(() => TimingDiagram.Segments(sig, 0, double.NaN)).ParamName);
            Check.Equal("t1", Check.Throws<ArgumentException>(() => TimingDiagram.Segments(sig, 3, 0)).ParamName);
            Check.Equal(0, TimingDiagram.Segments(sig, 1.5, 1.5).Count);
        }

        public void Changes_EqualTimeReplacementDropsANonChange()
        {
            // {0:a, 1:a, 2:c} kept a "change" at t=1 that changed nothing.
            var sig = TimingSignal.Bus("b", new (double, string?)[] { (0, "a"), (1, "b"), (1, "a"), (2, "c") });
            Check.Equal("0:a,2:c", string.Join(",", sig.Changes.Select(c => c.Time + ":" + c.Value)));
            var kept = TimingSignal.Bus("b", new (double, string?)[] { (0, "a"), (1, "b"), (1, "c") });
            Check.Equal("0:a,1:c", string.Join(",", kept.Changes.Select(c => c.Time + ":" + c.Value)));
        }

        // ---------- ScatterMatrix ----------

        public void ScatterMatrix_BadNamesAndPaletteNameTheirParameter()
        {
            var cols = new IReadOnlyList<double>[] { new[] { 1.0, 2, 3 }, new[] { 3.0, 1, 2 } };
            Check.Equal("names", Check.Throws<ArgumentException>(() => ScatterMatrix.RenderSvg(new[] { "a", null! }, cols)).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { Palette = Array.Empty<Rgba>() })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { Palette = null! })).ParamName);
        }

        public void Pearson_LengthMismatchThrows()
        {
            Check.Equal("y", Check.Throws<ArgumentException>(() => ScatterMatrix.Pearson(new[] { 1.0, 2, 3 }, new[] { 1.0, 2, 3, 4 })).ParamName);
        }

        public void ScatterMatrix_SmallNegativeCorrelationPrintsUnsignedZero()
        {
            // r = −0.001 printed "r = -0.00".
            var x = Enumerable.Range(0, 1000).Select(i => (double)i).ToArray();
            double[]? y = null;
            for (int seed = 1; seed < 10000 && y is null; seed++)
            {
                var rng = new Random(seed);
                var cand = x.Select(_ => rng.NextDouble()).ToArray();
                double r = ScatterMatrix.Pearson(x, cand)!.Value;
                if (r < 0 && r > -0.005) y = cand;
            }
            Check.NotNull(y);
            string svg = ScatterMatrix.RenderSvg(new[] { "x", "y" }, new IReadOnlyList<double>[] { x, y! });
            List<string> r2 = Texts(svg).Select(t => t.Value).Where(v => v.StartsWith("r = ", StringComparison.Ordinal)).ToList();
            Check.Equal(2, r2.Count);
            Check.True(r2.All(v => v == "r = 0.00"), string.Join("|", r2));
        }

        public void ScatterMatrix_EdgeTicksAreNotLabelled()
        {
            // −21..−1 pads to [−22, 0] and 1..21 to [0, 22]: two "0" labels 8 px apart at the shared panel edge.
            var a = Enumerable.Range(1, 21).Select(i => -(double)i).ToArray();
            var b = Enumerable.Range(1, 21).Select(i => (double)i).ToArray();
            string svg = ScatterMatrix.RenderSvg(new[] { "a", "b" }, new IReadOnlyList<double>[] { a, b });
            List<string> labels = Texts(svg).Select(t => t.Value).ToList();
            Check.False(labels.Contains("0"), string.Join("|", labels));
            Check.True(labels.Contains("-10") && labels.Contains("10"), string.Join("|", labels));
        }

        // ---------- Annotations, BoxPlot, BandChart ----------

        public void Callout_LabelStaysInsideThePlotAfterTheClamp()
        {
            var f = new PlotFrame(0, 0, 200, 200, 0, 200, 0, 200);
            const string text = "a fairly long callout label";
            double w = SvgUtils.EstimateTextWidth(text, 11);
            Check.True(w > 101 && w < 190, w.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var (x, dx) in new[] { (190.0, 30.0), (10.0, -30.0) })
            {
                string g = "<g xmlns=\"http://www.w3.org/2000/svg\">" + new Annotations().Callout(x, 100, text, dx, -30).ToSvg(f) + "</g>";
                XElement t = Texts(g).Single();
                double tx = double.Parse(t.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                string anchor = t.Attribute("text-anchor")!.Value;
                double lo = anchor == "end" ? tx - w : tx, hi = anchor == "end" ? tx : tx + w;
                // The flipped anchor ran the label to [−11, 93] or [107, 211].
                Check.True(lo >= 0 && hi <= 200, $"x={x}: {anchor} [{lo}, {hi}]");
            }
        }

        public void BoxPlot_QuartilesStayOrdered()
        {
            // Q1 came out above Q3 and both points were flagged as outliers.
            BoxStats s = BoxPlot.Compute(new[] { -1.0725826332683597E+141, -1.0725826332683595E+141 });
            Check.True(s.Min <= s.Q1 && s.Q1 <= s.Median && s.Median <= s.Q3 && s.Q3 <= s.Max, $"{s.Min:R} {s.Q1:R} {s.Median:R} {s.Q3:R} {s.Max:R}");
            var rng = new Random(80);
            for (int trial = 0; trial < 20000; trial++)
            {
                double baseV = 1 + rng.NextDouble();
                var data = Enumerable.Range(0, 2 + rng.Next(6)).Select(_ => baseV + rng.Next(3) * Math.Pow(2, -52)).ToArray();
                BoxStats b = BoxPlot.Compute(data);
                Check.True(b.Min <= b.Q1 && b.Q1 <= b.Median && b.Median <= b.Q3 && b.Q3 <= b.Max, $"trial {trial}");
            }
        }

        public void BandChart_LoneCenterSampleIsDrawn()
        {
            var o = new BandOptions();
            foreach (var center in new[] { new[] { 0.5 }, new[] { double.NaN, 0.5, double.NaN } })
            {
                int n = center.Length;
                ImageBuffer img = BandChart.Render(40, 40, Enumerable.Repeat(0.0, n).ToArray(), Enumerable.Repeat(1.0, n).ToArray(), center, o);
                int hits = 0;
                for (int y = 0; y < img.Height; y++) for (int x = 0; x < img.Width; x++) if (img.GetPixel(x, y).Equals(o.CenterColor)) hits++;
                Check.True(hits >= 1, $"n={n}: {hits} centre pixels");
            }
        }
    }
}
