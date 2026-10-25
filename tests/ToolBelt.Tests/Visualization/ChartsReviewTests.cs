using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 5 (charts).
    public sealed class ChartsReviewTests
    {
        private static int CountColor(ImageBuffer image, Rgba color)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y) == color) n++;
            return n;
        }

        private static (int Min, int Max) RowsOf(ImageBuffer image, Rgba color)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y) == color) { lo = Math.Min(lo, y); hi = Math.Max(hi, y); }
            return (lo, hi);
        }

        public void LinePlot_NonFiniteAndFarOutOfRangeValuesDoNotThrow()
        {
            var y = new[] { 0, double.NaN, 0.5, double.PositiveInfinity, 1e300, 1, -1e300 };
            foreach (var style in new[] { PlotStyle.Line, PlotStyle.Scatter, PlotStyle.Bar })
            {
                var image = LinePlot.Render(60, 60, new[] { new Series(y, Rgba.Red, style) },
                    new PlotOptions { MinY = 0, MaxY = 1 });
                Check.True(CountColor(image, Rgba.Red) > 0, style.ToString());
            }
        }

        public void BandChart_ValueOutsideExplicitRangeDoesNotThrow()
        {
            var o = new BandOptions { Min = 0, Max = 1 };
            var image = BandChart.Render(60, 60, new double[] { -5, 0, 0 }, new double[] { 1, 7, 1 }, new double[] { 0.5, 9, 0.5 }, o);
            Check.True(CountColor(image, o.BandColor) > 0);
        }

        public void BandChart_NaNDoesNotCollapseTheRange()
        {
            var o = new BandOptions { DrawFrame = false };
            var image = BandChart.Render(60, 60, new double[] { 0, 0, 0, 0 }, new double[] { 1, 1, double.NaN, 1 }, null, o);
            var (lo, hi) = RowsOf(image, o.BandColor);
            Check.True(hi - lo > 20, $"band spans rows {lo}..{hi}");
        }

        public void ErrorBarChart_NaNDoesNotCollapseTheRange()
        {
            var o = new ErrorBarOptions { DrawFrame = false };
            var image = ErrorBarChart.Render(60, 60, new double[] { 0, 1, 2 }, new double[] { 0, double.NaN, 10 }, new double[] { 0, 0, 0 }, o);
            var (lo, hi) = RowsOf(image, o.Color);
            Check.True(hi - lo > 20, $"markers span rows {lo}..{hi}");
        }

        public void HeatMap_InfinityDoesNotBlankTheFiniteCells()
        {
            var image = HeatMap.Render(new double[,] { { 0, double.PositiveInfinity }, { 1, 2 } }, Colormap.Grayscale);
            Check.Equal(Rgba.Black, image.GetPixel(0, 0));
            Check.Equal(Rgba.White, image.GetPixel(1, 1));
            Check.Equal(Rgba.White, image.GetPixel(1, 0));
        }

        public void HeatMap_RejectsMinAboveMax()
            => Check.Throws<ArgumentException>(() => HeatMap.Render(new double[,] { { 1 } }, Colormap.Grayscale, new HeatMapOptions { Min = 2, Max = 1 }));

        public void Histogram_OneInfinityDoesNotDiscardTheFiniteRange()
        {
            var (edges, counts) = Histogram.Bin(new[] { 0, 1, 2, double.PositiveInfinity }, 2);
            Check.Equal(0.0, edges[0]);
            Check.Equal(2.0, edges[2]);
            Check.Equal(3, counts.Sum());
        }

        public void Histogram_RejectsMinAboveMaxAndNonFiniteBounds()
        {
            Check.Throws<ArgumentException>(() => Histogram.Bin(new[] { 1.0 }, 2, 5, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => Histogram.Bin(new[] { 1.0 }, 2, double.NegativeInfinity, 1));
        }

        public void Histogram_ValuesOnEdgesLandInTheBinTheEdgesDescribe()
        {
            foreach (var (lo, hi, bins) in new[] { (0.1, 0.7, 6), (0.0, 1.0, 10), (-3.3, 9.1, 7), (1e-3, 2.9e-3, 19) })
            {
                var (edges, _) = Histogram.Bin(Array.Empty<double>(), bins, lo, hi);
                Check.Equal(hi, edges[bins]);
                for (int k = 0; k <= bins; k++)
                {
                    var (_, counts) = Histogram.Bin(new[] { edges[k] }, bins, lo, hi);
                    Check.Equal(1, counts[Math.Min(k, bins - 1)], $"[{lo},{hi}]/{bins} edge {k}");
                }
            }
        }

        public void BoxPlot_IgnoresNaN()
        {
            var s = BoxPlot.Compute(new[] { 1, 2, double.NaN, 3, 4 });
            Check.Equal(1.0, s.Min);
            Check.Equal(4.0, s.Max);
            Check.Equal(2.5, s.Median);
            Check.Throws<ArgumentException>(() => BoxPlot.Compute(new[] { double.NaN }));
        }

        public void BoxPlot_WhiskersNeverEndInsideTheBox()
        {
            var s = BoxPlot.Compute(new double[] { 0, 0, 0, 0, 0, 0, 100, 100 });
            Check.Equal(25.0, s.Q3);
            Check.True(s.UpperWhisker >= s.Q3, $"upper whisker {s.UpperWhisker} below Q3 {s.Q3}");
            Check.True(s.LowerWhisker <= s.Q1);
        }

        public void BoxPlot_RenderWithValuesOutsideExplicitRangeDoesNotThrow()
        {
            // The whisker and box reach far past the explicit Max: the int cast wrapped and DrawLine overflowed.
            var data = new IReadOnlyList<double>[] { new double[] { 0, 1, 2, 3, 4 }, new double[] { 0, 1, 2, 3, 1e300 } };
            BoxPlot.Render(60, 60, data, new BoxPlotOptions { Min = 0, Max = 1e-290 });
        }

        public void AxisTicks_NarrowSpansFarFromZeroGetDistinctLabels()
        {
            foreach (var (a, b) in new[] { (12345670.0, 12345680.0), (1.0, 1.00001) })
            {
                var t = AxisTicks.Linear(a, b);
                Check.Equal(t.Count, t.Labels.Distinct().Count(), string.Join(" ", t.Labels));
            }
        }

        public void AxisTicks_HugeRangeHasFiniteStepAndLabels()
        {
            var t = AxisTicks.Linear(-1e308, 1e308);
            Check.False(double.IsInfinity(t.Step) || double.IsNaN(t.Step), t.Step.ToString());
            Check.True(t.Count >= 2);
            Check.False(t.Labels.Any(l => l.Contains("NaN") || l.Contains("∞")), string.Join(" ", t.Labels));
            SvgChart.Render(new[] { new Series(new[] { -1e308, 1e308 }, Rgba.Red) });
        }

        public void AxisTicks_TightLogRangeInsideOneDecadeStillHasTicks()
        {
            var t = AxisTicks.Log10(2, 8, loose: false);
            Check.True(t.Count >= 2, $"{t.Count} ticks");
            Check.True(t.Values.All(v => v >= 2 && v <= 8));
        }

        public void Timing_FormatSecondsChoosesUnitAfterRounding()
        {
            Check.Equal("1 s", TimingDiagram.FormatSeconds(0.9999999));
            Check.Equal("1 ms", TimingDiagram.FormatSeconds(0.0009999999));
        }

        public void Timing_EpochScaleTicksGetDistinctLabels()
        {
            double t0 = 1700000000;
            var sig = TimingSignal.Digital("a", new (double, bool?)[] { (t0, false), (t0 + 4e-5, true), (t0 + 1e-4, false) });
            string svg = TimingDiagram.RenderSvg(new[] { sig });
            var labels = Regex.Matches(svg, "text-anchor=\"middle\">([^<]*)</text>").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Check.True(labels.Count >= 2, svg);
            Check.Equal(labels.Count, labels.Distinct().Count(), string.Join(" | ", labels));
        }

        public void Timing_MarkerRejectsNonFiniteTime()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new TimingMarker(double.NaN, "m"));
            Check.Throws<ArgumentOutOfRangeException>(() => new TimingMarker(double.PositiveInfinity, "m"));
        }

        public void SvgChart_AutoRangedBarsAreNotCutToHalfWidth()
        {
            var f = SvgChart.Layout(new[] { new Series(new double[] { 1, 2, 3 }, Rgba.Red, PlotStyle.Bar) });
            Check.True(f.XMin <= -0.4 && f.XMax >= 2.4, $"x range [{f.XMin}, {f.XMax}]");
        }

        public void SvgChart_OneExplicitLimitPastTheDataStillGivesARange()
        {
            var series = new[] { new Series(new double[] { 0, 1, 2, 3 }, Rgba.Red) };
            var f = SvgChart.Layout(series, new SvgChartOptions { XMin = 5 });
            Check.Equal(5.0, f.XMin);
            Check.True(f.XMax > f.XMin, $"[{f.XMin}, {f.XMax}]");
            var g = SvgChart.Layout(series, new SvgChartOptions { YMax = -2 });
            Check.Equal(-2.0, g.YMax);
            Check.True(g.YMax > g.YMin, $"[{g.YMin}, {g.YMax}]");
        }

        public void Annotations_BandWithNonPositiveBoundOnLogAxisIsClampedNotDropped()
        {
            var frame = new PlotFrame(0, 0, 100, 100, 1, 100, 1, 100, logX: true, logY: true);
            string svg = new Annotations().HorizontalBand(-5, 10).VerticalBand(0, 10).ToSvg(frame);
            Check.Equal(2, Regex.Matches(svg, "<rect").Count, svg);
            var image = new ImageBuffer(100, 100, Rgba.White);
            new Annotations().HorizontalBand(-5, 10, fill: Rgba.Red).Draw(image, frame);
            Check.True(image.GetPixel(50, 99) != Rgba.White);
        }

        public void HexBin_PointFarOutsideTheFrameGetsNoCell()
        {
            var frame = new PlotFrame(0, 0, 100, 100, 0, 1, 0, 1);
            var cells = HexBin.Compute(new[] { (0.5, 0.5), (1e300, 0.5), (0.5, -1e300) }, frame, 5);
            Check.Equal(1, cells.Count);
            Check.Equal(1, cells[0].Count);
        }

        public void Waterfall_ValueRangeKeepsTheOneExplicitLimitWithoutData()
        {
            var nan = new double[,] { { double.NaN } };
            Check.Equal((5.0, 6.0), Waterfall.ValueRange(nan, new WaterfallOptions { ValueMin = 5 }));
            Check.Equal((-1.0, 0.0), Waterfall.ValueRange(nan, new WaterfallOptions { ValueMax = 0 }));
        }

        public void Palettes_FromHexRejectsWhitespace()
        {
            Check.Throws<FormatException>(() => Palettes.FromHex(" fffff"));
            Check.Throws<FormatException>(() => Palettes.FromHex("#fffff "));
            Check.Throws<FormatException>(() => Palettes.FromHex("# ff"));
        }

        private static void Fast(Action action, string what)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            action();
            Check.True(sw.Elapsed.TotalSeconds < 3, $"{what} took {sw.Elapsed.TotalSeconds:0.0} s");
        }

        public void Raster_FarOutOfRangeDataIsClippedNotWalked()
        {
            Fast(() => LinePlot.Render(200, 100, new[] { new Series(new[] { 0.5, 1e7, 0.5 }, Rgba.Red) }, new PlotOptions { MinY = 0, MaxY = 1 }), "LinePlot");
            Fast(() => ErrorBarChart.Render(200, 100, new double[] { 0, 1, 2 }, new double[] { 1, 2, 3 }, new double[] { 0.1, 1e8, 0.1 },
                new ErrorBarOptions { MinY = 0, MaxY = 5 }), "ErrorBarChart");
            Fast(() => BoxPlot.Render(200, 100, new IReadOnlyList<double>[] { new[] { double.NaN, 1, 2, 3, 4 } }, new BoxPlotOptions { Min = 0, Max = 5 }), "BoxPlot");
            Fast(() => LinePlot.Render(200, 100, new[] { new Series(new[] { 1.0, double.NaN, 3 }, Rgba.Red, PlotStyle.Bar) }), "LinePlot bars");
        }

        public void ImageBuffer_DrawingFarOffCanvasIsCheapAndSafe()
        {
            var image = new ImageBuffer(50, 50, Rgba.White);
            Fast(() =>
            {
                image.DrawLine(int.MinValue, 10, int.MaxValue, 10, Rgba.Red);
                image.DrawLine(25, int.MinValue, 25, int.MaxValue, Rgba.Red);
                image.DrawRectangle(int.MinValue / 2, int.MinValue / 2, int.MaxValue, int.MaxValue, Rgba.Blue);
                image.DrawRectangle(-1_000_000_000, -1_000_000_000, int.MaxValue, int.MaxValue, Rgba.Green, filled: true);
            }, "DrawLine/DrawRectangle");
            Check.Equal(Rgba.Green, image.GetPixel(0, 0));
            Check.Equal(Rgba.Green, image.GetPixel(49, 49));
            var lines = new ImageBuffer(50, 50, Rgba.White);
            lines.DrawLine(-1000, 10, 1000, 10, Rgba.Red);
            for (int x = 0; x < 50; x++) Check.Equal(Rgba.Red, lines.GetPixel(x, 10));
        }

        public void ImageBuffer_RejectsSizesWhoseBufferWouldOverflow()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new ImageBuffer(65536, 16384));
            Check.Throws<ArgumentOutOfRangeException>(() => HeatMap.Render(new double[64, 256], Colormap.Grayscale, new HeatMapOptions { CellSize = 256 }));
        }

        public void AxisTicks_HugeValuesDoNotWrapTheTickCounter()
        {
            var t = AxisTicks.Time(0, 1e16);
            Check.True(t.Count >= 2);
            var e = AxisTicks.Time(1.7e18, 1.7e18 + 1e9);
            Check.True(e.Count >= 2 && e.Values.All(v => v > 1e18), string.Join(" ", e.Values));
            var f = SvgChart.Layout(new[] { new Series(new[] { 1.0, 2 }, Rgba.Red, x: new[] { 1.7e18, 1.7e18 + 1e9 }) }, new SvgChartOptions { XAxis = AxisKind.Time });
            Check.True(f.XMax > f.XMin && f.XMin > 1e18, $"[{f.XMin}, {f.XMax}]");
            var n = AxisTicks.Linear(1, 1 + 4.4e-16, 1000);
            Check.True(n.Values.All(v => v > 0.5 && v < 2.5), string.Join(" ", n.Values));
        }

        public void AxisTicks_FormatTimeRejectsNegativeDecimals()
            => Check.Equal("decimals", Check.Throws<ArgumentOutOfRangeException>(() => AxisTicks.FormatTime(1.5, 10, decimals: -1)).ParamName);

        public void SvgChart_HugeMaxTicksIsCappedToThePlot()
            => Fast(() => SvgChart.Render(new[] { new Series(new[] { 0.0, 1 }, Rgba.Red) }, new SvgChartOptions { MaxXTicks = 100_000_000, MaxYTicks = int.MaxValue }), "SvgChart");

        public void Timing_RejectsUnusableSizes()
        {
            var sig = new[] { TimingSignal.Digital("a", new (double, bool?)[] { (0, false), (1, true) }) };
            Check.Throws<ArgumentOutOfRangeException>(() => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { Width = 1e17 }));
            Check.Throws<ArgumentOutOfRangeException>(() => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { LaneHeight = double.NaN }));
            Check.Throws<ArgumentOutOfRangeException>(() => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { LaneGap = -1 }));
            var ex = Check.Throws<ArgumentException>(() => TimingSignal.Digital("a", new (double, bool?)[] { (double.NaN, true) }));
            Check.Equal("changes", ex.ParamName);
        }

        public void Annotations_RasterLinesAreClippedBeforeStepping()
        {
            var frame = new PlotFrame(10, 10, 80, 80, 0, 1, 0, 1);
            var image = new ImageBuffer(100, 100, Rgba.White);
            Fast(() => new Annotations().Callout(0.5, 0.5, "x", dx: 1e10).Draw(image, frame), "Callout");
            Fast(() => new Annotations().HorizontalLine(0.5, width: 1e4).Draw(image, frame), "thick line");
        }

        public void Annotations_BandNamesTheNaNArgument()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new Annotations().HorizontalBand(1, double.NaN));
            Check.Equal("y2", ex.ParamName);
            var ey = Check.Throws<ArgumentOutOfRangeException>(() => new Annotations().VerticalBand(double.NaN, 1));
            Check.Equal("x1", ey.ParamName);
        }

        public void SharedPalettesAndSignalChangesCannotBeMutated()
        {
            Check.False(Palettes.OkabeIto is Rgba[]);
            Check.False(Palettes.OkabeItoNoBlack is Rgba[]);
            Check.False(Palettes.Tableau10 is Rgba[]);
            var sig = TimingSignal.Digital("a", new (double, bool?)[] { (0, false), (1, true) });
            Check.False(sig.Changes is List<(double, string?)>);
        }

        public void Options_ErrorsNameTheOptionsParameter()
        {
            var series = new[] { new Series(new[] { 1.0 }, Rgba.Red) };
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(series, new SvgChartOptions { Width = double.NaN })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => SvgChart.Render(series, new SvgChartOptions { FontFamily = null! })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => Waterfall.RenderSvg(new double[,] { { 1 } }, new WaterfallOptions { Colormap = null! })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentException>(() => HexBin.RenderSvg(new[] { (0.0, 0.0) }, new HexBinOptions { Colormap = null! })).ParamName);
            Check.Equal("columns", Check.Throws<ArgumentException>(() => ScatterMatrix.RenderSvg(new[] { "a" }, new IReadOnlyList<double>[] { null! })).ParamName);
        }

        public void InvalidNumericInputsAreRejected()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => BoxPlot.Compute(new[] { 1.0, 2, 3, 100 }, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => Histogram.Bin(new[] { 1.0 }, int.MaxValue));
            Check.Throws<ArgumentException>(() => new Colormap((double.NaN, Rgba.Red), (0, Rgba.Black), (1, Rgba.White)));
            Check.Throws<ArgumentOutOfRangeException>(() => new PlotFrame(0, 0, double.PositiveInfinity, 10, 0, 1, 0, 1));
        }

        public void HugeFiniteRangesStayFinite()
        {
            var (edges, counts) = Histogram.Bin(new[] { -1e308, 0, 1e308 }, 4);
            Check.True(edges.All(e => !double.IsNaN(e) && !double.IsInfinity(e)), string.Join(" ", edges));
            Check.True(counts.SequenceEqual(new[] { 1, 0, 1, 1 }), string.Join(" ", counts));
            var f = new PlotFrame(0, 0, 100, 100, -1e308, 1e308, 0, 1);
            Check.Close(100, f.MapX(1e308), 1e-9);
            Check.Close(50, f.MapX(0), 1e-9);
            Check.Close(1e308, f.UnmapX(100), 1e294);
        }
    }
}
