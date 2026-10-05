using System;
using System.Linq;
using System.Xml.Linq;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    public sealed class AnnotationsTests
    {
        // 200x100 plot at the origin showing x,y in [0,10]: 20 px per x unit, 10 px per y unit.
        private static readonly PlotFrame Frame = new PlotFrame(0, 0, 200, 100, 0, 10, 0, 10);

        private static XElement Svg(Annotations a, AnnotationLayer layer = AnnotationLayer.All)
            => XElement.Parse(a.ToSvg(Frame, 11, "sans-serif", layer));

        private static double D(XElement e, string attr) => double.Parse(e.Attribute(attr)!.Value, System.Globalization.CultureInfo.InvariantCulture);

        public void Fluent_CountsAndExtents()
        {
            var a = new Annotations()
                .HorizontalLine(5, "UCL")
                .VerticalBand(2, 4)
                .Callout(1, 7, "peak");
            Check.Equal(3, a.Count);
            var e = a.Extents();
            Check.Equal(1.0, e.XMin);
            Check.Equal(4.0, e.XMax);
            Check.Equal(5.0, e.YMin);
            Check.Equal(7.0, e.YMax);
            Check.Equal((null as double?, null as double?, null as double?, null as double?), new Annotations().Extents());
        }

        public void HorizontalLine_GeometryAndLabel()
        {
            XElement g = Svg(new Annotations().HorizontalLine(5, "UCL"));
            XElement line = g.Elements("line").Single();
            Check.Equal(50.0, D(line, "y1"));
            Check.Equal(50.0, D(line, "y2"));
            Check.Equal(0.0, D(line, "x1"));
            Check.Equal(200.0, D(line, "x2"));
            Check.NotNull(line.Attribute("stroke-dasharray"));      // dashed by default

            XElement text = g.Elements("text").Single();
            Check.Equal("UCL", text.Value);
            Check.Equal("end", text.Attribute("text-anchor")!.Value);
            Check.Equal(196.0, D(text, "x"));
            Check.Equal(46.0, D(text, "y"));                         // just above the line
        }

        public void HorizontalLine_AtTopEdge_LabelGoesBelow()
        {
            XElement text = Svg(new Annotations().HorizontalLine(10, "max")).Elements("text").Single();
            Check.Equal(13.0, D(text, "y"));                         // 0 + 11 + 2
        }

        public void OutOfRangeLines_AreNotDrawn()
        {
            XElement g = Svg(new Annotations().HorizontalLine(20, "off").VerticalLine(-1, "off"));
            Check.Equal(0, g.Elements().Count());
        }

        public void VerticalLine_LabelFlipsNearRightEdge()
        {
            XElement left = Svg(new Annotations().VerticalLine(1, "start")).Elements("text").Single();
            Check.Equal("start", left.Attribute("text-anchor")!.Value);
            XElement right = Svg(new Annotations().VerticalLine(9.8, "deadline")).Elements("text").Single();
            Check.Equal("end", right.Attribute("text-anchor")!.Value);
        }

        public void Bands_AreClampedToThePlot()
        {
            XElement rect = Svg(new Annotations().HorizontalBand(8, 50)).Elements("rect").Single();
            Check.Equal(0.0, D(rect, "y"));                          // top clamped
            Check.Equal(20.0, D(rect, "height"));                    // down to y=8
            Check.Equal(200.0, D(rect, "width"));
            Check.Equal(0, Svg(new Annotations().VerticalBand(11, 12)).Elements("rect").Count());
        }

        public void Callout_MarksPointWithArrowAndLabel()
        {
            XElement g = Svg(new Annotations().Callout(5, 5, "peak", dx: 30, dy: -20));
            XElement dot = g.Elements("circle").Single();
            Check.Equal(100.0, D(dot, "cx"));
            Check.Equal(50.0, D(dot, "cy"));
            Check.Equal(1, g.Elements("polygon").Count());
            Check.Equal("peak", g.Elements("text").Single().Value);
        }

        public void Callout_LabelIsKeptInsideThePlot()
        {
            // A point at the very top-right with an up-right offset would put the label off the chart.
            XElement g = Svg(new Annotations().Callout(10, 10, "peak value", dx: 30, dy: -30));
            XElement text = g.Elements("text").Single();
            double x = D(text, "x"), y = D(text, "y");
            double w = SvgUtils.EstimateTextWidth("peak value", 11);
            string anchor = text.Attribute("text-anchor")!.Value;
            double left = anchor == "end" ? x - w : x, right = anchor == "end" ? x : x + w;
            Check.True(left >= Frame.Left && right <= Frame.Right, $"label spans {left}..{right}");
            Check.True(y - 11 >= Frame.Top && y <= Frame.Bottom, $"label baseline {y}");
        }

        public void Arrow_HeadSitsOnTarget()
        {
            XElement poly = Svg(new Annotations().Arrow(1, 1, 5, 5)).Elements("polygon").Single();
            string first = poly.Attribute("points")!.Value.Split(' ')[0];
            Check.Equal("100,50", first);                            // tip at data (5,5)
        }

        public void Layers_SplitBandsFromForeground()
        {
            var a = new Annotations().HorizontalBand(1, 2).HorizontalLine(5);
            Check.Equal(1, Svg(a, AnnotationLayer.Background).Elements("rect").Count());
            Check.Equal(0, Svg(a, AnnotationLayer.Background).Elements("line").Count());
            Check.Equal(0, Svg(a, AnnotationLayer.Foreground).Elements("rect").Count());
            Check.Equal(1, Svg(a, AnnotationLayer.Foreground).Elements("line").Count());
        }

        public void Labels_AreEscaped()
        {
            XElement g = Svg(new Annotations().HorizontalLine(5, "x < 5 & \"y\" > 2"));
            Check.Equal("x < 5 & \"y\" > 2", g.Elements("text").Single().Value);
        }

        public void Raster_SolidLineAndDashedGaps()
        {
            var image = new ImageBuffer(201, 101, Rgba.White);
            new Annotations().HorizontalLine(5, color: Rgba.Red, dash: LineDash.Solid, width: 1).Draw(image, Frame);
            for (int x = 0; x <= 200; x++) Check.Equal(Rgba.Red, image.GetPixel(x, 50));

            var dashed = new ImageBuffer(201, 101, Rgba.White);
            new Annotations().HorizontalLine(5, color: Rgba.Red, width: 1).Draw(dashed, Frame);
            int red = Enumerable.Range(0, 201).Count(x => dashed.GetPixel(x, 50) == Rgba.Red);
            Check.True(red > 60 && red < 201, $"dashed line should have gaps ({red} red pixels)");
        }

        public void Raster_BandBlendsOverExistingPixels()
        {
            var image = new ImageBuffer(201, 101, Rgba.White);
            new Annotations().HorizontalBand(0, 10, fill: new Rgba(0, 0, 255, 128)).Draw(image, Frame);
            Check.Equal(new Rgba(127, 127, 255), image.GetPixel(100, 50));
        }

        public void Raster_CalloutMarkerAndOffRangeSkipped()
        {
            var image = new ImageBuffer(201, 101, Rgba.White);
            new Annotations()
                .Callout(5, 5, "pt", color: Rgba.Blue)
                .HorizontalLine(50, color: Rgba.Red)       // off range: nothing
                .Draw(image, Frame);
            Check.Equal(Rgba.Blue, image.GetPixel(100, 50));
            for (int x = 0; x <= 200; x++)
                for (int y = 0; y <= 100; y++)
                    Check.False(image.GetPixel(x, y) == Rgba.Red);
        }

        public void Raster_LinesUpWithLinePlotFrame()
        {
            var series = new[] { new Series(new double[] { 0, 2, 4, 6, 8, 10 }, Rgba.Black) };
            ImageBuffer plot = LinePlot.Render(120, 80, series);
            PlotFrame frame = LinePlot.Frame(120, 80, series);
            new Annotations().HorizontalLine(5, color: Rgba.Red, dash: LineDash.Solid, width: 1).Draw(plot, frame);
            int row = (int)Math.Round(frame.MapY(5), MidpointRounding.AwayFromZero);
            Check.Equal(Rgba.Red, plot.GetPixel(60, row));
            Check.Close(10, frame.Left);
            Check.Close(0, frame.YMin);
            Check.Close(10, frame.YMax);
        }

        public void Validation()
        {
            var a = new Annotations();
            Check.Throws<ArgumentOutOfRangeException>(() => a.HorizontalLine(double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => a.VerticalLine(1, width: 0));
            Check.Throws<ArgumentNullException>(() => a.Callout(1, 1, null!));
            Check.Throws<ArgumentOutOfRangeException>(() => a.ToSvg(Frame, 0));
            Check.Throws<ArgumentNullException>(() => a.Draw(null!, Frame));
            Check.Equal(0, a.Count);
        }
    }
}
