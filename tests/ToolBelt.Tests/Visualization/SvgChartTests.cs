using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    public sealed class SvgChartTests
    {
        private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

        private static Series Line(double[] y, string? name = null, double[]? x = null)
            => new Series(y, new Rgba(31, 119, 180), PlotStyle.Line, x, name);

        private static XDocument Parse(string svg) => XDocument.Parse(svg);

        private static string[] Texts(XDocument doc) => doc.Descendants(Ns + "text").Select(t => t.Value).ToArray();

        public void Render_IsWellFormedWithEverythingOn()
        {
            var a = new Annotations().HorizontalLine(8, "UCL").HorizontalBand(2, 4, "spec").Callout(3, 9, "peak");
            string svg = SvgChart.Render(
                new[] { Line(new double[] { 1, 5, 9, 4, 6 }, "measured"), new Series(new double[] { 2, 3, 2, 3, 2 }, Rgba.Red, PlotStyle.Scatter, null, "ref") },
                new SvgChartOptions { Title = "Run 42", XLabel = "sample", YLabel = "volts", Annotations = a });
            XDocument doc = Parse(svg);
            Check.Equal("svg", doc.Root!.Name.LocalName);
            string[] texts = Texts(doc);
            foreach (string expected in new[] { "Run 42", "sample", "volts", "measured", "ref", "UCL", "spec", "peak" })
                Check.True(texts.Contains(expected), $"missing text '{expected}'");
            Check.True(svg.StartsWith("<?xml", StringComparison.Ordinal));
            Check.False(svg.Contains("NaN"));
        }

        public void Text_IsEscaped()
        {
            XDocument doc = Parse(SvgChart.Render(new[] { Line(new double[] { 1, 2 }, "a<b") },
                new SvgChartOptions { Title = "A < B & \"C\"" }));
            Check.True(Texts(doc).Contains("A < B & \"C\""));
            Check.True(Texts(doc).Contains("a<b"));
        }

        public void AutoRange_RoundsOutToNiceTicks()
        {
            PlotFrame f = SvgChart.Layout(new[] { Line(new[] { 0.13, 0.5, 0.97 }) });
            Check.Close(0, f.YMin);
            Check.Close(1, f.YMax);
            Check.Close(0, f.XMin);
            Check.Close(2, f.XMax);
        }

        public void AutoRange_IncludesAnnotationsAndBarBaseline()
        {
            var opts = new SvgChartOptions { Annotations = new Annotations().HorizontalLine(3, "limit") };
            Check.True(SvgChart.Layout(new[] { Line(new[] { 0.1, 0.9 }) }, opts).YMax >= 3, "limit line must be in range");

            var bars = new[] { new Series(new double[] { 5, 7, 9 }, Rgba.Blue, PlotStyle.Bar) };
            Check.Close(0, SvgChart.Layout(bars).YMin);
        }

        public void ExplicitLimits_AreHonouredExactly()
        {
            PlotFrame f = SvgChart.Layout(new[] { Line(new double[] { 0, 10, 20 }) },
                new SvgChartOptions { XMin = 0.5, XMax = 1.5, YMin = 4, YMax = 12 });
            Check.Equal(0.5, f.XMin);
            Check.Equal(1.5, f.XMax);
            Check.Equal(4.0, f.YMin);
            Check.Equal(12.0, f.YMax);
        }

        public void Lines_AreClippedToThePlotArea()
        {
            var y = Enumerable.Range(0, 101).Select(i => (double)i).ToArray();
            var opts = new SvgChartOptions { YMin = 40, YMax = 60, XMin = 10, XMax = 90 };
            PlotFrame f = SvgChart.Layout(new[] { Line(y) }, opts);
            XDocument doc = Parse(SvgChart.Render(new[] { Line(y) }, opts));
            string d = doc.Descendants(Ns + "path").Single().Attribute("d")!.Value;
            var nums = Regex.Matches(d, @"-?\d+(\.\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            Check.True(nums.Length >= 4 && nums.Length % 2 == 0);
            for (int i = 0; i < nums.Length; i += 2)
            {
                Check.True(nums[i] >= f.Left - 1e-6 && nums[i] <= f.Right + 1e-6, $"x {nums[i]} outside");
                Check.True(nums[i + 1] >= f.Top - 1e-6 && nums[i + 1] <= f.Bottom + 1e-6, $"y {nums[i + 1]} outside");
            }
        }

        public void NaN_BreaksTheLine()
        {
            XDocument doc = Parse(SvgChart.Render(new[] { Line(new[] { 1, 2, double.NaN, 4, 5 }) }));
            string d = doc.Descendants(Ns + "path").Single().Attribute("d")!.Value;
            Check.Equal(2, d.Count(c => c == 'M'));
        }

        public void LogAxis_DecadeLabelsAndSkipsNonPositive()
        {
            var opts = new SvgChartOptions { YAxis = AxisKind.Log };
            string[] texts = Texts(Parse(SvgChart.Render(new[] { Line(new double[] { 1, 0, -5, 1000 }) }, opts)));
            foreach (string label in new[] { "1", "10", "100", "1000" })
                Check.True(texts.Contains(label), $"missing decade {label}");
        }

        public void TimeAxis_ClockLabels()
        {
            var x = new double[] { 0, 300, 600 };
            string[] texts = Texts(Parse(SvgChart.Render(new[] { Line(new double[] { 1, 2, 3 }, x: x) },
                new SvgChartOptions { XAxis = AxisKind.Time })));
            Check.True(texts.Contains("0:00") && texts.Contains("10:00"), string.Join("|", texts));
        }

        public void Legend_OnlyWhenNamed()
        {
            Check.Equal(0, Parse(SvgChart.Render(new[] { Line(new double[] { 1, 2 }) }))
                .Descendants(Ns + "g").Count(g => (string?)g.Attribute("class") == "legend"));
            Check.Equal(1, Parse(SvgChart.Render(new[] { Line(new double[] { 1, 2 }, "named") }))
                .Descendants(Ns + "g").Count(g => (string?)g.Attribute("class") == "legend"));
        }

        public void Annotations_UseTheLayoutFrame()
        {
            var series = new[] { Line(new double[] { 0, 10 }) };
            var opts = new SvgChartOptions { Annotations = new Annotations().HorizontalLine(5, "mid") };
            PlotFrame f = SvgChart.Layout(series, opts);
            XDocument doc = Parse(SvgChart.Render(series, opts));
            XElement line = doc.Descendants(Ns + "g").Where(g => (string?)g.Attribute("class") == "annotations")
                .SelectMany(g => g.Elements(Ns + "line")).Single();
            Check.Equal(SvgUtils.Number(f.MapY(5)), line.Attribute("y1")!.Value);
        }

        public void RenderElement_IsNestable()
        {
            string el = SvgChart.RenderElement(new[] { Line(new double[] { 1, 2 }) }, null, 20, 30);
            Check.True(el.StartsWith("<svg", StringComparison.Ordinal));
            XElement root = XElement.Parse(el);
            Check.Equal("20", root.Attribute("x")!.Value);
            Check.Equal("30", root.Attribute("y")!.Value);
        }

        public void EmptyAndDeterministic()
        {
            Check.NotNull(Parse(SvgChart.Render(Array.Empty<Series>())));
            var s = new[] { Line(new double[] { 3, 1, 4, 1, 5, 9, 2, 6 }, "pi") };
            Check.Equal(SvgChart.Render(s), SvgChart.Render(s));
        }

        public void Validation()
        {
            Check.Throws<ArgumentNullException>(() => SvgChart.Render(null!));
            Check.Throws<ArgumentException>(() => SvgChart.Render(new[] { Line(new double[] { 1 }) }, new SvgChartOptions { Width = 60, Height = 40 }));
            Check.Throws<ArgumentOutOfRangeException>(() =>
                SvgChart.Render(new[] { Line(new double[] { 1 }) }, new SvgChartOptions { YAxis = AxisKind.Log, YMin = 0, YMax = 10 }));
        }
    }
}
