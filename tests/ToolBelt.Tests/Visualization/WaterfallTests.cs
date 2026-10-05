using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    public sealed class WaterfallTests
    {
        private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

        // rows = time, cols = frequency; value = row index, so the newest row (last) is the maximum.
        private static double[,] Ramp(int rows, int cols)
        {
            var d = new double[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    d[r, c] = r;
            return d;
        }

        private static double Num(XElement e, string a) => double.Parse(e.Attribute(a)!.Value, CultureInfo.InvariantCulture);

        public void Svg_WellFormedWithAxesScaleAndTitle()
        {
            var o = new WaterfallOptions { FrequencyStep = 100, TimeStep = 1, Title = "Spectrum <live>", ValueLabel = "dB" };
            XDocument doc = XDocument.Parse(Waterfall.RenderSvg(Ramp(120, 11), o));
            string[] texts = doc.Descendants(Ns + "text").Select(t => t.Value).ToArray();
            foreach (string s in new[] { "Spectrum <live>", "Frequency", "Time", "dB", "0", "1000", "0:00", "2:00" })
                Check.True(texts.Contains(s), $"missing '{s}' in [{string.Join("|", texts)}]");
            XElement scale = doc.Descendants(Ns + "g").Single(g => (string?)g.Attribute("class") == "colorscale");
            Check.Equal(64, scale.Elements(Ns + "rect").Count());
        }

        public void Svg_EmbedsExactlyTheRasterCells()
        {
            double[,] data = Ramp(7, 5);
            var o = new WaterfallOptions();
            XElement image = XDocument.Parse(Waterfall.RenderSvg(data, o)).Descendants(Ns + "image").Single();
            string expected = "data:image/png;base64," + Convert.ToBase64String(
                PngWriter.Encode(Waterfall.RenderImage(data, new WaterfallOptions { RasterColorbar = false })));
            Check.Equal(expected, image.Attribute("href")!.Value);
        }

        public void Svg_ImageFillsTheLayoutFrame()
        {
            double[,] data = Ramp(10, 10);
            PlotFrame f = Waterfall.Layout(data);
            XElement image = XDocument.Parse(Waterfall.RenderSvg(data)).Descendants(Ns + "image").Single();
            Check.Close(f.Left, Num(image, "x"), 1e-6);
            Check.Close(f.Top, Num(image, "y"), 1e-6);
            Check.Close(f.Width, Num(image, "width"), 1e-6);
            Check.Close(f.Height, Num(image, "height"), 1e-6);
        }

        public void Layout_RangesUseBinCentresAndRowTimes()
        {
            var o = new WaterfallOptions { FrequencyStart = 1000, FrequencyStep = 50, TimeStart = 10, TimeStep = 0.5 };
            PlotFrame f = Waterfall.Layout(Ramp(20, 9), o);
            Check.Close(975, f.XMin);                 // first centre − half a bin
            Check.Close(1425, f.XMax);                // 1000 + 8.5·50
            Check.Close(10, f.YMin);                  // newest at top: time grows upward
            Check.Close(20, f.YMax);                  // 10 + 20·0.5
            Check.True(f.MapY(20) < f.MapY(10), "later times are higher");

            PlotFrame down = Waterfall.Layout(Ramp(20, 9), new WaterfallOptions { TimeStart = 10, TimeStep = 0.5, NewestAtTop = false });
            Check.True(down.MapY(20) > down.MapY(10), "history scrolls downward");
        }

        public void RenderImage_NewestRowOnTop()
        {
            var o = new WaterfallOptions { RasterColorbar = false };
            ImageBuffer img = Waterfall.RenderImage(Ramp(4, 3), o);
            Check.Equal(3, img.Width);
            Check.Equal(4, img.Height);
            Check.Equal(o.Colormap.Map(1), img.GetPixel(0, 0));      // newest (max) at the top
            Check.Equal(o.Colormap.Map(0), img.GetPixel(0, 3));      // oldest at the bottom

            ImageBuffer flipped = Waterfall.RenderImage(Ramp(4, 3), new WaterfallOptions { RasterColorbar = false, NewestAtTop = false });
            Check.Equal(o.Colormap.Map(0), flipped.GetPixel(0, 0));
        }

        public void RenderImage_CellSizeAndColorbarStrip()
        {
            ImageBuffer img = Waterfall.RenderImage(Ramp(4, 3), new WaterfallOptions { CellSize = 5, ColorbarWidth = 10 });
            Check.Equal(3 * 5 + 4 + 10, img.Width);
            Check.Equal(20, img.Height);
            Check.Equal(Colormap.Viridis.Map(1), img.GetPixel(15 + 4 + 5, 0));   // strip: high value at the top
        }

        public void ValueRange_SkipsNonFiniteAndHonoursOverrides()
        {
            var d = new double[,] { { -3, double.NegativeInfinity }, { double.NaN, 7 } };
            Check.Equal((-3.0, 7.0), Waterfall.ValueRange(d));
            Check.Equal((-3.0, 0.0), Waterfall.ValueRange(d, new WaterfallOptions { ValueMax = 0 }));
            Check.Equal((0.0, 1.0), Waterfall.ValueRange(new double[,] { { double.NaN } }));

            // −∞ cells take the low end color rather than breaking the scale.
            ImageBuffer img = Waterfall.RenderImage(d, new WaterfallOptions { RasterColorbar = false, NewestAtTop = false });
            Check.Equal(Colormap.Viridis.Map(0), img.GetPixel(1, 0));
            Check.Equal(Rgba.Transparent, img.GetPixel(0, 1));                   // NaN
        }

        public void Annotations_AlignWithFrequencyAxis()
        {
            double[,] data = Ramp(10, 21);
            var o = new WaterfallOptions { FrequencyStep = 50, Annotations = new Annotations().VerticalLine(500, "carrier") };
            PlotFrame f = Waterfall.Layout(data, o);
            XElement line = XDocument.Parse(Waterfall.RenderSvg(data, o)).Descendants(Ns + "g")
                .Single(g => (string?)g.Attribute("class") == "annotations").Elements(Ns + "line").Single();
            Check.Equal(SvgUtils.Number(f.MapX(500)), line.Attribute("x1")!.Value);
        }

        public void Validation()
        {
            Check.Throws<ArgumentNullException>(() => Waterfall.RenderSvg(null!));
            Check.Throws<ArgumentException>(() => Waterfall.RenderSvg(new double[0, 3]));
            Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderSvg(Ramp(2, 2), new WaterfallOptions { FrequencyStep = 0 }));
            Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderSvg(Ramp(2, 2), new WaterfallOptions { TimeStep = double.NaN }));
            Check.Throws<ArgumentOutOfRangeException>(() => Waterfall.RenderImage(Ramp(2, 2), new WaterfallOptions { CellSize = 0 }));
            Check.Throws<ArgumentException>(() => Waterfall.RenderSvg(Ramp(2, 2), new WaterfallOptions { Width = 80, Height = 60 }));
        }
    }
}
