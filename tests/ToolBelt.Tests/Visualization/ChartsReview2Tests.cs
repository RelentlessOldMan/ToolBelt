using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 19 (charts, second pass).
    public sealed class ChartsReview2Tests
    {
        // Runs the action with a watchdog: a hang fails the test instead of stalling the run. Returns what it threw.
        private static Exception? Run(Action action, string what)
        {
            Exception? error = null;
            var task = Task.Run(() => { try { action(); } catch (Exception e) { error = e; } });
            Check.True(task.Wait(TimeSpan.FromSeconds(10)), what + " did not finish");
            return error;
        }

        private static void NoThrow(Action action, string what)
        {
            Exception? e = Run(action, what);
            Check.True(e is null, $"{what} threw {e?.GetType().Name}: {e?.Message}");
        }

        private static void Distinct(IReadOnlyList<double> values, string what)
        {
            Check.True(values.Count >= 2, $"{what}: {values.Count} ticks");
            for (int i = 1; i < values.Count; i++)
                Check.True(values[i] > values[i - 1], $"{what}: ticks {string.Join(", ", values.Select(v => v.ToString("R")))}");
        }

        private static int CountColor(ImageBuffer image, Rgba color)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y) == color) n++;
            return n;
        }

        private static readonly Rgba Ink = new Rgba(1, 2, 3);

        // ---- AxisTicks ----

        public void AxisTicks_SubnormalRangesDoNotHang()
        {
            foreach (var (lo, hi) in new[] { (double.Epsilon, double.Epsilon), (-double.Epsilon, double.Epsilon), (0, 2.5e-323), (0, double.Epsilon) })
            {
                TickSet? t = null;
                NoThrow(() => t = AxisTicks.Linear(lo, hi), $"Linear({lo:R}, {hi:R})");
                Distinct(t!.Values, $"Linear({lo:R}, {hi:R})");
                Check.True(t.Min <= lo && t.Max >= hi, "range covers the data");
            }
        }

        public void AxisTicks_TinyRangesKeepDistinctTicks()
        {
            TickSet a = AxisTicks.Linear(0, 1e-15);
            Distinct(a.Values, "[0, 1e-15]");
            Check.Equal(a.Labels.Count, a.Labels.Distinct().Count(), string.Join(" ", a.Labels));

            TickSet b = AxisTicks.Linear(1.6e-19, 1.7e-19, loose: false);
            Distinct(b.Values, "[1.6e-19, 1.7e-19]");
            Check.True(b.Values[0] == 1.6e-19 && b.Values[b.Count - 1] == 1.7e-19, string.Join(", ", b.Values.Select(v => v.ToString("R"))));
            Check.Equal(b.Labels.Count, b.Labels.Distinct().Count(), string.Join(" ", b.Labels));
        }

        public void AxisTicks_FormatTimeCarriesRoundedSeconds()
        {
            Check.Equal("1:06.000", AxisTicks.FormatTime(65.9996, 120, 3));
            Check.Equal("1:06.000", AxisTicks.FormatTime(65.9999999995, 120, 3));
            Check.Equal("1:00:00.000", AxisTicks.FormatTime(3599.9996, 7200, 3));
            Check.Equal("0:00.000", AxisTicks.FormatTime(-0.0001, 120, 3)); // rounds to zero: no sign
        }

        public void AxisTicks_TimeBeyondLongRangeHasSaneLabels()
        {
            TickSet t = AxisTicks.Time(0, 1e20);
            foreach (string label in t.Labels)
                Check.True(Regex.IsMatch(label, @"^\d+:\d\d:\d\d$"), label);
        }

        public void AxisTicks_Log10AtTheDoubleLimitsStaysFiniteAndPositive()
        {
            TickSet lo = AxisTicks.Log10(5e-324, 1);
            Check.True(lo.Values.All(v => v > 0) && lo.Min > 0, $"Min {lo.Min:R}, first {lo.Values[0]:R}");
            TickSet hi = AxisTicks.Log10(1, 1.7e308);
            Check.True(hi.Values.All(v => !double.IsInfinity(v)) && !double.IsInfinity(hi.Max) && hi.Max >= 1.7e308, $"Max {hi.Max:R}");
            NoThrow(() => SvgChart.Render(new[] { new Series(new[] { 1, 1.7e308 }, Ink) }, new SvgChartOptions { YAxis = AxisKind.Log }), "log chart near MaxValue");
        }

        // ---- Histogram ----

        public void Histogram_SubnormalAndOverflowingRangesBin()
        {
            foreach (double hi in new[] { double.Epsilon, 1e-323 })
            {
                (double[] Edges, int[] Counts) r = default;
                NoThrow(() => r = Histogram.Bin(new[] { 0.0, hi }, 10), $"Bin([0, {hi:R}])");
                Check.Equal(2, r.Counts!.Sum());
            }
            var one = Histogram.Bin(new[] { -1e308, 1e308 }, 1);
            Check.True(one.Edges.All(e => !double.IsNaN(e)), string.Join(", ", one.Edges));
            Check.Equal(2, one.Counts[0]);
        }

        // ---- BoxPlot ----

        public void BoxPlot_InfinitiesAndExtremesGiveNoNaN()
        {
            foreach (var data in new[] { new[] { double.NegativeInfinity, 1, 2, 3 }, new[] { 1, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity },
                                         new[] { double.NegativeInfinity, 1, 2, double.PositiveInfinity } })
            {
                BoxStats s = BoxPlot.Compute(data);
                foreach (double v in new[] { s.Min, s.Q1, s.Median, s.Q3, s.Max, s.LowerWhisker, s.UpperWhisker })
                    Check.False(double.IsNaN(v), $"{string.Join(",", data)}: Q1 {s.Q1} median {s.Median} Q3 {s.Q3} whiskers {s.LowerWhisker}..{s.UpperWhisker}");
            }
            Check.Equal(double.NegativeInfinity, BoxPlot.Compute(new[] { double.NegativeInfinity, 1, 2, 3 }).Q1);
            Check.Equal(double.PositiveInfinity, BoxPlot.Compute(new[] { 1, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity }).Median);
            Check.Equal(0.0, BoxPlot.Compute(new[] { -1e308, 1e308 }).Median);
        }

        // ---- TimingDiagram ----

        public void TimingDiagram_SingleChangeAtLargeTimestampRenders()
        {
            NoThrow(() => TimingDiagram.RenderSvg(new[] { TimingSignal.Digital("a", new (double, bool?)[] { (1e17, true) }) }), "digital at 1e17");
            NoThrow(() => TimingDiagram.RenderSvg(new[] { TimingSignal.Bus("b", new (double, string?)[] { (1.7e18, "A") }) }), "bus at 1.7e18");
        }

        public void TimingDiagram_LaneHeightIsBounded()
        {
            var signals = new[] { TimingSignal.Digital("a", new (double, bool?)[] { (1, true), (2, false) }) };
            foreach (double h in new[] { 1e17, 4, 6 })
            {
                Exception? e = Run(() => TimingDiagram.RenderSvg(signals, new TimingDiagramOptions { LaneHeight = h, Start = 0 }), $"LaneHeight {h}");
                Check.True(e is ArgumentOutOfRangeException, $"LaneHeight {h}: {e?.GetType().Name ?? "no exception"}");
            }
        }

        public void TimingDiagram_NullSignalIsAnArgumentError()
            => Check.Throws<ArgumentException>(() => TimingDiagram.RenderSvg(new TimingSignal[] { null! }));

        // ---- Annotations ----

        public void Annotations_WideLinesFinishAndKeepTheirWidth()
        {
            var image = new ImageBuffer(1000, 1000);
            var frame = new PlotFrame(0, 0, 999, 999, 0, 1, 0, 1);
            NoThrow(() => new Annotations().HorizontalLine(0.5, color: Ink, dash: LineDash.Solid, width: 2000).Draw(image, frame), "solid width 2000");
            Check.Equal(1000 * 1000, CountColor(image, Ink));
            NoThrow(() => new Annotations().VerticalLine(0.5, color: Rgba.Red, width: 2000).Draw(image, frame), "dashed width 2000");

            var small = new ImageBuffer(50, 50);
            new Annotations().HorizontalLine(0.5, color: Ink, dash: LineDash.Solid, width: 1e10).Draw(small, new PlotFrame(0, 0, 49, 49, 0, 1, 0, 1));
            Check.Equal(2500, CountColor(small, Ink)); // the int cast wrapped to a 1-px line
        }

        // ---- Waterfall ----

        public void Waterfall_HugeTickCountsAreCapped()
        {
            var data = new double[,] { { 0, 1 }, { 2, 3 } };
            NoThrow(() => Waterfall.RenderSvg(data, new WaterfallOptions { MaxFrequencyTicks = 100_000_000, MaxTimeTicks = 100_000_000 }), "huge tick counts");
        }

        public void Waterfall_ExplicitValueMinPastTheDataIsKept()
        {
            var (min, max) = Waterfall.ValueRange(new double[,] { { 0, 1 }, { 2, 3 } }, new WaterfallOptions { ValueMin = 5 });
            Check.Equal(5.0, min);
            Check.True(max > 5, $"max {max}");
            var (min2, max2) = Waterfall.ValueRange(new double[,] { { 10, 11 } }, new WaterfallOptions { ValueMax = 5 });
            Check.Equal(5.0, max2);
            Check.True(min2 < 5, $"min {min2}");
        }

        // ---- HexBin ----

        public void HexBin_ExplicitLimitPastTheDataIsHonoured()
        {
            string svg = HexBin.RenderSvg(new[] { (0.0, 0.0), (10.0, 10.0) }, new HexBinOptions { XMin = 100 });
            Check.True(svg.Contains(">100<"), "axis starts at XMin = 100");
            Check.False(svg.Contains(">50<"), "the limit became the axis maximum");
        }

        // ---- BandChart ----

        public void BandChart_FillsBetweenSamples()
        {
            var o = new BandOptions { DrawFrame = false, Min = -1, Max = 2 };
            var image = BandChart.Render(60, 60, new double[] { 0, 0, 0 }, new double[] { 1, 1, 1 }, null, o);
            for (int x = 10; x <= 49; x++)
                Check.True(image.GetPixel(x, 30) == o.BandColor, $"column {x} is not filled");
        }

        public void BandChart_InfiniteBoundsAreRejectedAndHugeSpansMap()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => BandChart.Render(60, 60, new double[] { 0, 0 }, new double[] { 1, 1 }, null, new BandOptions { Max = double.PositiveInfinity }));
            var o = new BandOptions { DrawFrame = false, CenterColor = Ink };
            var image = BandChart.Render(60, 60, new[] { -1e308, -1e308 }, new[] { 1e308, 1e308 }, new double[] { 0, 0 }, o);
            Check.True(image.GetPixel(30, 29) == Ink || image.GetPixel(30, 30) == Ink, "centre line at mid-height");
        }

        // ---- ErrorBarChart ----

        public void ErrorBarChart_InfiniteBoundsAreRejectedAndHugeSpansMap()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => ErrorBarChart.Render(60, 60, new double[] { 0, 1 }, new double[] { 0, 1 }, new double[] { 0, 0 }, new ErrorBarOptions { MinX = double.NegativeInfinity }));
            var o = new ErrorBarOptions { DrawFrame = false, Color = Ink };
            var image = ErrorBarChart.Render(60, 60, new[] { -1e308, 0, 1e308 }, new[] { -1e308, 0, 1e308 }, new double[] { 0, 0, 0 }, o);
            Check.True(image.GetPixel(30, 29) == Ink || image.GetPixel(29, 30) == Ink || image.GetPixel(30, 30) == Ink, "middle point at the centre");
        }

        // ---- HeatMap ----

        public void HeatMap_SingleLimitPastTheDataAndFlatRanges()
        {
            var below = HeatMap.Render(new double[,] { { 0, 1, 2, 3 } }, Colormap.Grayscale, new HeatMapOptions { Min = 5 });
            for (int c = 0; c < 4; c++) Check.Equal(Rgba.Black, below.GetPixel(c, 0));
            var flat = HeatMap.Render(new double[,] { { -1, 0, 1 } }, Colormap.Grayscale, new HeatMapOptions { Min = 0, Max = 0 });
            Check.Equal(Rgba.Black, flat.GetPixel(0, 0));
            Check.Equal(Rgba.White, flat.GetPixel(2, 0));
        }

        public void HeatMap_HugeSpanMapsTheMiddleToTheMiddle()
        {
            var image = HeatMap.Render(new double[,] { { -1e308, 0, 1e308 } }, Colormap.Grayscale);
            Check.Equal(Colormap.Grayscale.Map(0.5), image.GetPixel(1, 0));
        }

        // ---- ScatterMatrix ----

        public void ScatterMatrix_PearsonAtExtremeScales()
        {
            double[] x = { 1, 2, 3, 4 }, y = { 1, 3, 2, 4 };
            foreach (double scale in new[] { 1.0, 1e-90, 1e-200, 1e100, 1e200, 1e307 })
            {
                double? r = ScatterMatrix.Pearson(x.Select(v => v * scale).ToArray(), y.Select(v => v * scale).ToArray());
                Check.True(r.HasValue && Math.Abs(r.Value - 0.8) < 1e-12, $"scale {scale:R}: r = {r}");
            }
            double? same = ScatterMatrix.Pearson(new[] { -1e308, 0, 1e308 }, new[] { -1e308, 0, 1e308 });
            Check.True(same.HasValue && Math.Abs(same.Value - 1) < 1e-12, $"±1e308: r = {same}");
        }

        // ---- LinePlot ----

        public void LinePlot_RenderUsesTheFrameItReturns()
        {
            var cases = new[]
            {
                (new PlotOptions { MinX = 10, MaxX = 0, DrawFrame = false }, new double[] { 0, 10 }),
                (new PlotOptions { DrawFrame = false }, new double[] { -1e308, 1e308 }),
            };
            foreach (var (o, xs) in cases)
            {
                var series = new[] { new Series(new double[] { 0, 1 }, Ink, PlotStyle.Scatter, xs) };
                var image = LinePlot.Render(100, 100, series, o);
                PlotFrame f = LinePlot.Frame(100, 100, series, o);
                for (int i = 0; i < 2; i++)
                {
                    int px = (int)Math.Round(f.MapX(xs[i])), py = (int)Math.Round(f.MapY(i));
                    Check.True(image.GetPixel(px, py) == Ink, $"x = {xs[i]:R}: no point at the frame's ({px}, {py})");
                }
            }
        }

        public void LinePlot_NullSeriesIsAnArgumentError()
            => Check.Throws<ArgumentException>(() => LinePlot.Render(50, 50, new Series[] { null! }));

        // ---- SvgChart ----

        private static string SeriesPath(string svg)
            => Regex.Matches(svg, "<path d=\"([^\"]*)\"[^>]*stroke=\"#010203\"").Cast<Match>().Select(m => m.Groups[1].Value).SingleOrDefault() ?? "";

        public void SvgChart_IsolatedPointBetweenGapsIsDrawn()
        {
            string svg = SvgChart.Render(new[] { new Series(new[] { double.NaN, 5, double.NaN, 1, 2 }, Ink) });
            string d = SeriesPath(svg);
            Check.Equal(2, d.Count(ch => ch == 'M'), d);
        }

        public void SvgChart_SegmentWhoseSpanOverflowsStillClips()
        {
            string? svg = null;
            NoThrow(() => svg = SvgChart.Render(new[] { new Series(new[] { -4e305, 4e305, -4e305 }, Ink) }, new SvgChartOptions { YMin = 0, YMax = 1 }), "overflowing segment");
            Check.True(SeriesPath(svg!).Length > 0, "the segments crossing the plot are drawn");
        }

        // ---- Colormap ----

        public void Colormap_RangeBeyondMaxValueStillInterpolates()
        {
            Rgba c = new Colormap((-1e308, Rgba.Black), (1e308, Rgba.White)).Map(0.9e308);
            Check.Equal((byte)255, c.A);
            Check.True(c.R > 230 && c.R < 250, c.ToString());
        }

        public void Colormap_EqualPositionsKeepTheirOrder()
        {
            var stops = new List<(double, Rgba)>();
            for (int i = 0; i <= 20; i++) if (i != 10) stops.Add((i / 20.0, Rgba.Black));
            stops.Insert(10, (0.5, Rgba.Red));
            stops.Insert(11, (0.5, Rgba.Blue));
            var map = new Colormap(stops.ToArray());
            Check.True(map.Map(0.5 - 1e-9).R > 200, "left of the edge is red");
            Check.True(map.Map(0.5 + 1e-9).B > 200, "right of the edge is blue");
        }

        // ---- MultiPanel ----

        public void MultiPanel_ComposeSizeOverflowIsAnArgumentError()
        {
            var panel = new ImageBuffer(10, 10);
            Check.Throws<ArgumentOutOfRangeException>(() => MultiPanel.Compose(new[] { panel }, 1, int.MaxValue));
            Check.Throws<ArgumentOutOfRangeException>(() => MultiPanel.Compose(new[] { panel, panel }, 2, int.MaxValue / 2));
        }

        public void MultiPanel_InfiniteGapIsRejected()
        {
            var panel = new ChartPanel(new[] { new Series(new double[] { 0, 1 }, Ink) });
            var e = Check.Throws<ArgumentOutOfRangeException>(() => MultiPanel.RenderSvg(new[] { panel }, new MultiPanelOptions { Gap = double.PositiveInfinity }));
            Check.Equal("options", e.ParamName); // it failed later inside the SVG writer, naming "value"
        }
    }
}
