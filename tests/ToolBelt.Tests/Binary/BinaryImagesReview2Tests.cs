using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    // Regressions from review round 28 (binary formats & images, second pass).
    public sealed class BinaryImagesReview2Tests
    {
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        public void DrawLine_OffCanvasEndKeepsTheVisiblePixels()
        {
            var im = new ImageBuffer(10, 10);
            im.DrawLine(-60, -1, 9, 0, Rgba.White);
            for (int x = 0; x < 10; x++) Check.Equal(Rgba.White, im.GetPixel(x, 0));   // only (9,0) was lit
        }

        public void DrawLine_ClippedMatchesTheSameLineOnABiggerCanvas()
        {
            var rng = new Random(28);
            for (int n = 0; n < 2000; n++)
            {
                int x0 = rng.Next(-15, 25), y0 = rng.Next(-15, 25), x1 = rng.Next(-15, 25), y1 = rng.Next(-15, 25);
                var small = new ImageBuffer(10, 10);
                var big = new ImageBuffer(40, 40);
                small.DrawLine(x0, y0, x1, y1, Rgba.White);
                big.DrawLine(x0 + 15, y0 + 15, x1 + 15, y1 + 15, Rgba.White);
                for (int y = 0; y < 10; y++)
                    for (int x = 0; x < 10; x++)
                        if (small.GetPixel(x, y) != big.GetPixel(x + 15, y + 15))
                            throw new Exception($"Line ({x0},{y0})-({x1},{y1}) differs at ({x},{y}).");
            }
        }

        public void DrawLine_FarEndsStillCheap()
        {
            var im = new ImageBuffer(8, 8);
            im.DrawLine(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue, Rgba.White);
            Check.Equal(Rgba.White, im.GetPixel(3, 3));
            im.DrawLine(int.MaxValue, 0, int.MinValue, 1, Rgba.Red);
        }

        public void ImageBuffer_PastTheArrayLimitIsTooLarge()
        {
            // 2,147,483,592 bytes: under int.MaxValue but over the largest array, so it was OutOfMemoryException.
            Check.Throws<ArgumentOutOfRangeException>(() => new ImageBuffer(1, 536870898));
        }

        public void Blit_AtExtremeOffsetsClips()
        {
            var dst = new ImageBuffer(4, 4);
            var src = new ImageBuffer(2, 2, Rgba.Red);
            dst.Blit(src, int.MaxValue, 0);                                     // overflowed in a checked build
            dst.Blit(src, int.MinValue, int.MinValue);
            dst.Blit(src, 0, int.MaxValue);
            Check.Equal(Rgba.Transparent, dst.GetPixel(0, 0));
            dst.Blit(src, -1, 3);
            Check.Equal(Rgba.Red, dst.GetPixel(0, 3));
            Check.Equal(Rgba.Transparent, dst.GetPixel(1, 3));
        }

        public void DrawLineClipped_HugeRectangleDoesNotOverflow()
        {
            var im = new ImageBuffer(4, 4);
            im.DrawLineClipped(1e18, 1, -1e18, 2, Rgba.Red, 0, 0, int.MaxValue, 3);
            Check.True(Enumerable.Range(0, 4).Any(x => im.GetPixel(x, 1) == Rgba.Red || im.GetPixel(x, 2) == Rgba.Red));
        }

        public void SvgUtils_EscapeAttributeKeepsWhitespace()
        {
            const string value = "a\nb\tc\rd";
            var r = XmlReader.Create(new StringReader("<r a=\"" + SvgUtils.EscapeAttribute(value) + "\">" + SvgUtils.EscapeText("x\r\ny") + "</r>"));
            r.Read();
            Check.Equal(value, r.GetAttribute("a"));                          // was "a b c d"
            Check.Equal("x\r\ny", r.ReadElementContentAsString());           // was "x\ny"
        }

        public void SvgDocument_EscapingKeepsWhitespace()
        {
            string svg = new SvgDocument(10, 10).Text(0, 0, "a\r\nb", fontFamily: "x\ty").ToString();
            var doc = new XmlDocument();
            doc.Load(XmlReader.Create(new StringReader(svg)));                 // LoadXml skips the normalization
            var text = (XmlElement)doc.DocumentElement!.FirstChild!;
            Check.Equal("x\ty", text.GetAttribute("font-family"));
            Check.Equal("a\r\nb", text.InnerText);
        }

        public void QuotedPrintable_FinalEqualsIsASoftBreak()
        {
            Check.True(QuotedPrintable.Decode("a=").SequenceEqual(Ascii("a")));   // threw; "a= " already worked
            Check.True(QuotedPrintable.Decode("a= ").SequenceEqual(Ascii("a")));
            Check.True(QuotedPrintable.Decode("=").Length == 0);
            Check.Throws<FormatException>(() => QuotedPrintable.Decode("a=4"));
        }

        public void QuotedPrintable_LongBlankRunIsLinear()
        {
            string text = new string(' ', 200_000) + "x" + new string('\t', 200_000) + "y";
            var sw = Stopwatch.StartNew();
            byte[] bytes = QuotedPrintable.Decode(text);
            sw.Stop();
            Check.Equal(400_002, bytes.Length);
            Check.Equal((byte)'x', bytes[200_000]);
            Check.Equal((byte)'\t', bytes[200_001]);
            Check.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");   // tens of seconds before
        }

        public void SvgDocument_RejectsNegativeLengths()
        {
            var doc = new SvgDocument(10, 10).Circle(1, 1, 1);
            string before = doc.ToString();
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Rectangle(0, 0, -5, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Rectangle(0, 0, 1, -5));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Rectangle(0, 0, 1, 1, cornerRadius: double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Rectangle(0, 0, 1, 1, stroke: "red", strokeWidth: -2));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Circle(0, 0, 1, stroke: "red", strokeWidth: -2));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Line(0, 0, 1, 1, strokeWidth: -1));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Polyline(new[] { (0.0, 0.0), (1.0, 1.0) }, strokeWidth: -1));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Path("M0 0", stroke: "red", strokeWidth: -1));
            Check.Throws<ArgumentOutOfRangeException>(() => doc.Text(0, 0, "a", fontSize: -3));
            Check.Equal(before, doc.ToString());                               // nothing half-written
            doc.Rectangle(0, 0, 0, 0, strokeWidth: -1);                        // zero size is legal; no stroke, no width
        }
    }
}
