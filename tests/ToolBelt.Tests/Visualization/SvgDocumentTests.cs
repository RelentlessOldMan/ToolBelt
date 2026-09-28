using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class SvgDocumentTests
    {
        // Independent validation: parse the emitted SVG as XML and inspect it structurally.
        private static XElement Parse(string svg) => XDocument.Parse(svg).Root!;

        public void RootHasSizeAndViewBox()
        {
            var root = Parse(new SvgDocument(200, 100).ToString());
            Check.Equal("svg", root.Name.LocalName);
            Check.Equal("200", root.Attribute("width")!.Value);
            Check.Equal("100", root.Attribute("height")!.Value);
            Check.Equal("0 0 200 100", root.Attribute("viewBox")!.Value);
        }

        public void ShapesEmitElements()
        {
            var svg = new SvgDocument(100, 100)
                .Rectangle(1, 2, 10, 20, fill: "red")
                .Line(0, 0, 100, 100, stroke: "blue", strokeWidth: 2)
                .Circle(50, 50, 5, fill: "green")
                .ToString();
            var root = Parse(svg);

            var rect = root.Elements().First(e => e.Name.LocalName == "rect");
            Check.Equal("1", rect.Attribute("x")!.Value);
            Check.Equal("red", rect.Attribute("fill")!.Value);

            var line = root.Elements().First(e => e.Name.LocalName == "line");
            Check.Equal("blue", line.Attribute("stroke")!.Value);
            Check.Equal("2", line.Attribute("stroke-width")!.Value);

            var circle = root.Elements().First(e => e.Name.LocalName == "circle");
            Check.Equal("5", circle.Attribute("r")!.Value);
        }

        public void PolylineHasNoFillByDefault()
        {
            var svg = new SvgDocument(50, 50)
                .Polyline(new[] { (0.0, 0.0), (10.0, 20.0), (30.0, 5.0) }, stroke: "black").ToString();
            var poly = Parse(svg).Elements().First(e => e.Name.LocalName == "polyline");
            Check.Equal("none", poly.Attribute("fill")!.Value);
            Check.Equal("0,0 10,20 30,5", poly.Attribute("points")!.Value);
        }

        public void Polygon_HonorsFill()
        {
            var svg = new SvgDocument(50, 50)
                .Polygon(new[] { (0.0, 0.0), (10.0, 0.0), (5.0, 10.0) }, fill: "orange").ToString();
            var poly = Parse(svg).Elements().First(e => e.Name.LocalName == "polygon");
            Check.Equal("orange", poly.Attribute("fill")!.Value);
        }

        public void Text_IsEscaped()
        {
            var svg = new SvgDocument(100, 100).Text(5, 15, "a<b>&c", anchor: "middle").ToString();
            var text = Parse(svg).Elements().First(e => e.Name.LocalName == "text");
            Check.Equal("a<b>&c", text.Value); // XML parser round-trips the escaped entities back
            Check.Equal("middle", text.Attribute("text-anchor")!.Value);
        }

        public void CoordinatesUseInvariantCulture()
        {
            // Under a comma-decimal culture, coordinates must still use '.' so the SVG stays valid.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var svg = new SvgDocument(10, 10).Circle(1.5, 2.25, 0.75).ToString();
                Check.True(svg.Contains("cx=\"1.5\""), svg);
                Check.True(svg.Contains("r=\"0.75\""), svg);
                Parse(svg); // must still be valid XML/numbers
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        public void WholeDocumentIsValidXml()
        {
            var svg = new SvgDocument(120, 80)
                .Rectangle(0, 0, 120, 80, fill: "#eee")
                .Path("M0 0 L120 80", stroke: "black", fill: null)
                .Text(10, 40, "hello")
                .ToString();
            var root = Parse(svg); // throws if malformed
            Check.Equal(3, root.Elements().Count());
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(0, 10));
            Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(10, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(10, 10).Circle(0, 0, -1));
            Check.Throws<ArgumentNullException>(() => new SvgDocument(10, 10).Text(0, 0, null!));
        }
    }
}
