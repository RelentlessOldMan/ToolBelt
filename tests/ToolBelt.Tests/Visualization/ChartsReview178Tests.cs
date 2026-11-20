using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 178 (charts, eleventh pass): each test failed before its fix.
    public sealed class ChartsReview178Tests
    {
        private static readonly Rgba Ink = new Rgba(0, 0, 255);
        private static readonly Rgba Mark = new Rgba(1, 2, 3);

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static int Px(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        // Every pixel of the given colour lies inside the frame's plot area (rounded as the chart layers round).
        private static void OnlyInsidePlot(ImageBuffer img, PlotFrame f, Rgba color, string what)
        {
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (img.GetPixel(x, y).Equals(color) && (x < Px(f.Left) || x > Px(f.Right) || y < Px(f.Top) || y > Px(f.Bottom)))
                    {
                        Check.True(false, what + ": colour at (" + x + ", " + y + ")");
                        return;
                    }
        }

        private static List<int> Columns(ImageBuffer img, int row, Rgba color)
        {
            var cols = new List<int>();
            for (int x = 0; x < img.Width; x++) if (img.GetPixel(x, row).Equals(color)) cols.Add(x);
            return cols;
        }

        private static XElement Svg(string fragment) => XElement.Parse("<g>" + fragment + "</g>");

        private static double Attr(XElement e, string name) => double.Parse(e.Attribute(name)!.Value, CultureInfo.InvariantCulture);

        // ---------- LinePlot ----------

        // LinePlot rounded pixel halves to even, Annotations away from zero: a scatter point and a VerticalLine at the same
        // x landed a column apart for a quarter of the x values, though both use the same frame.
        public void Scatter_LinesUpWithVerticalLine()
        {
            var o = new PlotOptions { MinX = 0, MaxX = 10, MinY = 0, MaxY = 10, DrawFrame = false };
            for (int w = 100; w < 112; w++)
                for (int k = 0; k <= 160; k++)
                {
                    double x = k / 16.0;
                    var series = new[] { new Series(new[] { 8.0 }, Ink, PlotStyle.Scatter, new[] { x }) };
                    ImageBuffer img = LinePlot.Render(w, 60, series, o);
                    PlotFrame f = LinePlot.Frame(w, 60, series, o);
                    new Annotations().VerticalLine(x, color: Rgba.Red, dash: LineDash.Solid, width: 1).Draw(img, f);
                    int row = Px(f.MapY(8));
                    List<int> marker = Columns(img, row, Ink), line = Columns(img, Px(f.MapY(2)), Rgba.Red);
                    Check.True(marker.Count > 0 && line.Count == 1, "w " + w + " x " + R(x) + ": drawn");
                    // The line overdraws the marker's centre column: the marker's other columns sit one either side.
                    Check.True(marker.All(c => Math.Abs(c - line[0]) == 1), "w " + w + " x " + R(x) + ": marker " + string.Join(",", marker) + " line " + line[0]);
                }
        }

        // The 3x3 marker of a point on the plot edge painted into the margin (and over the frame).
        public void Scatter_MarkersStayInsideThePlot()
        {
            var o = new PlotOptions { MinX = 0, MaxX = 10, MinY = 0, MaxY = 10, DrawFrame = false };
            var series = new[] { new Series(new double[] { 0, 10, 0, 10, 5 }, Ink, PlotStyle.Scatter, new double[] { 0, 10, 10, 0, 0 }) };
            ImageBuffer img = LinePlot.Render(80, 60, series, o);
            OnlyInsidePlot(img, LinePlot.Frame(80, 60, series, o), Ink, "scatter");
            Check.True(img.GetPixel(Px(LinePlot.Frame(80, 60, series, o).Left), Px(LinePlot.Frame(80, 60, series, o).Bottom)).Equals(Ink), "corner drawn");
        }

        // ---------- Annotations, raster callout ----------

        // The raster callout took the raw offset for its tail (the SVG layer clamps it into the plot), so a callout near
        // the right edge drew its arrow through the margin, and its 5x5 marker on the frame painted 2 px outside.
        public void RasterCallout_StaysInsideThePlot()
        {
            var f = new PlotFrame(20, 20, 160, 100, 0, 10, 0, 10);
            var rng = new Random(178);
            for (int trial = 0; trial < 400; trial++)
            {
                var img = new ImageBuffer(200, 140);
                double x = rng.Next(3) == 0 ? 10 * rng.Next(2) : rng.NextDouble() * 10, y = rng.Next(3) == 0 ? 10 * rng.Next(2) : rng.NextDouble() * 10;
                double dx = (rng.NextDouble() * 2 - 1) * 120, dy = (rng.NextDouble() * 2 - 1) * 120;
                new Annotations().Callout(x, y, "note", dx, dy, Mark).Draw(img, f);
                OnlyInsidePlot(img, f, Mark, "trial " + trial + " (" + R(x) + ", " + R(y) + ") + (" + R(dx) + ", " + R(dy) + ")");
            }
        }

        // ---------- Annotations, short arrows ----------

        // An 8-px head on a 2-px arrow put its base 6 px behind the tail (and the SVG shaft ran backwards past it).
        public void ShortArrow_DrawsNothingBehindItsTail()
        {
            var f = new PlotFrame(0, 0, 100, 100, 0, 100, 0, 100);
            foreach (double len in new[] { 0.5, 1, 2, 3, 5, 7.9 })
            {
                var a = new Annotations().Arrow(50, 50, 50 + len, 50, color: Mark);
                XElement svg = Svg(a.ToSvg(f));
                Check.True(svg.Descendants("polygon").Any(), "svg head drawn, length " + R(len));
                foreach (XElement line in svg.Descendants("line"))
                    Check.True(Attr(line, "x1") >= 50 - 1e-9 && Attr(line, "x2") >= 50 - 1e-9 && Attr(line, "x2") <= 50 + len + 1e-9, "svg shaft, length " + R(len));
                foreach (XElement poly in svg.Descendants("polygon"))
                    foreach (string p in poly.Attribute("points")!.Value.Split(' '))
                    {
                        double px = double.Parse(p.Split(',')[0], CultureInfo.InvariantCulture);
                        Check.True(px >= 50 - 1e-9 && px <= 50 + len + 1e-9, "svg head point " + p + ", length " + R(len));
                    }
                var img = new ImageBuffer(100, 100);
                a.Draw(img, f);
                for (int y = 0; y < 100; y++)
                    for (int x = 0; x < 100; x++)
                        if (img.GetPixel(x, y).Equals(Mark))
                            Check.True(x >= 50 && x <= Px(50 + len), "raster pixel (" + x + ", " + y + "), length " + R(len));
            }
        }

        // On a frame 1e200 px wide the squared deltas overflowed: a zero direction, so the head was three equal points.
        public void SvgArrow_OnAHugeFrame_KeepsItsHead()
        {
            string svg = new Annotations().Arrow(0, 0.5, 1, 0.5).ToSvg(new PlotFrame(0, 0, 1e200, 100, 0, 1, 0, 1));
            XElement poly = Svg(svg).Descendants("polygon").Single();
            var ys = poly.Attribute("points")!.Value.Split(' ').Select(p => p.Split(',')[1]).Distinct().Count();
            Check.Equal(3, ys, poly.Attribute("points")!.Value);
        }

        // ---------- Annotations, labels held inside the plot ----------

        // Band, arrow and line labels had no vertical clamp: a thin band at the bottom put its label below the plot, an
        // arrow whose tail was on the top edge put its label above it, and a line on a short plot put its label below.
        public void SvgLabels_StayInsideThePlotVertically()
        {
            var thin = new PlotFrame(50, 20, 400, 200, 0, 1, 0, 1);
            foreach (var (a, what) in new[] {
                (new Annotations().HorizontalBand(0, 0.01, "Tolerance"), "band"),
                (new Annotations().Arrow(0.5, 1, 0.5, 0.2, "start"), "arrow") })
                foreach (XElement t in Svg(a.ToSvg(thin)).Descendants("text").DefaultIfEmpty(new XElement("text", new XAttribute("y", "-1"))))
                    Check.True(Attr(t, "y") >= 20 + 11 && Attr(t, "y") <= 220, what + " y " + t.Attribute("y")!.Value);
            var shortPlot = new PlotFrame(0, 0, 200, 8, 0, 1, 0, 1);
            foreach (XElement t in Svg(new Annotations().HorizontalLine(0.5, "h").ToSvg(shortPlot)).Descendants("text").DefaultIfEmpty(new XElement("text", new XAttribute("y", "99"))))
                Check.True(Attr(t, "y") <= 8, "short h-line y " + t.Attribute("y")!.Value);

            var rng = new Random(1781);
            for (int trial = 0; trial < 3000; trial++)
            {
                double top = rng.Next(0, 40), height = rng.Next(30, 300);
                var f = new PlotFrame(rng.Next(0, 60), top, rng.Next(150, 600), height, 0, 10, 0, 10);
                double U() => rng.NextDouble() * 10;
                var a = new Annotations();
                switch (rng.Next(6))
                {
                    case 0: a.HorizontalBand(U(), U(), "band"); break;
                    case 1: a.VerticalBand(U(), U(), "band"); break;
                    case 2: a.HorizontalLine(rng.Next(2) == 0 ? 10 * rng.Next(2) : U(), "line"); break;
                    case 3: a.VerticalLine(U(), "line"); break;
                    case 4: a.Arrow(U(), U(), rng.Next(2) == 0 ? 10 * rng.Next(2) : U(), rng.Next(2) == 0 ? 10 * rng.Next(2) : U(), "arrow"); break;
                    default: a.Callout(U(), U(), "callout", (rng.NextDouble() * 2 - 1) * 80, (rng.NextDouble() * 2 - 1) * 80); break;
                }
                foreach (XElement t in Svg(a.ToSvg(f)).Descendants("text"))
                {
                    double y = Attr(t, "y");
                    Check.True(y >= top + 11 - 1e-9 && y <= top + height + 1e-9, "trial " + trial + ": y " + R(y) + " in [" + R(top) + ", " + R(top + height) + "]");
                }
            }
        }

        // ---------- BoxPlot ----------

        // (v - min) / range * height overflowed for a Q3 of 4.5e307 (data max double.MaxValue) or +inf on a 0..10 axis:
        // the pixel was -inf, so the box sides and upper whisker were dropped instead of clipped at the top edge. Any
        // value above the explicit Max draws the same as one just above it.
        public void BoxPlot_HugeOrInfiniteValues_ClipLikeLargeOnes()
        {
            var o = new BoxPlotOptions { Min = 0, Max = 10, DrawFrame = false };
            ImageBuffer Draw(params double[] d) => BoxPlot.Render(80, 100, new[] { (IReadOnlyList<double>)d }, o);
            void Same(ImageBuffer a, ImageBuffer b, string what)
            {
                for (int y = 0; y < a.Height; y++)
                    for (int x = 0; x < a.Width; x++)
                        if (!a.GetPixel(x, y).Equals(b.GetPixel(x, y)))
                        {
                            Check.True(false, what + ": differs at (" + x + ", " + y + ")");
                            return;
                        }
            }
            ImageBuffer large = Draw(1, 2, 3, 1e6);
            Same(large, Draw(1, 2, 3, double.MaxValue), "MaxValue");
            Same(large, Draw(1, 2, 3, double.PositiveInfinity), "+inf");
            ImageBuffer large2 = Draw(1, 2, 1e6, 1e6);
            Same(large2, Draw(1, 2, double.PositiveInfinity, double.PositiveInfinity), "two +inf");
            ImageBuffer low = Draw(-1e6, 7, 8, 9);
            Same(low, Draw(double.NegativeInfinity, 7, 8, 9), "-inf");
            Same(low, Draw(-double.MaxValue, 7, 8, 9), "-MaxValue");
            // The box side reaches the top row.
            Check.True(Columns(large, 12, o.BoxColor).Count > 0, "side at the top");
        }

        // ---------- Histogram ----------

        // Edges were lo + span*i/bins in doubles, a quarter of decimal edges an ulp off: 70.9..115.8 in 12 bins gave
        // 93.35000000000001, and -0.46 on the edge of [-0.5, -0.42] in 2 bins was counted in the bin below.
        public void Histogram_DecimalEdges_AreExact()
        {
            Check.Equal(93.35, Histogram.Bin(new double[0], 12, 70.9, 115.8).Edges[6], "70.9..115.8");
            var (edges, counts) = Histogram.Bin(new[] { -0.5, -0.46, -0.42 }, 2, -0.5, -0.42);
            Check.Equal(-0.46, edges[1], "-0.46 edge");
            Check.Equal("1,2", string.Join(",", counts), "-0.46 counted");

            // Extremes stay finite, monotone and exact at the ends.
            foreach (var (lo, hi, n) in new[] { (-double.MaxValue, double.MaxValue, 7), (-double.Epsilon, double.Epsilon, 5), (0.0, 5e-324 * 9, 9), (1e-310, 1e300, 13) })
            {
                double[] e = Histogram.Bin(new double[0], n, lo, hi).Edges;
                Check.Equal(lo, e[0], "lo " + R(lo));
                Check.Equal(hi, e[n], "hi " + R(hi));
                for (int i = 1; i <= n; i++) Check.True(e[i] >= e[i - 1] && !double.IsInfinity(e[i]), "edge " + i + " of " + R(lo) + ".." + R(hi) + ": " + R(e[i]));
            }
            Check.Equal(0.0, Histogram.Bin(new double[0], 2, -double.MaxValue, double.MaxValue).Edges[1], "+-MaxValue middle");
#if !NETFRAMEWORK
            // Against decimal arithmetic on the printed bounds (net8 parses correctly rounded; net48 does not).
            var rng = new Random(1782);
            for (int trial = 0; trial < 20000; trial++)
            {
                int bins = rng.Next(2, 25);
                double lo = Math.Round((rng.NextDouble() * 2 - 1) * 1000, rng.Next(0, 4));
                double hi = Math.Round(lo + rng.NextDouble() * 200 + 0.01, rng.Next(0, 4));
                if (!(hi > lo)) continue;
                double[] e = Histogram.Bin(new double[0], bins, lo, hi).Edges;
                decimal dLo = decimal.Parse(R(lo), CultureInfo.InvariantCulture), dHi = decimal.Parse(R(hi), CultureInfo.InvariantCulture);
                for (int i = 1; i < bins; i++)
                {
                    double expected = double.Parse((dLo + (dHi - dLo) * i / bins).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    Check.Equal(expected, e[i], "trial " + trial + ": " + R(lo) + ".." + R(hi) + " in " + bins + ", edge " + i);
                }
            }
#endif
        }

        // ---------- Colormap ----------

        // Channels rounded halves to even: the midpoint of 0..1 gave 0 and of 1..2 gave 2. They now round as the chart
        // layers do, halves up.
        public void Colormap_ChannelHalvesRoundUp()
        {
            for (int a = 0; a < 255; a++)
            {
                var map = new Colormap((0, new Rgba((byte)a, (byte)a, (byte)a)), (1, new Rgba((byte)(a + 1), (byte)(a + 1), (byte)(a + 1))));
                Rgba mid = map.MapFraction(0.5);
                Check.Equal(a + 1, (int)mid.R, "R " + a);
                Check.Equal(a + 1, (int)mid.B, "B " + a);
            }
        }
    }
}
