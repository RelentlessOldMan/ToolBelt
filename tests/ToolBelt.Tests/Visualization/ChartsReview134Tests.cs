using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    /// <summary>Round 134 review regressions (Charts, eighth pass).</summary>
    public sealed class ChartsReview134Tests
    {
        private static readonly Rgba Ink = new Rgba(200, 0, 0);

        private static int Count(ImageBuffer image, Rgba color, int? column = null)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if ((column is null || x == column) && image.GetPixel(x, y) == color) n++;
            return n;
        }

        private static double Attr(string element, string name)
            => double.Parse(Regex.Match(element, " " + name + "=\"([^\"]*)\"").Groups[1].Value, CultureInfo.InvariantCulture);

        // ---------- equal explicit limits ----------

        // XMin == XMax drew every point, those at x = 0 and 10 included, on the centre line.
        public void Svg_EqualLimits_AreRejected()
        {
            var s = new[] { new Series(new[] { 0.0, 1.0 }, Ink, x: new[] { 0.0, 10.0 }) };
            Check.Equal("options", Check.Throws<ArgumentException>(() => SvgChart.Render(s, new SvgChartOptions { XMin = 5, XMax = 5 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => SvgChart.Layout(s, new SvgChartOptions { YMin = 2, YMax = 2 })).ParamName);
            Check.NotNull(SvgChart.Render(s, new SvgChartOptions { XMin = 5, XMax = 6 }));
        }

        public void LinePlot_EqualLimits_AreRejected_ReversedStillDraws()
        {
            var s = new[] { new Series(new[] { 0.0, 1.0 }, Ink, PlotStyle.Scatter, new[] { 0.0, 10.0 }) };
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => LinePlot.Render(100, 100, s, new PlotOptions { MinX = 5, MaxX = 5 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => LinePlot.Frame(100, 100, s, new PlotOptions { MinY = 0, MaxY = 0 })).ParamName);
            Check.True(Count(LinePlot.Render(100, 100, s, new PlotOptions { MinX = 10, MaxX = 0, DrawFrame = false }), Ink) > 0);
        }

        // ---------- box plot fences ----------

        // Q3 − Q1 past double.MaxValue made the fences NaN at reach 0 (0·∞) and ±∞ at a tiny reach: no outliers, and the
        // whiskers ran to ±1.79e308.
        public void BoxPlot_OverflowedIqr_KeepsItsOutliers()
        {
            double[] d = { -1.79e308, -1.7e308, -1.7e308, -1.7e308, 0, 1.7e308, 1.7e308, 1.7e308, 1.79e308 };
            foreach (double k in new[] { 0, 1e-300 })
            {
                var b = BoxPlot.Compute(d, k);
                Check.Equal(2, b.Outliers.Length, $"reach {k}");
                Check.Equal(-1.7e308, b.LowerWhisker);
                Check.Equal(1.7e308, b.UpperWhisker);
            }
            Check.Equal(0, BoxPlot.Compute(d, 1.5).Outliers.Length);

            // Differential: data spread over ±1.79e308 is classified as the same data at unit scale.
            var rnd = new Random(1341);
            for (int trial = 0; trial < 300; trial++)
            {
                var small = Enumerable.Range(0, 9).Select(_ => Math.Round((rnd.NextDouble() * 2 - 1) * 1.79, 2)).ToArray();
                var big = small.Select(v => v * 1e308).ToArray();
                double k = new[] { 0, 0.25, 0.5, 1 }[rnd.Next(4)];
                var a = BoxPlot.Compute(small, k);
                var c = BoxPlot.Compute(big, k);
                Check.Equal(a.Outliers.Length, c.Outliers.Length, $"trial {trial}, reach {k}: {string.Join(", ", small)}");
            }
        }

        // ---------- band chart ----------

        // Sampled once per column, a spike between two column positions (1000 samples on 100 columns) vanished.
        public void Band_SpikeBetweenColumns_ReachesTheEnvelope()
        {
            var lower = new double[1000];
            var upper = Enumerable.Repeat(1.0, 1000).ToArray();
            upper[507] = 100;
            var o = new BandOptions { DrawFrame = false, Margin = 0 };
            var img = BandChart.Render(100, 100, lower, upper, null, o);
            int top = Enumerable.Range(0, 100).First(y => Enumerable.Range(0, 100).Any(x => img.GetPixel(x, y) == o.BandColor));
            Check.True(top <= 1, $"band top row {top}");
        }

        // The centre line to a sample whose pixel overflows (1e300 on a 1e-300 range) was dropped.
        public void Band_CenterLineToAFarSample_IsDrawn()
        {
            var o = new BandOptions { DrawFrame = false, BandColor = Rgba.White, Min = 0, Max = 1e-300 };
            var img = BandChart.Render(100, 100, new[] { 0.0, 0.0 }, new[] { 1e-301, 1e-301 }, new[] { 5e-301, 1e300 }, o);
            Check.True(Count(img, o.CenterColor) > 30, $"{Count(img, o.CenterColor)} centre-line pixels");
            // Both ends far outside, on opposite sides: the segment still crosses the range.
            var img2 = BandChart.Render(100, 100, new[] { 0.0, 0.0 }, new[] { 1e-301, 1e-301 }, new[] { -1e300, 1e300 }, o);
            Check.True(Count(img2, o.CenterColor) > 30, $"{Count(img2, o.CenterColor)} pixels for a crossing segment");
            // Both ends on one side: nothing.
            var img3 = BandChart.Render(100, 100, new[] { 0.0, 0.0 }, new[] { 1e-301, 1e-301 }, new[] { 1e300, 2e300 }, o);
            Check.Equal(0, Count(img3, o.CenterColor));
        }

        // ---------- error bars ----------

        // A whisker whose ends map to ±∞ pixels was dropped, leaving the 3-pixel marker.
        public void ErrorBar_WhiskerPastTheRange_IsDrawn()
        {
            var o = new ErrorBarOptions { MinY = 0, MaxY = 1e-300, DrawFrame = false };
            var img = ErrorBarChart.Render(100, 100, new[] { 0.0, 1.0 }, new[] { 5e-301, 5e-301 }, new[] { 1e300, 1e-301 }, o);
            Check.True(Count(img, o.Color, 12) > 60, $"{Count(img, o.Color, 12)} pixels in the whisker column");
        }

        // ---------- clipping ----------

        // The clip parameter 5e-301 / 1e300 underflowed to 0, so the clipped segment collapsed to its first end.
        public void LinePlot_ClipParameterUnderflow_StillReachesTheEdge()
        {
            foreach (var (y0, y1, max) in new[] { (5e-301, 1e300, 1e-300), (5e-201, 1e200, 1e-200) })
            {
                var img = LinePlot.Render(100, 100, new[] { new Series(new[] { y0, y1 }, Ink) }, new PlotOptions { MinY = 0, MaxY = max, DrawFrame = false });
                Check.True(Count(img, Ink) > 30, $"{y1:R}: {Count(img, Ink)} pixels");
            }
        }

        // ---------- SvgChart margins ----------

        // The left margin never made room for half the first x label: with no y tick labels "-2000000" started off the canvas.
        public void Svg_FirstXLabel_FitsTheLeftMargin()
        {
            var o = new SvgChartOptions { ShowYTickLabels = false, Legend = false };
            var s = new[] { new Series(new[] { 0.0, 1.0 }, Ink, x: new[] { -2000000.0, 0 }) };
            string svg = SvgChart.Render(s, o);
            var labels = Regex.Matches(svg, "<text[^>]*text-anchor=\"middle\"[^>]*>([^<]*)</text>").Cast<Match>()
                .Where(m => m.Groups[1].Value.Contains("2000000") || m.Groups[1].Value.Contains("2e")).ToArray();
            Check.True(labels.Length > 0, "the first x label is drawn");
            foreach (Match m in labels)
            {
                double x = Attr(m.Value, "x"), w = SvgUtils.EstimateTextWidth(m.Groups[1].Value, o.FontSize);
                Check.True(x - w / 2 >= 0, $"label '{m.Groups[1].Value}' starts at {x - w / 2}");
            }
        }

        // ---------- scatter matrix ----------

        // The legend was a fixed 120 px column on a canvas sized for the panels: 30 long group names ran past both edges.
        public void ScatterMatrix_Legend_FitsTheCanvas()
        {
            var groups = Enumerable.Range(0, 30).Select(i => "group-with-a-long-name-" + i).ToArray();
            var a = Enumerable.Range(0, 30).Select(i => (double)i).ToArray();
            string svg = ScatterMatrix.RenderSvg(new[] { "a", "b" }, new[] { a, Enumerable.Reverse(a).ToArray() },
                new ScatterMatrixOptions { CellSize = 60, Groups = groups });
            string head = Regex.Match(svg, "<svg[^>]*>").Value;
            double width = Attr(head, "width"), height = Attr(head, "height");
            foreach (Match m in Regex.Matches(svg, "<text x=\"[^\"]*\" y=\"[^\"]*\">(group-[^<]*)</text>"))
            {
                double x = Attr(m.Value, "x"), y = Attr(m.Value, "y");
                Check.True(y <= height, $"{m.Groups[1].Value} at y {y} on a {height} canvas");
                Check.True(x + SvgUtils.EstimateTextWidth(m.Groups[1].Value, 10) <= width, $"{m.Groups[1].Value} ends past {width}");
            }
        }

        // 0.5·v rounded subnormals (0.5·3ε is 2ε), so {1, 2, 3, 4}·ε binned 1, 0, 1, 2; the 5% pad halved to 0 too.
        public void ScatterMatrix_SubnormalColumn_BinsExactly()
        {
            double e = double.Epsilon;
            string svg = ScatterMatrix.RenderSvg(new[] { "a", "b" }, new[] { new[] { e, 2 * e, 3 * e, 4 * e }, new[] { 4 * e, 3 * e, 2 * e, e } },
                new ScatterMatrixOptions { Bins = 4 });
            var bars = Regex.Matches(svg, "<rect [^>]*fill=\"#7f9fbf\"/>").Cast<Match>().Select(m => Attr(m.Value, "height")).ToArray();
            Check.Equal(8, bars.Length);   // four equal bars in each diagonal panel
            Check.True(bars.All(h => h == bars[0]), string.Join(", ", bars));
            // The pad keeps points off the panel edges.
            foreach (Match m in Regex.Matches(svg, "<circle [^>]*/>"))
            {
                double cx = Attr(m.Value, "cx");
                Check.True(Math.Abs(cx - 56) > 0.5 && Math.Abs(cx - 206) > 0.5, $"circle on a panel edge at {cx}");
            }
        }

        // ---------- colormap range ----------

        // Min and Max went to positions 0 and 1, so a diverging map with stops at −1 … 1 showed only its upper half.
        public void HeatMap_And_Colorbar_UseTheWholeStopRange()
        {
            Rgba blue = new Rgba(0, 0, 255), white = new Rgba(255, 255, 255), red = new Rgba(255, 0, 0);
            var map = new Colormap((-1, blue), (0, white), (1, red));
            var img = HeatMap.Render(new double[,] { { -5, 0, 5 } }, map);
            Check.True(img.GetPixel(0, 0) == blue, "min is blue");
            Check.True(img.GetPixel(1, 0) == white, "middle is white");
            Check.True(img.GetPixel(2, 0) == red, "max is red");
            var inf = HeatMap.Render(new double[,] { { double.NegativeInfinity, 0, 1 } }, map);
            Check.True(inf.GetPixel(0, 0) == blue, "−∞ takes the low end");
            var bar = Colorbar.Render(1, 3, map);
            Check.True(bar.GetPixel(0, 2) == blue && bar.GetPixel(0, 1) == white && bar.GetPixel(0, 0) == red, "colorbar spans the stops");

            // For maps over [0, 1] nothing changes.
            var rnd = new Random(1342);
            foreach (var m in new[] { Colormap.Viridis, new Colormap((0, blue), (1, red)) })
                for (int i = 0; i < 200; i++)
                {
                    double t = rnd.NextDouble();
                    Check.True(m.MapFraction(t) == m.Map(t), $"t {t:R}");
                }
            Check.Equal(-1.0, map.StartPosition);
            Check.Equal(1.0, map.EndPosition);
        }

        // ---------- timing diagram ----------

        public void Clock_ReversedWindow_Throws()
        {
            Check.Equal("end", Check.Throws<ArgumentException>(() => TimingSignal.Clock("c", 1, 10, 0)).ParamName);
            Check.Equal(0, TimingSignal.Clock("c", 1, 5, 5).Changes.Count);
        }

        // A marker near the right edge put its label past the canvas.
        public void Timing_MarkerLabelNearTheRightEdge_StaysOnTheCanvas()
        {
            var sig = TimingSignal.Digital("d", new (double, bool?)[] { (0, false), (1, true) });
            string svg = TimingDiagram.RenderSvg(new[] { sig }, new TimingDiagramOptions
            {
                Width = 300, Start = 0, End = 2, Markers = new[] { new TimingMarker(2, "trigger point reached here") },
            });
            Match m = Regex.Match(svg, "<text [^>]*>trigger point reached here</text>");
            Check.True(m.Success);
            double x = Attr(m.Value, "x"), w = SvgUtils.EstimateTextWidth("trigger point reached here", 11);
            bool end = m.Value.Contains("text-anchor=\"end\"");
            Check.True(end ? x <= 300 && x - w >= 0 : x + w <= 300, $"label at {x} (end: {end})");
        }

        public void Timing_DefaultMarker_NamesOptions()
        {
            var sig = TimingSignal.Clock("c", 1, 0, 4);
            var ex = Check.Throws<ArgumentException>(() => TimingDiagram.RenderSvg(new[] { sig }, new TimingDiagramOptions { Markers = new[] { default(TimingMarker) } }));
            Check.Equal("options", ex.ParamName);
        }

        // Each sub-pixel unknown sliver drew up to 200 hatch lines: 20 000 changes in a tall lane wrote 67 M characters.
        public void Timing_ManyUnknownSlivers_OutputGrowsWithWidthNotLines()
        {
            var changes = Enumerable.Range(0, 20000).Select(i => (i / 20000.0, i % 2 == 0 ? null : "A")).ToArray();
            string svg = TimingDiagram.RenderSvg(new[] { TimingSignal.Bus("b", changes) }, new TimingDiagramOptions { LaneHeight = 1000 });
            Check.True(svg.Length < 12_000_000, $"{svg.Length} characters");
            // A wide unknown segment is still hatched.
            string wide = TimingDiagram.RenderSvg(new[] { TimingSignal.Bus("b", new (double, string?)[] { (0, null), (1, "A") }) },
                new TimingDiagramOptions { Start = 0, End = 2, LaneHeight = 1000 });
            Check.True(Regex.Matches(wide, "M[-0-9.]+ [-0-9.]+ ?L").Count > 50, "hatch lines in a wide unknown segment");
            Check.False(wide.Contains("d=\"\""), "no empty paths");
        }

        // ---------- hex bins ----------

        // Points inside the frame past hexagon index 1e9 were skipped as if far outside it: no cells and no error.
        public void HexBin_TooManyHexagonsAcross_Throws()
        {
            var frame = new PlotFrame(0, 0, 500, 500, 0, 1, 0, 1);
            var pts = new[] { (0.5, 0.5), (0.2, 0.2) };
            Check.Equal("radius", Check.Throws<ArgumentOutOfRangeException>(() => HexBin.Compute(pts, frame, 1e-9)).ParamName);
            Check.Equal(2, HexBin.Compute(pts, frame, 1e-3).Count);
            var line = Enumerable.Range(0, 101).Select(i => ((double)i, (double)i)).ToArray();
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                () => HexBin.RenderSvg(line, new HexBinOptions { Width = 4e7, Height = 400, Radius = 0.01 })).ParamName);
        }

        // ---------- multi-panel ----------

        // An empty panel's placeholder [0, 1] joined the shared range: x in [1000, 2000] beside it shared [0, 2000].
        public void MultiPanel_EmptyPanel_DoesNotWidenTheSharedRange()
        {
            var a = new ChartPanel(new[] { new Series(new[] { 1.0, 2.0, 3.0 }, Ink, x: new[] { 1000.0, 1500, 2000 }) });
            var empty = new ChartPanel(Array.Empty<Series>());
            string shared = MultiPanel.RenderSvg(new[] { a, empty }, new MultiPanelOptions { ShareX = true });
            string alone = SvgChart.Render(a.Series);
            PlotFrame f = SvgChart.Layout(a.Series);
            Check.Equal(1000.0, f.XMin);
            Check.False(Regex.IsMatch(shared, ">0</text>[\\s\\S]*>500</text>"), "no 0 … 500 ticks on the shared axis");
            Check.True(alone.Contains(">1200</text>") == shared.Contains(">1200</text>"), "same ticks as the panel alone");
            // Every panel empty: the placeholder range is used.
            Check.NotNull(MultiPanel.RenderSvg(new[] { empty, empty }, new MultiPanelOptions { ShareX = true, ShareY = true }));
        }
    }
}
