using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    // Round 101 review regressions (charts, sixth pass): each failed on the code before its fix.
    public sealed class ChartsReview101Tests
    {
        private static readonly Rgba Blue = new Rgba(0, 0, 255);

        // ---- Histogram: edges over a range wider than double.MaxValue ----

        // Against a naive reference: a value goes in the last bin whose lower edge is ≤ it, the maximum in the last bin.
        // i·width overflowed past the middle of ±1.7e308, so edges were +∞ and the maximum landed in a middle bin.
        public void Histogram_EdgesStayFiniteAndMonotoneOverHugeRanges()
        {
            var rng = new Random(10101);
            for (int trial = 0; trial < 300; trial++)
            {
                int bins = rng.Next(1, 40);
                var data = new List<double> { -1.7e308, 1.7e308 };
                for (int i = 0; i < 20; i++) data.Add((rng.NextDouble() * 2 - 1) * 1.7e308);
                var (edges, counts) = Histogram.Bin(data, bins);
                for (int i = 0; i < edges.Length; i++)
                {
                    Check.False(double.IsInfinity(edges[i]) || double.IsNaN(edges[i]), $"trial {trial}: edge {i} is {edges[i]}");
                    if (i > 0) Check.True(edges[i] >= edges[i - 1], $"trial {trial}: edge {i} below edge {i - 1}");
                }
                var expected = new int[bins];
                foreach (double v in data)
                {
                    int b = bins - 1;
                    while (b > 0 && v < edges[b]) b--;
                    expected[b]++;
                }
                Check.Equal(string.Join(",", expected), string.Join(",", counts), $"trial {trial}, {bins} bins");
            }
            Check.Equal("1,1,1", string.Join(",", Histogram.Bin(new[] { -1.7e308, 0, 1.7e308 }, 3).Counts));
        }

        // ---- A value whose v − min overflows against a finite explicit range maps outside the plot, not to ±∞ ----

        public void ErrorBar_AWhiskerFarOutsideTheRangeIsStillDrawn()
        {
            foreach (double err in new[] { 1.7e308, 0.4e308 })   // 0.4e308 is the guard: no overflow
            {
                var img = ErrorBarChart.Render(100, 200, new[] { 0.0 }, new[] { -0.5e308 }, new[] { err },
                    new ErrorBarOptions { MinX = -1, MaxX = 1, MinY = -1e308, MaxY = 0, DrawFrame = false });
                int colored = 0;
                for (int y = 0; y < 200; y++) if (!img.GetPixel(50, y).Equals(Rgba.White)) colored++;
                Check.True(colored > 100, $"err {err}: {colored} pixels in the bar's column");
            }
        }

        public void BoxPlot_AnUpperWhiskerFarAboveMaxReachesTheTop()
        {
            var data = new[] { -0.9e308, -0.8e308, -0.7e308, -0.6e308, 1.7e308 };
            var img = BoxPlot.Render(100, 200, new IReadOnlyList<double>[] { data },
                new BoxPlotOptions { Min = -1e308, Max = 0, WhiskerReach = 1e10, DrawFrame = false });
            int topmost = -1;
            for (int y = 0; y < 200 && topmost < 0; y++) if (!img.GetPixel(50, y).Equals(Rgba.White)) topmost = y;
            Check.True(topmost >= 0 && topmost <= 14, $"topmost drawn pixel at y = {topmost}");
        }

        public void LinePlot_ASegmentToAPointFarOutsideTheRangeIsClipped()
        {
            foreach (double y2 in new[] { 1.7e308, 0.4e308 })
            {
                var img = LinePlot.Render(100, 100, new[] { new Series(new[] { -0.5e308, y2 }, Blue, PlotStyle.Line, new[] { 0.0, 1 }) },
                    new PlotOptions { MinY = -1e308, MaxY = 0, MinX = 0, MaxX = 1, DrawFrame = false });
                int blue = 0;
                for (int y = 0; y < 100; y++) for (int x = 0; x < 100; x++) if (img.GetPixel(x, y).Equals(Blue)) blue++;
                Check.True(blue > 20, $"y2 {y2}: {blue} blue pixels");
            }
        }

        public void PlotFrame_MapsAFarValueToAFinitePixel()
        {
            var frame = new PlotFrame(0, 0, 100, 100, 0, 1, -1e308, 0);
            double px = frame.MapY(1.7e308);
            Check.False(double.IsInfinity(px) || double.IsNaN(px), $"MapY = {px}");
            Check.True(px < 0, $"MapY = {px}");
        }

        public void SvgChart_ALineToAFarPointIsNotADot()
        {
            string svg = SvgChart.Render(new[] { new Series(new[] { -0.5e308, 1.7e308 }, Blue, PlotStyle.Line, new[] { 0.0, 1 }) },
                new SvgChartOptions { YMin = -1e308, YMax = 0, XMin = 0, XMax = 1 });
            var m = Regex.Match(svg, "<path d=\"M([-0-9.]+) ([-0-9.]+) L([-0-9.]+) ([-0-9.]+)");
            Check.True(m.Success, svg);
            Check.False(m.Groups[1].Value == m.Groups[3].Value && m.Groups[2].Value == m.Groups[4].Value, m.Value);
        }

        // ---- SvgChart: options ----

        // Bars at 0 and 1.7e308 padded the x axis to ∞, and AxisTicks refused it naming its own "min".
        public void SvgChart_BarsNearMaxValueRender()
        {
            string svg = SvgChart.Render(new[] { new Series(new[] { 1.0, 2 }, Blue, PlotStyle.Bar, new[] { 0.0, 1.7e308 }) });
            Check.True(svg.Contains("<svg"), svg);
            Check.False(svg.Contains("NaN") || svg.Contains("Infinity"), svg);
        }

        public void SvgChart_ANanMarginNamesTheOptions()
        {
            var line = new[] { new Series(new[] { 1.0, 2 }, Blue, PlotStyle.Line) };
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
            {
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(line, new SvgChartOptions { MinMarginLeft = bad })).ParamName);
                Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(line, new SvgChartOptions { MinMarginBottom = bad })).ParamName);
            }
            SvgChart.Render(line, new SvgChartOptions { MinMarginLeft = 40 });   // guard
        }

        // ---- Images past the byte-array cap name the caller's own argument ----

        public void Images_TooLargeForAByteArrayNameTheirOwnArguments()
        {
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() =>
                HeatMap.Render(new double[1, 1], Colormap.Viridis, new HeatMapOptions { CellSize = 50000 })).ParamName);
            Check.Equal("gap", Check.Throws<ArgumentOutOfRangeException>(() =>
                MultiPanel.Compose(new[] { new ImageBuffer(30000, 1) }, 1, gap: 20000)).ParamName);
        }

        // ---- TimingDiagram: an explicit End before every change is kept ----

        public void Timing_AnEndBeforeEveryChangeIsKept()
        {
            var a = TimingSignal.Digital("a", new (double, bool?)[] { (5, true), (10, false) });
            string svg = TimingDiagram.RenderSvg(new[] { a }, new TimingDiagramOptions { End = 0 });
            Check.False(svg.Contains(">5.2 s<"), svg);
            Check.True(svg.Contains(">0<"), svg);
            // Guard: a Start after every change still opens the window after it.
            string after = TimingDiagram.RenderSvg(new[] { a }, new TimingDiagramOptions { Start = 20 });
            Check.True(after.Contains(">20 s<"), after);
        }

        // The unit was chosen with ties-to-even rounding but printed with the format's own: "1000 ps" for 9.999995e-10.
        public void Timing_FormatSecondsNeverPrintsAThousandOfAUnit()
        {
            Check.Equal("1 ns", TimingDiagram.FormatSeconds(9.999995e-10));
            var rng = new Random(10102);
            double[] bounds = { 1e-9, 1e-6, 1e-3, 1 };
            for (int trial = 0; trial < 4000; trial++)
            {
                double b = bounds[rng.Next(bounds.Length)];
                double v = b * (1 - rng.Next(1, 2000) * 1e-7) * (rng.Next(2) == 0 ? 1 : -1);
                string label = TimingDiagram.FormatSeconds(v);
                Check.False(label.StartsWith("1000 ") || label.StartsWith("-1000 "), $"{v:R} -> {label}");
            }
        }
    }
}
