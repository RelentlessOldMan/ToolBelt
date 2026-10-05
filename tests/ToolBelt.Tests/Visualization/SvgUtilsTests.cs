using System;
using System.Globalization;
using System.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    public sealed class SvgUtilsTests
    {
        public void Escape_TextAndAttribute()
        {
            Check.Equal("a&lt;b&amp;c&gt;\"'", SvgUtils.EscapeText("a<b&c>\"'"));
            Check.Equal("a&lt;b&amp;c&gt;&quot;&#39;", SvgUtils.EscapeAttribute("a<b&c>\"'"));
        }

        public void Escape_CleanStringIsReturnedAsIs()
        {
            const string clean = "plain text 123";
            Check.True(ReferenceEquals(clean, SvgUtils.EscapeText(clean)), "no allocation when nothing to escape");
        }

        public void Number_InvariantTrimmedNoNegativeZero()
        {
            Check.Equal("1.5", SvgUtils.Number(1.5));
            Check.Equal("2", SvgUtils.Number(2.0));
            Check.Equal("0.333333", SvgUtils.Number(1.0 / 3));
            Check.Equal("0", SvgUtils.Number(-0.0));
            Check.Equal("0", SvgUtils.Number(-1e-9));   // rounds to "-0" -> normalised
            Check.Equal("-12.25", SvgUtils.Number(-12.25));
        }

        public void Number_IgnoresAmbientCulture()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Check.Equal("1.5", SvgUtils.Number(1.5));
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        public void Number_RejectsNonFinite()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.Number(double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.Number(double.PositiveInfinity));
        }

        public void ColorAndPaintAttributes()
        {
            Check.Equal("#ff0080", SvgUtils.Color(new Rgba(255, 0, 128)));
            Check.Equal("0.502", SvgUtils.Opacity(new Rgba(0, 0, 0, 128)));
            Check.Equal("fill=\"none\"", SvgUtils.FillAttributes(null));
            Check.Equal("fill=\"#0000ff\"", SvgUtils.FillAttributes(Rgba.Blue));
            Check.Equal("fill=\"#0000ff\" fill-opacity=\"0.502\"", SvgUtils.FillAttributes(new Rgba(0, 0, 255, 128)));
            Check.Equal("", SvgUtils.StrokeAttributes(null));
            Check.Equal("stroke=\"#000000\" stroke-width=\"1.5\" stroke-dasharray=\"4 2\"",
                SvgUtils.StrokeAttributes(Rgba.Black, 1.5, "4 2"));
        }

        public void Transforms()
        {
            Check.Equal("translate(10 -5.5)", SvgUtils.Translate(10, -5.5));
            Check.Equal("scale(2 2)", SvgUtils.Scale(2, 2));
            Check.Equal("rotate(-90)", SvgUtils.Rotate(-90));
            Check.Equal("rotate(45 10 20)", SvgUtils.Rotate(45, 10, 20));
            Check.Equal("matrix(1 0 0 1 5 6)", SvgUtils.Matrix(1, 0, 0, 1, 5, 6));
            Check.Equal("translate(1 2) rotate(30)", SvgUtils.Combine(SvgUtils.Translate(1, 2), "", SvgUtils.Rotate(30)));
        }

        public void PathBuilder_Commands()
        {
            string d = new PathBuilder().MoveTo(0, 0).LineTo(10, 0.5).HorizontalTo(20).VerticalTo(5)
                .QuadraticTo(1, 2, 3, 4).CubicTo(1, 2, 3, 4, 5, 6).ArcTo(5, 5, 0, false, true, 9, 9).Close().ToString();
            Check.Equal("M0 0 L10 0.5 H20 V5 Q1 2 3 4 C1 2 3 4 5 6 A5 5 0 0 1 9 9 Z", d);
        }

        public void PathBuilder_FromPoints()
        {
            var pts = new[] { (0.0, 0.0), (1.0, 2.0), (3.0, 1.0) };
            Check.Equal("M0 0 L1 2 L3 1", PathBuilder.FromPoints(pts).ToString());
            Check.Equal("M0 0 L1 2 L3 1 Z", PathBuilder.FromPoints(pts, closed: true).ToString());
            Check.True(PathBuilder.FromPoints(Array.Empty<(double, double)>(), closed: true).IsEmpty);
            Check.Throws<ArgumentOutOfRangeException>(() => new PathBuilder().MoveTo(double.NaN, 0));
        }

        public void TextWidth_FromHelveticaMetrics()
        {
            // H 722 + e 556 + l 222 + l 222 + o 556 = 2278 units -> 22.78 at 10px.
            Check.Close(22.78, SvgUtils.EstimateTextWidth("Hello", 10), 1e-9);
            Check.Close(45.56, SvgUtils.EstimateTextWidth("Hello", 20), 1e-9);   // linear in font size
            Check.Close(0, SvgUtils.EstimateTextWidth("", 12));
            Check.True(SvgUtils.EstimateTextWidth("W", 10) > SvgUtils.EstimateTextWidth("i", 10));
            Check.True(SvgUtils.EstimateTextWidth("Hello", 10, bold: true) > SvgUtils.EstimateTextWidth("Hello", 10));
        }

        public void TextWidth_NonAscii()
        {
            Check.Close(10, SvgUtils.EstimateTextWidth("中", 10), 1e-9);                       // CJK: a full em
            Check.Close(SvgUtils.EstimateTextWidth("e", 10), SvgUtils.EstimateTextWidth("é", 10), 1e-9); // combining mark adds 0
            Check.Close(10, SvgUtils.EstimateTextWidth("\U0001F600", 10), 1e-9);               // surrogate pair counted once
        }
    }
}
