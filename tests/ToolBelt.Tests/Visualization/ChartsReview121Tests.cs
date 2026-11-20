using System;
using System.Linq;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    /// <summary>Round 121 review regressions (Charts, seventh pass).</summary>
    public sealed class ChartsReview121Tests
    {
        private static readonly Rgba Ink = new Rgba(200, 0, 0);

        // ---------- far points ----------

        // A finite sample whose pixel position overflows (1e307 on [0, 1]) was a gap, so the line to it was dropped: the
        // segment (0,0)–(1e307,1e307) is the diagonal (0,0)–(1,1) inside the ranges.
        public void Svg_LineToAFarPoint_IsDrawnLikeItsClippedSegment()
        {
            var o = new SvgChartOptions { XMin = 0, XMax = 1, YMin = 0, YMax = 1, Legend = false };
            string far = SvgChart.Render(new[] { new Series(new[] { 0.0, 1e307 }, Ink, x: new[] { 0.0, 1e307 }) }, o);
            string near = SvgChart.Render(new[] { new Series(new[] { 0.0, 1.0 }, Ink, x: new[] { 0.0, 1.0 }) }, o);
            Check.Equal(near, far);

            // Far only in y: 1e300 on a 1e-10 range from (0, 0) is a vertical line up the left edge.
            var o2 = new SvgChartOptions { XMin = 0, XMax = 1, YMin = 0, YMax = 1e-10, Legend = false };
            string farY = SvgChart.Render(new[] { new Series(new[] { 0.0, 1e300 }, Ink, x: new[] { 0.0, 1.0 }) }, o2);
            string up = SvgChart.Render(new[] { new Series(new[] { 0.0, 1e-10 }, Ink, x: new[] { 0.0, 1e-20 }) }, o2);
            Check.Equal(up, farY);
        }

        public void Raster_LineToAFarPoint_IsDrawn()
        {
            var opts = new PlotOptions { MinX = 0, MaxX = 1, MinY = 0, MaxY = 1, DrawFrame = false };
            var far = LinePlot.Render(101, 101, new[] { new Series(new[] { 0.0, 1e307 }, Ink, x: new[] { 0.0, 1e307 }) }, opts);
            var near = LinePlot.Render(101, 101, new[] { new Series(new[] { 0.0, 1.0 }, Ink, x: new[] { 0.0, 1.0 }) }, opts);
            int drawn = 0;
            for (int y = 0; y < 101; y++)
                for (int x = 0; x < 101; x++)
                {
                    Check.Equal(near.GetPixel(x, y), far.GetPixel(x, y), $"pixel {x},{y}");
                    if (near.GetPixel(x, y) == Ink) drawn++;
                }
            Check.True(drawn > 50, "the diagonal is drawn: " + drawn);
        }

        // ---------- Blend ----------

        // The destination's alpha was ignored: a band on a transparent canvas came out darkened, and two half-transparent
        // layers kept alpha 128.
        public void Raster_Band_CompositesOverTheDestinationAlpha()
        {
            var frame = new PlotFrame(0, 0, 10, 10, 0, 1, 0, 1);
            var clear = new ImageBuffer(11, 11, Rgba.Transparent);
            new Annotations().HorizontalBand(0, 1).Draw(clear, frame);
            Check.Equal(new Rgba(31, 119, 180, 48), clear.GetPixel(5, 5));

            var red = new ImageBuffer(11, 11, new Rgba(255, 0, 0, 128));
            new Annotations().HorizontalBand(0, 1, fill: new Rgba(0, 0, 255, 128)).Draw(red, frame);
            Check.Equal(new Rgba(85, 0, 170, 192), red.GetPixel(5, 5));

            // An opaque destination is as before: s·a + d·(1 − a).
            var white = new ImageBuffer(11, 11, Rgba.White);
            new Annotations().HorizontalBand(0, 1, fill: new Rgba(0, 0, 0, 51)).Draw(white, frame);
            Check.Equal(new Rgba(204, 204, 204, 255), white.GetPixel(5, 5));
        }

        // ---------- callouts and arrows with huge offsets ----------

        // The leader of a callout 1e200 px to the side was clipped from its far end (x0 + t·dx cancels): nothing was drawn.
        // Since round 178 the tail is held inside the plot, where the SVG layer puts the label, so the leader stops a
        // label's width short of the edge.
        public void Raster_CalloutWithAHugeOffset_DrawsItsLeaderToTheEdge()
        {
            var frame = new PlotFrame(0, 0, 100, 100, 0, 1, 0, 1);
            foreach (double dx in new[] { 1e200, -1e200, 1e17, -1e17 })
            {
                var img = new ImageBuffer(101, 101, Rgba.White);
                new Annotations().Callout(0.5, 0.5, "x", dx: dx, dy: 0, color: Ink).Draw(img, frame);
                int edge = dx > 0 ? 90 : 10, mid = dx > 0 ? 80 : 20;
                Check.Equal(Ink, img.GetPixel(edge, 50), "edge pixel, dx " + dx);
                Check.Equal(Ink, img.GetPixel(mid, 50), "middle pixel, dx " + dx);
                Check.Equal(Rgba.White, img.GetPixel(mid, 40), "off the line, dx " + dx);
            }
        }

        // ---------- bar width ----------

        // Bars that are never drawn (a NaN-y sample 0.01 away, one at y = ∞) set every bar's width.
        public void Svg_BarWidth_CountsOnlyDrawnBars()
        {
            var o = new SvgChartOptions { XMin = -0.5, XMax = 1.5, YMin = 0, YMax = 2, Legend = false };
            string plain = SvgChart.Render(new[] { new Series(new[] { 1.0, 1.0 }, Ink, PlotStyle.Bar, new[] { 0.0, 1.0 }) }, o);
            string nanY = SvgChart.Render(new[] { new Series(new[] { 1.0, 1.0, double.NaN }, Ink, PlotStyle.Bar, new[] { 0.0, 1.0, 1.01 }) }, o);
            string inf = SvgChart.Render(new[] { new Series(new[] { 1.0, 1.0, double.PositiveInfinity }, Ink, PlotStyle.Bar, new[] { 0.0, 1.0, 1.01 }) }, o);
            Check.Equal(plain, nanY);
            Check.Equal(plain, inf);
        }

        // ---------- default frames ----------

        public void DefaultFrame_IsRejected()
        {
            var pts = new[] { (0.5, 0.5) };
            Check.Equal("frame", Check.Throws<ArgumentException>(() => HexBin.Compute(pts, default, 5)).ParamName);
            var a = new Annotations().HorizontalLine(0.5);
            Check.Equal("frame", Check.Throws<ArgumentException>(() => a.ToSvg(default)).ParamName);
            Check.Equal("frame", Check.Throws<ArgumentException>(() => a.Draw(new ImageBuffer(10, 10), default)).ParamName);
        }

        // ---------- colorbar ----------

        // A one-pixel bar showed the top of the map when vertical and the bottom when horizontal.
        public void Colorbar_OnePixel_ShowsTheMiddle()
        {
            Rgba mid = Colormap.Viridis.Map(0.5);
            Check.Equal(mid, Colorbar.Render(3, 1, Colormap.Viridis, vertical: true).GetPixel(1, 0));
            Check.Equal(mid, Colorbar.Render(1, 3, Colormap.Viridis, vertical: false).GetPixel(0, 1));
        }

        // ---------- tick rounding past 2^52 / 10^d ----------

        public void TimeTicks_LargeValues_KeepTheirFraction()
        {
            var t = AxisTicks.Time(1e15, 1e15 + 1, 6, loose: false);
            int i = t.Values.ToList().IndexOf(1e15 + 0.5);
            Check.True(i >= 0, "1e15 + 0.5 is a tick: " + string.Join(", ", t.Values.Select(v => (v - 1e15).ToString("R"))));
            Check.True(t.Labels[i].EndsWith(".5", StringComparison.Ordinal), t.Labels[i]);

            var u = AxisTicks.Time(1e12, 1e12 + 0.001, 6, loose: false);
            int j = u.Values.ToList().IndexOf(1e12 + 0.0005);
            Check.True(j >= 0, "1e12 + 0.0005 is a tick: " + string.Join(", ", u.Values.Select(v => (v - 1e12).ToString("R"))));
            Check.True(u.Labels[j].EndsWith(".0005", StringComparison.Ordinal), u.Labels[j]);

        }

        // ---------- waterfall ----------

        // The value-axis span was always halved, so a subnormal range (0..5e-324) became 0/0 and its ticks NaN.
        public void Waterfall_SubnormalRange_PlacesTicks()
        {
            string svg = Waterfall.RenderSvg(new double[,] { { 0, 5e-324 } });
            Check.True(!svg.Contains("NaN"), "no NaN in the SVG");
            string svg2 = Waterfall.RenderSvg(new double[,] { { 1, 2 } }, new WaterfallOptions { ValueMin = 0, ValueMax = 5e-324 });
            Check.True(!svg2.Contains("NaN"), "no NaN with explicit limits");
        }

        // ---------- undefined enums ----------

        public void UndefinedEnums_AreRejected()
        {
            Check.Equal("style", Check.Throws<ArgumentOutOfRangeException>(() => new Series(new[] { 1.0 }, Ink, (PlotStyle)7)).ParamName);
            var s = new[] { new Series(new[] { 1.0, 2.0 }, Ink) };
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(s, new SvgChartOptions { XAxis = (AxisKind)9 })).ParamName);
            Check.Equal("options", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.Render(s, new SvgChartOptions { YAxis = (AxisKind)(-1) })).ParamName);
            Check.Equal("panels", Check.Throws<ArgumentOutOfRangeException>(
                () => MultiPanel.RenderSvg(new[] { new ChartPanel(s, new SvgChartOptions { YAxis = (AxisKind)9 }) })).ParamName);
            Check.Equal("dash", Check.Throws<ArgumentOutOfRangeException>(() => new Annotations().HorizontalLine(1, dash: (LineDash)5)).ParamName);
            Check.Equal("dash", Check.Throws<ArgumentOutOfRangeException>(() => new Annotations().VerticalLine(1, dash: (LineDash)5)).ParamName);
            var frame = new PlotFrame(0, 0, 10, 10, 0, 1, 0, 1);
            Check.Equal("layer", Check.Throws<ArgumentOutOfRangeException>(() => new Annotations().ToSvg(frame, layer: (AnnotationLayer)5)).ParamName);
        }
    }
}
