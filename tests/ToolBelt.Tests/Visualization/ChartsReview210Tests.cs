using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    // Regressions from review round 210 (charts, thirteenth pass): each test failed before its fix.
    public sealed class ChartsReview210Tests
    {
        private static readonly Rgba Ink = new Rgba(0, 0, 255);

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        // A double as an exact fraction: numerator over 2^1074.
        private static BigInteger Scaled(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            BigInteger r = (BigInteger)m * BigInteger.Pow(2, be - 1);
            return bits < 0 ? -r : r;
        }

        // Naive R-7 quartile over the exact values, times 4 (numerator over 4 * 2^1074).
        private static BigInteger Quartile4(double[] sorted, int quarter)
        {
            int n = sorted.Length;
            int num = quarter * (n - 1);           // rank = num / 4
            int lo = num / 4, f = num % 4;
            int hi = f == 0 ? lo : lo + 1;
            return (4 - f) * Scaled(sorted[lo]) + f * Scaled(sorted[hi]);
        }

        public void BoxPlot_ValuesOnOrNearTheFences_AreClassifiedExactly()
        {
            var first = BoxPlot.Compute(new[] { 0.1, 0.7 }, 0.5);
            Check.Equal(0, first.Outliers.Length, string.Join(", ", first.Outliers));
            var rng = new Random(2101);
            int wrong = 0;
            string? example = null;
            double[] reaches = { 0, 0.25, 0.5, 1, 1.5, 2, 3, 0.1, 1.3 };
            for (int t = 0; t < 20000; t++)
            {
                int n = rng.Next(1, 12);
                var data = new double[n];
                for (int i = 0; i < n; i++) data[i] = rng.Next(-40, 41) / 10.0;
                double k = reaches[rng.Next(reaches.Length)];
                var stats = BoxPlot.Compute(data, k);
                var sorted = (double[])data.Clone();
                Array.Sort(sorted);
                BigInteger q1 = Quartile4(sorted, 1), q3 = Quartile4(sorted, 3), unit = BigInteger.Pow(2, 1074);
                BigInteger kk = Scaled(k), lowFence = q1 * unit - kk * (q3 - q1), highFence = q3 * unit + kk * (q3 - q1);
                var expected = new List<double>();
                foreach (double v in sorted)
                {
                    BigInteger vx = Scaled(v) * 4 * unit;
                    if (vx < lowFence || vx > highFence) expected.Add(v);
                }
                bool same = expected.Count == stats.Outliers.Length;
                for (int i = 0; same && i < expected.Count; i++) same = expected[i] == stats.Outliers[i];
                if (!same)
                {
                    wrong++;
                    if (example == null) example = $"[{string.Join(", ", data)}] at k = {R(k)}: [{string.Join(", ", stats.Outliers)}], expected [{string.Join(", ", expected)}]";
                }
            }
            Check.Equal(0, wrong, example);
        }

        public void BoxPlot_InfinityPastAnOverflowedFence_IsAnOutlier()
        {
            var up = BoxPlot.Compute(new[] { 0, 0, 1e308, 1e308, 1e308, double.PositiveInfinity });
            Check.Equal(1, up.Outliers.Length);
            Check.Equal(double.PositiveInfinity, up.Outliers[0]);
            Check.Equal(1e308, up.UpperWhisker);
            var down = BoxPlot.Compute(new[] { double.NegativeInfinity, -1e308, -1e308, -1e308, 0, 0 });
            Check.Equal(1, down.Outliers.Length);
            Check.Equal(double.NegativeInfinity, down.Outliers[0]);
            Check.Equal(-1e308, down.LowerWhisker);
            // Scaled down it was already right; the answer must not depend on the scale.
            var rng = new Random(2102);
            for (int t = 0; t < 200; t++)
            {
                double s = Math.Pow(2, rng.Next(0, 1020)) * (1 + rng.NextDouble());
                var st = BoxPlot.Compute(new[] { 0, 0, s, s, s, double.PositiveInfinity });
                Check.True(st.Outliers.Length == 1 && double.IsPositiveInfinity(st.Outliers[0]) && st.UpperWhisker == s, $"scale {R(s)}");
            }
        }

        public void BoxPlot_Render_AllInfinite_DrawsSomething()
        {
            // A box from -inf to +inf: its sides run the whole plot height, clipped at the edges (from +inf to -inf every
            // position was NaN and nothing was drawn).
            var image = BoxPlot.Render(100, 100, new[] { new[] { double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity } },
                new BoxPlotOptions { DrawFrame = false });
            int painted = 0;
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 100; x++)
                    if (!image.GetPixel(x, y).Equals(Rgba.White)) painted++;
            Check.True(painted > 0, "nothing drawn");
        }

        public void MultiPanel_Layout_EveryPlotAreaHasTheSameSize()
        {
            var rng = new Random(2103);
            int wrong = 0;
            string? example = null;
            for (int t = 0; t < 300; t++)
            {
                int n = rng.Next(1, 7);
                var panels = new List<ChartPanel>();
                for (int p = 0; p < n; p++)
                {
                    int len = rng.Next(2, 8);
                    var y = new double[len];
                    double scale = Math.Pow(10, rng.Next(-3, 7));
                    for (int i = 0; i < len; i++) y[i] = (rng.NextDouble() - 0.3) * scale;
                    var opts = new SvgChartOptions();
                    if (rng.Next(2) == 0) opts.Title = "Panel " + p.ToString(CultureInfo.InvariantCulture);
                    if (rng.Next(2) == 0) opts.YLabel = "value";
                    if (rng.Next(2) == 0) opts.XLabel = "time";
                    panels.Add(new ChartPanel(new[] { new Series(y, Ink) }, opts));
                }
                var options = new MultiPanelOptions
                {
                    Columns = rng.Next(1, 4), ShareX = rng.Next(2) == 0, ShareY = rng.Next(2) == 0,
                    PanelWidth = 300 + 400 * rng.NextDouble(), PanelHeight = 150 + 300 * rng.NextDouble(),
                };
                var frames = MultiPanel.Layout(panels, options);
                for (int i = 1; i < frames.Count; i++)
                    if (frames[i].Width != frames[0].Width || frames[i].Height != frames[0].Height)
                    {
                        wrong++;
                        if (example == null) example = $"trial {t} panel {i}: {R(frames[i].Width)} x {R(frames[i].Height)} vs {R(frames[0].Width)} x {R(frames[0].Height)}";
                    }
            }
            Check.Equal(0, wrong, example);
        }

        public void Annotations_ThickDottedLine_CostsNoMoreThanASolidOne()
        {
            var image = new ImageBuffer(2000, 2000);
            var frame = new PlotFrame(0, 0, 1999, 1999, 0, 1, 0, 1);
            var sw = Stopwatch.StartNew();
            new Annotations().VerticalLine(0.5, dash: LineDash.Dotted, width: 1e6).HorizontalLine(0.5, dash: LineDash.Dashed, width: 1e6).Draw(image, frame);
            Check.True(sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms");
            // The pen covers the whole image either way, as a solid one does.
            var solid = new ImageBuffer(2000, 2000);
            new Annotations().VerticalLine(0.5, dash: LineDash.Solid, width: 1e6).Draw(solid, frame);
            for (int y = 0; y < 2000; y += 37)
                for (int x = 0; x < 2000; x += 41)
                    Check.Equal(solid.GetPixel(x, y), image.GetPixel(x, y), $"({x}, {y})");
        }

        public void Annotations_BandsThatTileARange_BlendEachRowOnce()
        {
            var rng = new Random(2104);
            for (int t = 0; t < 200; t++)
            {
                int size = rng.Next(20, 120);
                bool horizontal = rng.Next(2) == 0;
                var frame = new PlotFrame(0, 0, size - 1, size - 1, 0, 10, 0, 10);
                int k = rng.Next(2, 6);
                var cuts = Enumerable.Range(0, k - 1).Select(_ => Math.Round(rng.NextDouble() * 10, rng.Next(0, 3))).Concat(new[] { 0.0, 10.0 }).Distinct().OrderBy(v => v).ToArray();
                var a = new Annotations();
                for (int i = 0; i + 1 < cuts.Length; i++)
                {
                    if (horizontal) a.HorizontalBand(cuts[i], cuts[i + 1]); else a.VerticalBand(cuts[i], cuts[i + 1]);
                }
                var one = new ImageBuffer(size, size, Rgba.White);
                new Annotations().HorizontalBand(0, 10).Draw(one, frame);
                var image = new ImageBuffer(size, size, Rgba.White);
                a.Draw(image, frame);
                string expected = one.GetPixel(size / 2, size / 2).ToString();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (image.GetPixel(x, y).ToString() != expected)
                        {
                            Check.True(false, $"trial {t}: [{string.Join(", ", cuts)}] {(horizontal ? "horizontal" : "vertical")} on {size} px, pixel ({x}, {y}) is {image.GetPixel(x, y)}, not {expected}");
                            return;
                        }
            }
        }

        public void Histogram_MinAtMaxValue_OpensBelowIt()
        {
            var up = Histogram.Bin(new[] { double.MaxValue, 1.0 }, 4, min: double.MaxValue);
            Check.True(up.Edges[0] < up.Edges[4], string.Join(", ", up.Edges.Select(R)));
            for (int i = 0; i < 4; i++) Check.True(up.Edges[i] < up.Edges[i + 1], string.Join(", ", up.Edges.Select(R)));
            Check.Equal(double.MaxValue, up.Edges[4]);
            Check.Equal(1, up.Counts.Sum());
            Check.Equal(1, up.Counts[3]);
            var down = Histogram.Bin(new[] { -double.MaxValue, 1.0 }, 4, max: -double.MaxValue);
            for (int i = 0; i < 4; i++) Check.True(down.Edges[i] < down.Edges[i + 1], string.Join(", ", down.Edges.Select(R)));
            Check.Equal(-double.MaxValue, down.Edges[0]);
            Check.Equal(1, down.Counts.Sum());
            Check.Equal(1, down.Counts[0]);
        }

        // Fails only on .NET Framework, where "R" can print 15 digits that read back as another double.
        public void Waterfall_FlatLabel_ReadsBack()
        {
            var rng = new Random(2105);
            for (int t = 0; t < 300; t++)
            {
                double v = t == 0 ? 0.84551240822557006 : (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-20, 21));
                string svg = Waterfall.RenderSvg(new double[2, 2] { { v, v }, { v, v } });
                bool found = false;
                foreach (Match m in Regex.Matches(svg, ">([-0-9.E+e]+)</text>"))
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double read) && read == v) found = true;
                Check.True(found, "no label reads back as " + R(v));
            }
        }

        public void SvgChart_InFrameLine_IsOneSubpath()
        {
            var rng = new Random(2106);
            int split = 0;
            string? example = null;
            for (int t = 0; t < 1000; t++)
            {
                int n = rng.Next(2, 40);
                var ys = Enumerable.Range(0, n).Select(_ => rng.NextDouble()).ToArray();
                var xs = Enumerable.Range(0, n).Select(_ => rng.NextDouble()).ToArray();
                string svg = SvgChart.Render(new[] { new Series(ys, Ink, PlotStyle.Line, xs) }, new SvgChartOptions());
                var m = Regex.Match(svg, "<path d=\"([^\"]*)\" fill=\"none\" stroke=\"#0000ff\"");
                Check.True(m.Success, "no path");
                if (m.Groups[1].Value.Count(c => c == 'M') != 1)
                {
                    split++;
                    if (example == null) example = m.Groups[1].Value;
                }
            }
            Check.Equal(0, split, example);
        }
    }
}
