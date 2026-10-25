using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 37 (charts, third pass): each test failed before its fix.
    public sealed class ChartsReview3Tests
    {
        private static int Drawn(ImageBuffer img)
        {
            int n = 0;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.GetPixel(x, y) != Rgba.White) n++;
            return n;
        }

        public void Histogram_DecimalValuesLandInTheirOwnBin()
        {
            Check.Equal("0,0,0,1,0,0,1,1,0,0", string.Join(",", Histogram.Bin(new[] { 0.3, 0.6, 0.7 }, 10, 0, 1).Counts));
            var tenths = Enumerable.Range(0, 11).Select(i => i / 10.0).ToArray();
            Check.Equal("1,1,1,1,1,1,1,1,1,2", string.Join(",", Histogram.Bin(tenths, 10, 0, 1).Counts));
            var (edges, _) = Histogram.Bin(tenths, 10, 0, 1);
            Check.Equal(0.3, edges[3]);
        }

        public void RasterCharts_OneLimitPastTheDataOpensTheAxis()
        {
            var data = new double[] { 0, 1, 2, 3 };
            PlotFrame f = LinePlot.Frame(100, 100, new[] { new Series(data, Rgba.Black, PlotStyle.Scatter) }, new PlotOptions { MinY = 100 });
            Check.True(f.YMin == 100 && f.YMax > 100, $"line y: {f.YMin}..{f.YMax}");
            f = LinePlot.Frame(100, 100, new[] { new Series(data, Rgba.Black, PlotStyle.Scatter) }, new PlotOptions { MaxX = -5 });
            Check.True(f.XMax == -5 && f.XMin < -5, $"line x: {f.XMin}..{f.XMax}");
            f = LinePlot.Frame(100, 100, new Series[0], new PlotOptions { MinX = 5 });
            Check.True(f.XMin == 5 && f.XMax > 5, $"no data: {f.XMin}..{f.XMax}");
            Check.Equal(0, Drawn(LinePlot.Render(100, 100, new[] { new Series(data, Rgba.Black, PlotStyle.Scatter) },
                new PlotOptions { MinY = 100, DrawFrame = false })));

            var ten = Enumerable.Range(0, 11).Select(i => (double)i).ToArray();
            Check.Equal(0, Drawn(BoxPlot.Render(100, 100, new List<IReadOnlyList<double>> { ten }, new BoxPlotOptions { Min = 100, DrawFrame = false })));
            Check.Equal(0, Drawn(BandChart.Render(100, 100, data, data.Select(v => v + 1).ToArray(), null,
                new BandOptions { Min = 100, DrawFrame = false })));
            Check.Equal(0, Drawn(ErrorBarChart.Render(100, 100, data, data, new double[] { 0.1, 0.1, 0.1, 0.1 },
                new ErrorBarOptions { MinY = 100, DrawFrame = false })));
        }

        public void AxisTicks_OneLabelFormatPerAxis()
        {
            Check.Equal("0|5E+6|1E+7|1.5E+7", string.Join("|", AxisTicks.Linear(0, 1.2e7).Labels));
            Check.Equal("0|20|40|60|80|100", string.Join("|", AxisTicks.Linear(0, 100).Labels));
        }

        public void AxisTicks_TimeWithFewTicksAcrossZero()
        {
            TickSet t = AxisTicks.Time(-1, 1, 2);
            Check.Equal(1.0, t.Step);
            Check.Equal("-0:01|0:00|0:01".Split('|').Length, t.Count);
        }

        public void ScatterMatrix_NullGroupIsRejectedClearly()
        {
            var cols = new IReadOnlyList<double>[] { new double[] { 1, 2, 3 }, new double[] { 3, 1, 2 } };
            var e = Check.Throws<ArgumentException>(() => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols,
                new ScatterMatrixOptions { Groups = new string[] { "x", null!, "y" } }));
            Check.Equal("options", e.ParamName);
        }

        public void AxisTicks_SubMillisecondTimeAxis()
        {
            TickSet t = AxisTicks.Time(0.0001, 0.0002, 6, loose: false);
            Check.True(t.Count >= 2, "ticks: " + t.Count);
            Check.True(AxisTicks.Time(0.0001, 0.0002).Step < 0.001, "loose step");
            string svg = SvgChart.Render(new[] { new Series(new[] { 1.0, 2 }, Rgba.Red, x: new[] { 0.0001, 0.0002 }) },
                new SvgChartOptions { XAxis = AxisKind.Time, XMin = 0.0001, XMax = 0.0002 });
            Check.True(svg.Contains("0.0001"), "x labels");
            Check.True(Waterfall.RenderSvg(new double[3, 2], new WaterfallOptions { TimeStep = 0.0001 }).Contains("0.0002"), "waterfall labels");
        }

        public void Histogram_KeepsOneExplicitBoundPastTheData()
        {
            var (edges, counts) = Histogram.Bin(new[] { 4.7, 4.8 }, 2, min: 5);
            Check.Equal(5.0, edges[0]);
            Check.Equal(0, counts.Sum());
            (edges, counts) = Histogram.Bin(new[] { 5.2, 5.3 }, 2, max: 5);
            Check.Equal(5.0, edges[2]);
            Check.Equal(0, counts.Sum());
            (edges, counts) = Histogram.Bin(new[] { 5.0, 5.0 }, 2, min: 5);
            Check.Equal(5.0, edges[0]);
            Check.Equal(2, counts[0]);
        }

        public void SpansOverflowingToInfinityRender()
        {
            Waterfall.RenderSvg(new double[,] { { -1e308, 0 }, { 1, 1e308 } });
            TimingDiagram.RenderSvg(new[] { TimingSignal.Digital("a", new (double, bool?)[] { (-1e308, true), (1e308, false) }) });
            ScatterMatrix.RenderSvg(new[] { "a", "b" }, new IReadOnlyList<double>[] { new[] { -1e308, 0, 1e308 }, new double[] { 1, 2, 3 } });
            var frame = new PlotFrame(0, 0, 100, 100, 0, 1, 0, 1);
            new Annotations().HorizontalLine(0.5, width: 1e308).ToSvg(frame);
        }

        public void RasterChartLimitsAreValidated()
        {
            var data = new double[] { 1, 2, 3 };
            var sets = new List<IReadOnlyList<double>> { new double[] { 1, 2, 3, 4, 50 } };
            foreach (Action a in new Action[]
            {
                () => BoxPlot.Render(100, 100, sets, new BoxPlotOptions { Min = double.NaN }),
                () => BoxPlot.Render(100, 100, sets, new BoxPlotOptions { Max = double.PositiveInfinity }),
                () => BoxPlot.Render(100, 100, sets, new BoxPlotOptions { Min = 100, Max = 0 }),
                () => BandChart.Render(100, 100, data, data, null, new BandOptions { Min = 10, Max = 0 }),
                () => ErrorBarChart.Render(100, 100, data, data, data, new ErrorBarOptions { MinX = 5, MaxX = 0 }),
                () => BandChart.Render(100, 100, data, data, null, new BandOptions { Margin = -5 }),
                () => Histogram.Render(100, 100, data, 4, new HistogramOptions { Margin = -5 }),
                () => LinePlot.Render(100, 100, new[] { new Series(data, Rgba.Black) }, new PlotOptions { Margin = int.MinValue }),
                () => BoxPlot.Render(100, 100, sets, new BoxPlotOptions { Margin = int.MinValue }),
                () => ErrorBarChart.Render(100, 100, data, data, data, new ErrorBarOptions { Margin = -1 }),
            })
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(a).ParamName);
            // Max past the data, alone, opens the axis below it: nothing in range is drawn.
            Check.Equal(0, Drawn(BoxPlot.Render(100, 100, new List<IReadOnlyList<double>> { Enumerable.Range(1, 50).Select(i => (double)i).ToArray() },
                new BoxPlotOptions { Max = -10, DrawFrame = false })));
        }

        public void OptionErrorsNameTheOptions()
        {
            var sig = new[] { TimingSignal.Digital("a", new (double, bool?)[] { (0, true), (1, false) }) };
            var cols = new IReadOnlyList<double>[] { new double[] { 1, 2, 3 }, new double[] { 3, 1, 2 } };
            foreach (Action a in new Action[]
            {
                () => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { Markers = null! }),
                () => Waterfall.RenderSvg(new double[2, 2], new WaterfallOptions { FontFamily = null! }),
                () => MultiPanel.RenderSvg(new[] { new ChartPanel(new Series[0]) }, new MultiPanelOptions { FontFamily = null! }),
            })
                Check.Equal("options", Check.Throws<ArgumentException>(a).ParamName);
            foreach (Action a in new Action[]
            {
                () => TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { LaneGap = 1e308 }),
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { CellSize = double.NaN }),
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { CellSize = -5 }),
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { Gap = double.PositiveInfinity }),
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { PointRadius = double.NaN }),
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { PointOpacity = 5 }),
                () => ScatterMatrix.RenderSvg(new[] { "a", "b" }, cols, new ScatterMatrixOptions { Bins = int.MaxValue }),
                () => HexBin.RenderSvg(new[] { (0.0, 0.0) }, new HexBinOptions { Radius = double.PositiveInfinity }),
                () => HexBin.RenderSvg(new[] { (0.0, 0.0) }, new HexBinOptions { Radius = 1e-300 }),
                () => HexBin.RenderSvg(new[] { (0.0, 0.0) }, new HexBinOptions { Radius = double.NaN }),
                () => Waterfall.RenderSvg(new double[2, 2], new WaterfallOptions { ValueMin = double.NaN }),
                () => Waterfall.RenderSvg(new double[2, 2], new WaterfallOptions { ValueMin = double.PositiveInfinity }),
                () => Waterfall.RenderSvg(new double[2, 3], new WaterfallOptions { FrequencyStep = 1e308 }),
                () => Waterfall.RenderSvg(new double[2, 2], new WaterfallOptions { TimeStep = 1e308 }),
                () => Waterfall.RenderImage(new double[2, 2], new WaterfallOptions { ColorbarWidth = int.MaxValue }),
                () => SvgChart.Render(new[] { new Series(new[] { 1.0, 2 }, Rgba.Red) }, new SvgChartOptions { MaxXTicks = 1 }),
            })
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(a).ParamName);
            Check.True(TimingDiagram.RenderSvg(sig, new TimingDiagramOptions { FormatTime = t => null! }).Contains("<svg"), "null label");
            Check.Throws<ArgumentOutOfRangeException>(() => TimingDiagram.FormatSeconds(double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => TimingDiagram.FormatSeconds(double.PositiveInfinity));
        }

        public void BoxPlot_MinusToPlusInfinityHasNoNaN()
        {
            BoxStats s = BoxPlot.Compute(new[] { double.NegativeInfinity, double.PositiveInfinity });
            foreach (double v in new[] { s.Q1, s.Median, s.Q3, s.LowerWhisker, s.UpperWhisker })
                Check.False(double.IsNaN(v), "NaN statistic");
        }
    }
}
