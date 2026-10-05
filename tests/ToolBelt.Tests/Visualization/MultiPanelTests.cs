using System;
using System.Linq;
using System.Xml.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    public sealed class MultiPanelTests
    {
        private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

        private static ChartPanel Panel(double[] x, double[] y, SvgChartOptions? o = null)
            => new ChartPanel(new[] { new Series(y, Rgba.Blue, PlotStyle.Line, x) }, o);

        private static int TextCount(string svg, string value)
            => XDocument.Parse(svg).Descendants(Ns + "text").Count(t => t.Value == value);

        public void Svg_IsWellFormedWithNestedPanelsAndTitle()
        {
            var panels = new[]
            {
                Panel(new double[] { 0, 1, 2 }, new double[] { 1, 2, 3 }, new SvgChartOptions { Title = "A" }),
                Panel(new double[] { 0, 1, 2 }, new double[] { 3, 2, 1 }, new SvgChartOptions { Title = "B" }),
                Panel(new double[] { 0, 1, 2 }, new double[] { 2, 2, 2 }, new SvgChartOptions { Title = "C" }),
            };
            var o = new MultiPanelOptions { Columns = 2, PanelWidth = 300, PanelHeight = 200, Gap = 10, Title = "Overview" };
            XDocument doc = XDocument.Parse(MultiPanel.RenderSvg(panels, o));
            XElement root = doc.Root!;
            Check.Equal(3, root.Elements(Ns + "svg").Count());
            Check.Equal("630", root.Attribute("width")!.Value);       // 2*300 + 3*10
            Check.True(doc.Descendants(Ns + "text").Any(t => t.Value == "Overview"));

            // Third panel wraps to the second row, first column.
            XElement third = root.Elements(Ns + "svg").ElementAt(2);
            Check.Equal("10", third.Attribute("x")!.Value);
            Check.True(double.Parse(third.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture) > 200);
        }

        public void ShareX_CommonRangeAlignedAreasAndBottomLabelsOnly()
        {
            var panels = new[]
            {
                Panel(new double[] { 1000, 3000 }, new double[] { 0, 1_000_000 }),   // wide y labels
                Panel(new double[] { 2000, 5000 }, new double[] { 0, 1 }),           // narrow y labels
            };
            var o = new MultiPanelOptions { ShareX = true };
            var frames = MultiPanel.Layout(panels, o);
            Check.Equal(frames[0].XMin, frames[1].XMin);
            Check.Equal(frames[0].XMax, frames[1].XMax);
            Check.True(frames[0].XMin <= 1000 && frames[0].XMax >= 5000, "union of both ranges");
            Check.Equal(frames[0].Left, frames[1].Left);                     // aligned despite label widths
            Check.Equal(frames[0].Width, frames[1].Width);

            string shared = MultiPanel.RenderSvg(panels, o);
            Check.Equal(1, TextCount(shared, "3000"));                       // only the bottom panel labels x
            Check.Equal(2, TextCount(MultiPanel.RenderSvg(panels), "3000")); // unshared: both do
        }

        public void PlotAreasAlignExactly_Randomized()
        {
            var rng = new DeterministicRandom(8675309);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = rng.Next(2, 6), cols = rng.Next(1, 4);
                var panels = new ChartPanel[n];
                for (int i = 0; i < n; i++)
                {
                    double scale = Math.Pow(10, rng.Next(-4, 8));
                    double lo = (rng.NextDouble() - 0.5) * scale, hi = lo + rng.NextDouble() * scale + scale * 1e-3;
                    panels[i] = Panel(new double[] { 0, rng.Next(1, 1000) }, new[] { lo, hi },
                        new SvgChartOptions { Title = rng.NextDouble() < 0.5 ? "T" : null, YLabel = rng.NextDouble() < 0.5 ? "y" : null });
                }
                var frames = MultiPanel.Layout(panels, new MultiPanelOptions { Columns = cols, ShareX = rng.NextDouble() < 0.5 });
                int c = Math.Min(cols, n);
                for (int i = 1; i < n; i++)
                {
                    Check.Close(frames[0].Width, frames[i].Width, 1e-9);     // one plot size everywhere
                    Check.Close(frames[0].Height, frames[i].Height, 1e-9);
                }
                for (int i = c; i < n; i++)
                {
                    // Along a column, exact: same left edge and width, so x axes line up to the bit.
                    Check.Equal(frames[i - c].Left, frames[i].Left);
                    Check.Equal(frames[i - c].Width, frames[i].Width);
                }
                for (int i = 1; i < n; i++)
                    if (i % c != 0)
                    {
                        // Along a row, exact: same top edge and height.
                        Check.Equal(frames[i - 1].Top, frames[i].Top);
                        Check.Equal(frames[i - 1].Height, frames[i].Height);
                    }
            }
        }

        public void ShareX_UpperPanelsDoNotReserveLabelSpace()
        {
            var panels = new[]
            {
                Panel(new double[] { 0, 10 }, new double[] { 0, 1 }, new SvgChartOptions { XLabel = "time" }),
                Panel(new double[] { 0, 10 }, new double[] { 0, 1 }, new SvgChartOptions { XLabel = "time" }),
            };
            var o = new MultiPanelOptions { ShareX = true, PanelHeight = 200, Gap = 10 };
            var frames = MultiPanel.Layout(panels, o);
            double gapBetweenPlots = frames[1].Top - frames[0].Bottom;
            double unsharedGap = MultiPanel.Layout(panels, new MultiPanelOptions { PanelHeight = 200, Gap = 10 }) is var u ? u[1].Top - u[0].Bottom : 0;
            Check.True(gapBetweenPlots < unsharedGap - 20, $"shared x should tighten the gap ({gapBetweenPlots} vs {unsharedGap})");
            Check.Close(frames[0].Height, frames[1].Height, 1e-9);
        }

        public void ShareY_LabelsOnFirstColumnOnly()
        {
            var panels = new[]
            {
                Panel(new double[] { 0, 1 }, new double[] { 100, 300 }),
                Panel(new double[] { 0, 1 }, new double[] { 200, 500 }),
            };
            var o = new MultiPanelOptions { Columns = 2, ShareY = true };
            var frames = MultiPanel.Layout(panels, o);
            Check.Equal(frames[0].YMin, frames[1].YMin);
            Check.Equal(frames[0].YMax, frames[1].YMax);
            Check.Equal(frames[0].Top, frames[1].Top);
            Check.Equal(1, TextCount(MultiPanel.RenderSvg(panels, o), "300"));
        }

        public void Layout_FramesAreInFigureCoordinates()
        {
            var panels = new[] { Panel(new double[] { 0, 1 }, new double[] { 0, 1 }), Panel(new double[] { 0, 1 }, new double[] { 0, 1 }) };
            var frames = MultiPanel.Layout(panels, new MultiPanelOptions { PanelHeight = 200, Gap = 10 });
            Check.Close(210, frames[1].Top - frames[0].Top);                 // one panel height + gap apart
        }

        public void CallerOptionsAreNotMutated()
        {
            var mine = new SvgChartOptions { Width = 111, XLabel = "t" };
            var panels = new[] { Panel(new double[] { 0, 1 }, new double[] { 0, 1 }, mine), Panel(new double[] { 0, 1 }, new double[] { 0, 1 }, mine) };
            MultiPanel.RenderSvg(panels, new MultiPanelOptions { ShareX = true });
            Check.Equal(111.0, mine.Width);
            Check.Equal("t", mine.XLabel);
            Check.Null(mine.XMin);
            Check.Equal(0.0, mine.MinMarginLeft);
        }

        public void Compose_TilesRasterPanels()
        {
            var red = new ImageBuffer(10, 10, Rgba.Red);
            var blue = new ImageBuffer(10, 6, Rgba.Blue);
            var green = new ImageBuffer(4, 4, Rgba.Green);
            ImageBuffer grid = MultiPanel.Compose(new[] { red, blue, green }, columns: 2, gap: 2, background: Rgba.Black);
            Check.Equal(26, grid.Width);                                     // 2*10 + 3*2
            Check.Equal(26, grid.Height);                                    // 2 rows of 10 + 3*2
            Check.Equal(Rgba.Red, grid.GetPixel(2, 2));
            Check.Equal(Rgba.Blue, grid.GetPixel(14, 2));
            Check.Equal(Rgba.Black, grid.GetPixel(14, 9));                   // below the short blue panel
            Check.Equal(Rgba.Green, grid.GetPixel(2, 14));
            Check.Equal(Rgba.Black, grid.GetPixel(12, 5));                   // gutter
        }

        public void Validation()
        {
            Check.Throws<ArgumentException>(() => MultiPanel.RenderSvg(Array.Empty<ChartPanel>()));
            Check.Throws<ArgumentNullException>(() => MultiPanel.RenderSvg(null!));
            Check.Throws<ArgumentOutOfRangeException>(() =>
                MultiPanel.RenderSvg(new[] { Panel(new double[] { 0 }, new double[] { 0 }) }, new MultiPanelOptions { Columns = 0 }));
            Check.Throws<ArgumentException>(() => MultiPanel.Compose(Array.Empty<ImageBuffer>()));
            Check.Throws<ArgumentOutOfRangeException>(() => MultiPanel.Compose(new[] { new ImageBuffer(1, 1) }, gap: -1));
            Check.Throws<ArgumentNullException>(() => new ChartPanel(null!));
        }
    }
}
