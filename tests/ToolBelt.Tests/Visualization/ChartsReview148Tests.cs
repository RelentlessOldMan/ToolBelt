using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    // Review round 148 (charts, ninth pass): each test failed on the code before its fix.
    public sealed class ChartsReview148Tests
    {
        private static readonly Rgba Blue = new Rgba(31, 119, 180);

        // The horizontal span a <text> element holding exactly `label` covers: its textLength when it has one, else the
        // library's own width estimate, placed by its text-anchor.
        private static (double Start, double End) Extent(string svg, string label, double fontSize, bool bold = false)
        {
            var m = Regex.Match(svg, "<text ([^>]*)>" + Regex.Escape(label) + "</text>");
            Check.True(m.Success, "label found");
            string attrs = m.Groups[1].Value;
            double Attr(string name) => double.Parse(Regex.Match(attrs, "(?:^| )" + name + "=\"([^\"]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);
            double x = Attr("x");
            double w = attrs.Contains("textLength=") ? Attr("textLength") : SvgUtils.EstimateTextWidth(label, fontSize, bold);
            string anchor = Regex.Match(attrs, "text-anchor=\"([a-z]+)\"").Groups[1].Value;
            return anchor == "end" ? (x - w, x) : anchor == "middle" ? (x - w / 2, x + w / 2) : (x, x + w);
        }

        private static void Within(string svg, string label, double fontSize, double lo, double hi, string what, bool bold = false)
        {
            var (s, e) = Extent(svg, label, fontSize, bold);
            Check.True(s >= lo - 1e-6 && e <= hi + 1e-6, $"{what}: [{s}, {e}] inside [{lo}, {hi}]");
        }

        // ---------- ScatterMatrix.Pearson: a constant column ----------

        // The rounded mean of seven 0.1s is not 0.1, so the deviations were an ulp, not 0, and r ≈ −2.4e-17 came back.
        public void Pearson_AConstantColumnHasNoCorrelation()
        {
            var ramp = Enumerable.Range(0, 13).Select(i => (double)i).ToArray();
            foreach (double c in new[] { 0.1, 0.3, 3.3, 0.2, 1e-5, 123.456 })
                foreach (int n in new[] { 6, 7, 10, 11, 13 })
                {
                    var flat = Enumerable.Repeat(c, n).ToArray();
                    Check.True(ScatterMatrix.Pearson(flat, ramp.Take(n).ToArray()) is null, $"x = {c} × {n}");
                    Check.True(ScatterMatrix.Pearson(ramp.Take(n).ToArray(), flat) is null, $"y = {c} × {n}");
                }
            // Guard: a column that varies still correlates.
            Check.Close(1, ScatterMatrix.Pearson(new[] { 0.1, 0.2, 0.3 }, new[] { 1.0, 2, 3 })!.Value, 1e-12);
        }

        // ---------- Annotations: labels wider than their room ----------

        private static readonly PlotFrame Frame = new PlotFrame(50, 20, 300, 200, 0, 10, 0, 10);

        // Callout skipped its clamp, HorizontalLine and the bands never checked width, and VerticalLine flipped without
        // checking the left: each label ran off the plot and the canvas.
        public void Annotations_WideLabelsStayInsideThePlot()
        {
            string wide = new string('W', 80), mid = new string('W', 30);
            var cases = new (string What, Annotations A, string Label)[]
            {
                ("callout right", new Annotations().Callout(5, 5, wide), wide),
                ("callout left", new Annotations().Callout(5, 5, wide, dx: -30), wide),
                ("arrow", new Annotations().Arrow(5, 5, 9, 9, wide), wide),
                ("hline", new Annotations().HorizontalLine(5, wide), wide),
                ("vline near left", new Annotations().VerticalLine(0.5, mid), mid),
                ("vline near right", new Annotations().VerticalLine(9.5, mid), mid),
                ("vline wide", new Annotations().VerticalLine(5, wide), wide),
                ("vband", new Annotations().VerticalBand(1, 2, wide), wide),
                ("hband", new Annotations().HorizontalBand(1, 2, wide), wide),
            };
            foreach (var (what, a, label) in cases)
                Within(a.ToSvg(Frame), label, 11, Frame.Left, Frame.Left + Frame.Width, what);
            // Guard: a label that fits keeps its place and gets no textLength.
            string svg = new Annotations().VerticalLine(5, "ok").ToSvg(Frame);
            Check.False(svg.Contains("textLength"));
            Check.Equal(204.0, Extent(svg, "ok", 11).Start);
        }

        // ---------- SvgChart legend ----------

        private static (double X, double Y, double W, double H) LegendBox(string svg)
        {
            var m = Regex.Match(svg, "<g class=\"legend\"[^>]*>\n<rect x=\"([^\"]+)\" y=\"([^\"]+)\" width=\"([^\"]+)\" height=\"([^\"]+)\"");
            Check.True(m.Success, "legend found");
            double P(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            return (P(1), P(2), P(3), P(4));
        }

        // A long name put the box at x = −440; forty series ran it to y = 696 on a 400-high chart.
        public void SvgChart_TheLegendStaysOnTheChart()
        {
            string longName = new string('W', 100);
            string svg = SvgChart.Render(new[] { new Series(new[] { 1.0, 2 }, Blue, name: longName) });
            var box = LegendBox(svg);
            Check.True(box.X >= 0 && box.X + box.W <= 640, $"x {box.X}, w {box.W}");
            Within(svg, longName, 14 * 0.9, box.X, box.X + box.W, "the name");

            var many = Enumerable.Range(0, 40).Select(i => new Series(new[] { 1.0, i }, Blue, name: "s" + i)).ToArray();
            svg = SvgChart.Render(many);
            box = LegendBox(svg);
            Check.True(box.Y >= 0 && box.Y + box.H <= 400, $"y {box.Y}, h {box.H}");
            var shown = Regex.Matches(svg, ">s[0-9]+</text>").Count;
            Check.True(svg.Contains(">+" + (40 - shown) + " more</text>"), "the rest are counted");
            // Guard: a small legend is drawn whole, as before.
            svg = SvgChart.Render(new[] { new Series(new[] { 1.0, 2 }, Blue, name: "a"), new Series(new[] { 2.0, 1 }, Blue, name: "b") });
            Check.True(svg.Contains(">a</text>") && svg.Contains(">b</text>") && !svg.Contains(" more<"));
        }

        // ---------- Titles ----------

        // Centred (or left-aligned) with no width check, a long title ran off the canvas.
        public void Titles_StayOnTheCanvas()
        {
            string title = new string('W', 300);
            Within(SvgChart.Render(new[] { new Series(new[] { 1.0, 2 }, Blue) }, new SvgChartOptions { Title = title }),
                title, 14 * 1.2, 0, 640, "SvgChart", bold: true);
            Within(HexBin.RenderSvg(new[] { (0.0, 0.0), (1.0, 1.0) }, new HexBinOptions { Title = title }), title, 14, 0, 640, "HexBin", bold: true);
            var data = new double[4, 4];
            data[1, 1] = 1;
            var wf = new WaterfallOptions { Title = title };
            Within(Waterfall.RenderSvg(data, wf), title, wf.FontSize * 1.2, 0, 720, "Waterfall", bold: true);
            var mp = new MultiPanelOptions { Title = title };
            string multi = MultiPanel.RenderSvg(new[] { new ChartPanel(new[] { new Series(new[] { 1.0, 2 }, Blue) }) }, mp);
            double mw = double.Parse(Regex.Match(multi, "<svg[^>]* width=\"([^\"]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);
            Within(multi, title, mp.FontSize * 1.25, 0, mw, "MultiPanel", bold: true);
            var sig = TimingSignal.Digital("a", new (double, bool?)[] { (0, true), (1, false) });
            Within(TimingDiagram.RenderSvg(new[] { sig }, new TimingDiagramOptions { Title = title }), title, 14, 0, 900, "TimingDiagram", bold: true);
        }

        // ---------- TimingDiagram: the last tick label ----------

        // Centred on t1 with a 16 px right margin, "1700000001 s" ended at about 919 on a 900 px canvas.
        public void TimingDiagram_TickLabelsStayOnTheCanvas()
        {
            var sig = TimingSignal.Digital("a", new (double, bool?)[] { (1.7e9, true), (1.7e9 + 1, false) });
            string svg = TimingDiagram.RenderSvg(new[] { sig });
            var labels = Regex.Matches(svg, "<text [^>]*text-anchor=\"[a-z]+\">([^<]+)</text>").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Check.True(labels.Count >= 2, "tick labels found");
            foreach (string l in labels) Within(svg, l, 11, 0, 900, l);
            // Guard: an inner tick is still centred on its tick.
            Check.True(Regex.IsMatch(TimingDiagram.RenderSvg(new[] { TimingSignal.Digital("a", new (double, bool?)[] { (0, true), (10, false) }) }),
                "text-anchor=\"middle\">4 s</text>"));
        }

        // ---------- TimingSignal.FromSamples: a NaN high threshold ----------

        // A NaN low threshold threw ArgumentOutOfRangeException; a NaN high one threw ArgumentException saying it was below.
        public void FromSamples_ANaNHighThresholdIsOutOfRange()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => TimingSignal.FromSamples("a", new[] { 0.0, 1 }, 1, 0, double.NaN));
            Check.Equal("highThreshold", ex.ParamName);
            Check.Equal("lowThreshold", Check.Throws<ArgumentOutOfRangeException>(() => TimingSignal.FromSamples("a", new[] { 0.0, 1 }, 1, double.NaN, 1)).ParamName);
        }

        // ---------- BandChart: the centre line outside the band ----------

        private static List<(int X, int Y)> CenterPixels(ImageBuffer img, Rgba c)
        {
            var list = new List<(int, int)>();
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.GetPixel(x, y).Equals(c)) list.Add((x, y));
            return list;
        }

        // The range came from the bounds only: a centre at 5 over a band [0, 1] was clipped away, and a ramp over a flat
        // band drew on one row.
        public void BandChart_TheCentreLineSetsTheRangeToo()
        {
            var o = new BandOptions();
            var outside = BandChart.Render(100, 100, Enumerable.Repeat(0.0, 50).ToArray(), Enumerable.Repeat(1.0, 50).ToArray(),
                Enumerable.Repeat(5.0, 50).ToArray(), o);
            Check.True(CenterPixels(outside, o.CenterColor).Count > 0, "the excursion is drawn");

            var ramp = Enumerable.Range(0, 50).Select(i => (double)i).ToArray();
            foreach (double b in new[] { 1.0, double.NaN })
            {
                var flat = BandChart.Render(100, 100, Enumerable.Repeat(b, 50).ToArray(), Enumerable.Repeat(b, 50).ToArray(), ramp, o);
                var px = CenterPixels(flat, o.CenterColor);
                Check.True(px.Select(p => p.Y).Distinct().Count() > 40, $"bound {b}: the ramp rises");
            }
        }

        // ---------- SvgChart.RenderElement: a non-finite offset ----------

        // It failed inside SvgUtils.Number, naming "value".
        public void RenderElement_ANonFiniteOffsetNamesItself()
        {
            var s = new[] { new Series(new[] { 1.0, 2 }, Blue) };
            Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.RenderElement(s, null, double.NaN, 0)).ParamName);
            Check.Equal("y", Check.Throws<ArgumentOutOfRangeException>(() => SvgChart.RenderElement(s, null, 0, double.PositiveInfinity)).ParamName);
            Check.True(SvgChart.RenderElement(s, null, 5, 6).Contains(" x=\"5\" y=\"6\""));
        }

        // ---------- SvgUtils.FitWidth ----------

        public void FitWidth_SqueezesOnlyWhatIsWider()
        {
            double w = SvgUtils.EstimateTextWidth("hello", 12);
            Check.Equal("", SvgUtils.FitWidth("hello", 12, w));
            Check.Equal("", SvgUtils.FitWidth("hello", 12, 0));
            Check.Equal("", SvgUtils.FitWidth("hello", 12, double.PositiveInfinity));
            Check.Equal(" textLength=\"10\" lengthAdjust=\"spacingAndGlyphs\"", SvgUtils.FitWidth("hello", 12, 10));
            Check.Equal("maxWidth", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.FitWidth("hello", 12, double.NaN)).ParamName);
            Check.Equal("text", Check.Throws<ArgumentNullException>(() => SvgUtils.FitWidth(null!, 12, 10)).ParamName);
        }
    }
}
