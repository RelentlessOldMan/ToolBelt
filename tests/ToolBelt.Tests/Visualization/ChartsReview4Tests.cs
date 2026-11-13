using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 62 (charts, fourth pass): each test failed before its fix.
    public sealed class ChartsReview4Tests
    {
        private static int Drawn(ImageBuffer img)
        {
            int n = 0;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.GetPixel(x, y) != Rgba.White) n++;
            return n;
        }

        private static int DrawnRows(ImageBuffer img, Rgba background)
        {
            int rows = 0;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.GetPixel(x, y) != background) { rows++; break; }
            return rows;
        }

        private static Series Line(params double[] y) => new Series(y, Rgba.Black);

        // The two-tick step overflowed to ∞: the search walked all 2³¹ candidates (about 4 minutes), then threw
        // IndexOutOfRangeException. SvgChart and TimingDiagram (a narrow Width) reached it the same way.
        public void AxisTicks_TwoTicksAcrossTheWholeDoubleRange()
        {
            TickSet t = AxisTicks.Linear(-double.MaxValue, double.MaxValue, 2);
            Check.True(t.Count >= 2 && t.Values.All(v => !double.IsInfinity(v)), $"{t.Count} ticks");
            t = AxisTicks.Linear(-1e308, 1e308, 2);
            Check.True(t.Count >= 2 && t.Count <= 3, $"{t.Count} ticks");
            string svg = SvgChart.Render(new[] { Line(1, 2, 3) },
                new SvgChartOptions { XMin = -double.MaxValue, XMax = double.MaxValue, MaxXTicks = 2 });
            Check.True(svg.Contains("</svg>"));
            svg = TimingDiagram.RenderSvg(new[] { TimingSignal.Digital("a", new (double, bool?)[] { (0, true) }) },
                new TimingDiagramOptions { Start = -double.MaxValue, End = double.MaxValue, Width = 250 });
            Check.True(svg.Contains("</svg>"));
        }

        // plotW / 90 ticks passed AxisTicks' 10,000 cap and failed naming "maxTicks".
        public void TimingDiagram_WideWidthsRender()
        {
            var sig = new[] { TimingSignal.Digital("a", new (double, bool?)[] { (0, true), (1, false) }) };
            foreach (double w in new[] { 9.5e5, 1e6 })
                Check.True(TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { Width = w }).Contains("</svg>"), $"width {w}");
        }

        // Each unknown segment drew (width + LaneHeight) / 6 hatch lines: 30 narrow slivers in a 1e6 px lane wrote about
        // 220 million characters.
        public void TimingDiagram_HatchOfNarrowUnknownSegmentsIsBounded()
        {
            var changes = new List<(double, string?)>();
            for (int i = 0; i < 30; i++) { changes.Add((2 * i, null)); changes.Add((2 * i + 1, "A")); }
            string svg = TimingDiagram.RenderSvg(new[] { TimingSignal.Bus("bus", changes) }, new TimingDiagramOptions { LaneHeight = 1e6 });
            Check.True(svg.Length < 10_000_000, $"{svg.Length} characters");
            Check.True(svg.Contains("stroke=\"#c8c8c8\""), "the hatch is still drawn");
        }

        // An underflowed bin width made the index guess far off, and the settle walk took up to `bins` steps per value.
        public void Histogram_TinyRangeWithManyBinsIsNotQuadratic()
        {
            var data = Enumerable.Repeat(double.Epsilon, 20_000).ToArray();
            var sw = Stopwatch.StartNew();
            var (edges, counts) = Histogram.Bin(data, 1_000_000, 0, double.Epsilon);
            sw.Stop();
            Check.True(sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds} ms");
            Check.Equal(20_000, counts.Sum());
            Check.Equal(20_000, counts[counts.Length - 1]);
            // Graded against the walk it replaced on a random spread over the same tiny range.
            var rng = new Random(62);
            var spread = Enumerable.Range(0, 2000).Select(_ => rng.Next(0, 3) * double.Epsilon).ToArray();
            var (e2, c2) = Histogram.Bin(spread, 1000, 0, 2 * double.Epsilon);
            var naive = new int[1000];
            foreach (double v in spread)
            {
                int k = 999;
                while (k > 0 && v < e2[k]) k--;
                naive[k]++;
            }
            Check.Equal(string.Join(",", naive), string.Join(",", c2));
        }

        // Opening past one limit at ±MaxValue overflowed to ∞, and valid input failed in AxisTicks, naming "min".
        public void Charts_OneLimitAtMaxValueRenders()
        {
            var s = new[] { Line(1, 2, 3) };
            Check.True(SvgChart.Render(s, new SvgChartOptions { XMin = double.MaxValue }).Contains("</svg>"));
            Check.True(SvgChart.Render(s, new SvgChartOptions { XMax = -double.MaxValue }).Contains("</svg>"));
            Check.True(SvgChart.Render(s, new SvgChartOptions { XAxis = AxisKind.Log, XMin = 1e308 }).Contains("</svg>"));
            var pts = new[] { (1.0, 1.0), (2.0, 2.0) };
            Check.True(HexBin.RenderSvg(pts, new HexBinOptions { XMin = 1.7e308 }).Contains("</svg>"));
            Check.True(HexBin.RenderSvg(pts, new HexBinOptions { XMin = double.MaxValue }).Contains("</svg>"));
            var grid = new double[,] { { 0, 1, 2 }, { 1, 2, 3 } };
            Check.True(Waterfall.RenderSvg(grid, new WaterfallOptions { ValueMin = double.MaxValue }).Contains("</svg>"));
            Check.True(Waterfall.RenderSvg(grid, new WaterfallOptions { ValueMax = -double.MaxValue }).Contains("</svg>"));
        }

        // Both factories failed in the constructor, naming "changes", which they don't have.
        public void TimingSignal_FactoriesNameTheirOwnArguments()
        {
            Check.Equal("period", Check.Throws<ArgumentException>(() => TimingSignal.Clock("c", 1.5e308, 1.5e308, double.MaxValue)).ParamName);
            Check.Equal("sampleRate", Check.Throws<ArgumentOutOfRangeException>(
                () => TimingSignal.FromSamples("s", new double[] { 0, 1 }, double.Epsilon, 0.25, 0.75)).ParamName);
            Check.Equal("startTime", Check.Throws<ArgumentOutOfRangeException>(
                () => TimingSignal.FromSamples("s", new double[] { 0, 1 }, 1, 0.25, 0.75, double.NaN)).ParamName);
        }

        // Finite panels summed past MaxValue (failing in SvgUtils as "value"), and an infinite panel failed as "width".
        public void MultiPanel_OverflowingFigureNamesOptions()
        {
            var panels = new[] { new ChartPanel(new[] { Line(1, 2) }), new ChartPanel(new[] { Line(2, 1) }) };
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                () => MultiPanel.RenderSvg(panels, new MultiPanelOptions { Columns = 2, PanelWidth = 1e308 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                () => MultiPanel.RenderSvg(panels, new MultiPanelOptions { PanelWidth = double.PositiveInfinity })).ParamName);
        }

        // A series whose x list shrank after construction indexed past the end mid-render (List's "index").
        public void Series_LengthChangedAfterConstructionIsRejected()
        {
            var x = new List<double> { 0, 1, 2 };
            var s = new Series(new double[] { 1, 2, 3 }, Rgba.Black, PlotStyle.Line, x);
            x.RemoveAt(2);
            Check.Equal("series", Check.Throws<ArgumentException>(() => SvgChart.Render(new[] { s })).ParamName);
            Check.Equal("series", Check.Throws<ArgumentException>(() => LinePlot.Render(100, 100, new[] { s })).ParamName);
        }

        // Pixel coordinates beyond the int range threw OverflowException in the checked build (where this test failed;
        // unchecked, the wrap happened to land the band right).
        public void Annotations_BandOfAFrameFarOutsideTheImage()
        {
            var image = new ImageBuffer(100, 100);
            var frame = new PlotFrame(-1e12, 0, 2e12, 100, 0, 1, 0, 1);
            new Annotations().VerticalBand(0.4, 0.6).Draw(image, frame);
            Check.True(Drawn(image) > 5000, $"{Drawn(image)} pixels");
        }

        // The span overflowed to ∞ and every box collapsed onto the bottom row.
        public void BoxPlot_DataNearMaxValueSpansThePlot()
        {
            var opts = new BoxPlotOptions();
            var img = BoxPlot.Render(200, 200, new[] { new double[] { -1e308, -0.5e308, 0, 0.5e308, 1e308 } }, opts);
            int medianRow = -1, boxTop = int.MaxValue, boxBottom = -1;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    if (img.GetPixel(x, y) == opts.MedianColor) medianRow = y;
                    if (img.GetPixel(x, y) == opts.BoxColor) { boxTop = Math.Min(boxTop, y); boxBottom = Math.Max(boxBottom, y); }
                }
            Check.True(medianRow > 80 && medianRow < 120, $"median on row {medianRow}"); // the median 0 is mid-plot
            Check.True(boxBottom - boxTop > 100, $"box and whiskers over rows {boxTop}..{boxBottom}");
        }

        // y ± e overflowed to ∞ and the chart collapsed to three rows.
        public void ErrorBarChart_HugeErrorsSpanThePlot()
        {
            var opts = new ErrorBarOptions { DrawFrame = false };
            var img = ErrorBarChart.Render(100, 100, new double[] { 0, 1 }, new double[] { 0, 1e308 }, new double[] { 1, 1e308 }, opts);
            Check.True(DrawnRows(img, opts.Background) > 40, $"{DrawnRows(img, opts.Background)} rows");
        }

        // ±0.5 did nothing past 4.5e15: five equal edges, every bin zero wide.
        public void Histogram_ConstantHugeDataHasRealBins()
        {
            foreach (double v in new[] { 1e16, 1e20, double.MaxValue, -double.MaxValue })
            {
                var (edges, counts) = Histogram.Bin(new[] { v, v }, 4);
                for (int i = 1; i < edges.Length; i++) Check.True(edges[i] > edges[i - 1], $"{v}: edge {i}");
                Check.Equal(2, counts.Sum());
            }
        }

        // Explicit min = max = 5 widened to [4.5, 5.5] and counted 4.6 and 5.4.
        public void Histogram_ExplicitEqualBoundsCountOnlyTheValue()
        {
            var (_, counts) = Histogram.Bin(new[] { 4.6, 5, 5.4, 6 }, 2, 5, 5);
            Check.Equal(1, counts.Sum());
        }

        // size − 18 went negative and the histogram cells wrote negative heights.
        public void ScatterMatrix_SmallCellsHaveNoNegativeHeights()
        {
            var cols = new IReadOnlyList<double>[] { new double[] { 1, 2, 3, 4 }, new double[] { 4, 3, 1, 2 } };
            string svg = ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { CellSize = 10 });
            Check.False(svg.Contains("height=\"-"), "negative height");
        }

        // Subnormal columns rounded away in x / n and x / 2: exactly anti-correlated data gave r = −0.667.
        public void ScatterMatrix_PearsonOfSubnormals()
        {
            double e = double.Epsilon;
            Check.Close(-1.0, ScatterMatrix.Pearson(new[] { e, 2 * e, 3 * e, 4 * e }, new[] { 4 * e, 3 * e, 2 * e, e })!.Value, 1e-15);
            // Graded against the same data scaled into the normal range (r does not depend on scale).
            var rng = new Random(62);
            for (int trial = 0; trial < 50; trial++)
            {
                var x = Enumerable.Range(0, 8).Select(_ => (double)rng.Next(1, 1000)).ToArray();
                var y = Enumerable.Range(0, 8).Select(_ => (double)rng.Next(1, 1000)).ToArray();
                double? r = ScatterMatrix.Pearson(x, y);
                double? tiny = ScatterMatrix.Pearson(x.Select(v => v * e).ToArray(), y.Select(v => v * e).ToArray());
                Check.Close(r!.Value, tiny!.Value, 1e-13);
                double? huge = ScatterMatrix.Pearson(x.Select(v => v * 1e305).ToArray(), y.Select(v => v * 1e305).ToArray());
                Check.Close(r.Value, huge!.Value, 1e-13);
            }
        }

        // The halved span of [0, ε] rounded to 0 and every x was 0/0.
        public void TimingDiagram_ChangesOneUlpApart()
        {
            var sig = TimingSignal.Digital("a", new (double, bool?)[] { (0, true), (double.Epsilon, false) });
            Check.True(TimingDiagram.RenderSvg(new[] { sig }).Contains("</svg>"));
        }

        // End < Start was replaced by [Start, Start + 1], and non-finite bounds failed in AxisTicks as "min".
        public void TimingDiagram_ExplicitWindowIsValidated()
        {
            var sig = new[] { TimingSignal.Digital("a", new (double, bool?)[] { (0, true), (1, false) }) };
            Check.Equal("options", Check.Throws<ArgumentException>(
                () => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { Start = 10, End = 0 })).ParamName);
            foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                    () => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { Start = bad })).ParamName);
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                    () => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { End = bad })).ParamName);
            }
        }

        // 1e-16 printed "0 ps" (only 0 should read as zero), and 1e300 printed every one of its 301 digits.
        public void FormatSeconds_ExtremeMagnitudes()
        {
            foreach (double v in new[] { 1e-16, -1e-16, 5e-324 })
            {
                string s = TimingDiagram.FormatSeconds(v);
                Check.True(s.Any(c => c >= '1' && c <= '9'), $"{v}: {s}");
            }
            Check.True(TimingDiagram.FormatSeconds(1e300).Length < 20, TimingDiagram.FormatSeconds(1e300));
            Check.Equal("1.5 ms", TimingDiagram.FormatSeconds(0.0015));
        }

        // −∞ and NaN limits painted every cell one colour.
        public void HeatMap_RejectsNonFiniteLimits()
        {
            var data = new double[,] { { 0, 1 }, { 2, 3 } };
            foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                    () => HeatMap.Render(data, Colormap.Viridis, new HeatMapOptions { Min = bad })).ParamName);
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                    () => HeatMap.Render(data, Colormap.Viridis, new HeatMapOptions { Max = bad })).ParamName);
            }
        }

        // A reversed range drew a [0.5, 9.5] axis; NaN limits and an infinite Width failed as "min" and "width".
        public void HexBin_ValidatesItsLimits()
        {
            var pts = new[] { (1.0, 1.0), (2.0, 2.0) };
            Check.Equal("options", Check.Throws<ArgumentException>(() => HexBin.RenderSvg(pts, new HexBinOptions { XMin = 10, XMax = 0 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => HexBin.RenderSvg(pts, new HexBinOptions { YMin = 10, YMax = 0 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => HexBin.RenderSvg(pts, new HexBinOptions { XMin = double.NaN })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                () => HexBin.RenderSvg(pts, new HexBinOptions { Width = double.PositiveInfinity })).ParamName);
        }

        public void SvgChart_ValidatesItsOptions()
        {
            var s = new[] { Line(1, 2, 3) };
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(s, new SvgChartOptions { XMin = double.NaN })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(s, new SvgChartOptions { YMax = double.PositiveInfinity })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                () => SvgChart.Render(s, new SvgChartOptions { Width = double.PositiveInfinity })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                () => SvgChart.Render(s, new SvgChartOptions { FontSize = double.PositiveInfinity })).ParamName);
        }

        public void Annotations_InfiniteFontSizeIsRejected()
        {
            var frame = new PlotFrame(0, 0, 100, 100, 0, 1, 0, 1);
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(
                () => new Annotations().HorizontalLine(0.5, "a").ToSvg(frame, double.PositiveInfinity)).ParamName);
        }

        // Render's errors named Compute's "whiskerReach" and "data".
        public void BoxPlot_RenderNamesItsOwnArguments()
        {
            var data = new[] { new double[] { 1, 2, 3 } };
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(
                () => BoxPlot.Render(100, 100, data, new BoxPlotOptions { WhiskerReach = double.NaN })).ParamName);
            Check.Equal("datasets", Check.Throws<ArgumentException>(
                () => BoxPlot.Render(100, 100, new IReadOnlyList<double>[] { new double[] { 1 }, null! })).ParamName);
            Check.Equal("datasets", Check.Throws<ArgumentException>(
                () => BoxPlot.Render(100, 100, new IReadOnlyList<double>[] { new[] { double.NaN } })).ParamName);
        }

        // A lone point (one-element series, or one between NaN gaps) drew nothing in Line style.
        public void LinePlot_LonePointsAreDrawn()
        {
            var opts = new PlotOptions { DrawFrame = false };
            Check.True(Drawn(LinePlot.Render(50, 50, new[] { Line(1) }, opts)) > 0, "one point");
            Check.True(Drawn(LinePlot.Render(50, 50, new[] { Line(1, double.NaN, 3, double.NaN, 1) }, opts)) >= 3, "between gaps");
        }
    }
}
