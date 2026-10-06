using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    /// <summary>Regression tests for the 2026-10-05 review findings in Documents and Visualization.</summary>
    public sealed class DocsVizReviewRegressionTests
    {
        public void TimingClock_HugeStartTimeTerminates()
        {
            var t = Task.Run(() => TimingSignal.Clock("clk", 1e-8, 1.7e9, 1.7e9 + 1e-5));
            Check.True(t.Wait(5000), "Clock with an epoch-second start must terminate");
            Check.True(t.Result.Changes.Count >= 2);
            Check.Throws<ArgumentException>(() => TimingSignal.Clock("c", 1, double.NaN, 5));
        }

        public void Svg_XmlIllegalCharactersAreReplaced()
        {
            string svg = SvgChart.Render(new[] { new Series(new[] { 0.0, 1 }, new Rgba(0, 0, 255), name: "ch\u001b[0m \uFFFE \uD800") });
            XDocument.Parse(svg);                                                          // well-formed
            Check.True(svg.Contains("ch�[0m"));
            Check.Equal("a😀b", SvgUtils.EscapeText("a😀b"));                              // valid pairs untouched
        }

        public void Docx_XmlIllegalCharactersAndEmptyTable()
        {
            byte[] docx = new DocxWriter().Paragraph("bad\uFFFEchar \uDC00 ok😀").Build();
            using var zip = new ZipArchive(new MemoryStream(docx));
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
            string xml = reader.ReadToEnd();
            XDocument.Parse(xml);
            Check.True(xml.Contains("ok😀"));
            Check.Throws<ArgumentException>(() => new DocxWriter().Table(Array.Empty<string>(), new List<IReadOnlyList<string>>()));
        }

        private static byte[] HostilePng(int width, int height)
        {
            // Valid signature + IHDR claiming a big RGBA16 image, then a tiny (empty) zlib stream.
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            void Chunk(string type, byte[] data)
            {
                var len = new byte[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
                ms.Write(len);
                var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
                ms.Write(td);
                uint crc = 0xFFFFFFFF;
                foreach (byte b in td) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1; }
                crc ^= 0xFFFFFFFF;
                ms.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc });
            }
            Chunk("IHDR", new byte[] { (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
                                      (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, 8, 6, 0, 0, 0 });
            Chunk("IDAT", new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 });
            Chunk("IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        public void Png_TinyFileClaimingHugeImageDoesNotAllocateIt()
        {
            byte[] bomb = HostilePng(7000, 7000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Check.Throws<InvalidDataException>(() => PngReader.Decode(bomb));
            Check.Throws<ArgumentException>(() => new PdfWriter().ImagePng(bomb));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 16L << 20, $"allocated {allocated / 1048576} MB for a {bomb.Length}-byte file");
        }

        public void Bmp_HostileHeadersGiveInvalidData()
        {
            byte[] good = Bmp.Encode(new ImageBuffer(2, 2), includeAlpha: false);
            byte[] Mutate(int at, int value) { var b = (byte[])good.Clone(); BitConverter.GetBytes(value).CopyTo(b, at); return b; }
            Check.Throws<InvalidDataException>(() => Bmp.Decode(Mutate(10, -100)));        // negative pixel offset
            Check.Throws<InvalidDataException>(() => Bmp.Decode(Mutate(14, 100000)));      // header larger than the file
            var pal = Mutate(28, 8); BitConverter.GetBytes(-1).CopyTo(pal, 46);              // 8-bit with palette count -1
            Check.Throws<InvalidDataException>(() => Bmp.Decode(pal));
            var masks = good.Take(54).ToArray(); BitConverter.GetBytes((short)32).CopyTo(masks, 28); BitConverter.GetBytes(3).CopyTo(masks, 30);
            Check.Throws<InvalidDataException>(() => Bmp.Decode(masks));                    // BI_BITFIELDS with no room for masks
        }

        public void Comparison_ZeroFlagsAnyChangeAndPdfHasNoQuestionMarks()
        {
            var rows = new[] { new ComparisonRow("speed", 100, 110), new ComparisonRow("flat", 5, 5) };
            string md = ReportTemplates.Comparison("C", "a", "b", rows, flagPercent: 0).ToMarkdown();
            Check.True(md.Contains("1 metric(s) changed: speed"), md);
            Check.False(md.Contains("No metric changed"), "a 10% change must not be reported as no change");
            string pdf = Encoding.GetEncoding(28591).GetString(ReportTemplates.Comparison("C", "a", "b", rows).ToPdf());
            Check.True(pdf.Contains("(Change %) Tj"));
            Check.False(pdf.Contains("(?) Tj") || pdf.Contains("(?%) Tj"));
        }

        public void Waterfall_FlatDataHasOneScaleLabel()
        {
            var rows = new double[4, 4];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) rows[r, c] = -120;
            string svg = Waterfall.RenderSvg(rows, new WaterfallOptions());
            XDocument.Parse(svg);
            Check.Equal(1, Regex.Matches(svg, ">-120<").Count);
            Check.False(svg.Contains(">-130<") || svg.Contains(">-110<"), "overprinted tick labels");
        }

        public void Pdf_TableHeaderIsNotOrphanedAndSizesValidated()
        {
            for (int filler = 30; filler <= 46; filler++)
            {
                var w = new PdfWriter(font: PdfFont.Helvetica, fontSize: 11);
                for (int i = 0; i < filler; i++) w.Paragraph("x");
                int pagesBefore = w.PageCount;
                byte[] bytes = w.Table(new[] { "Key", "Value" }, new[] { new[] { "row1", "v" } }).Build();
                string text = Encoding.GetEncoding(28591).GetString(bytes);
                // The header appears exactly once and on the same page as row1.
                Check.Equal(1, Regex.Matches(text, @"\(Key\) Tj").Count);
                var pages = Regex.Split(text, @"/Type /Page\b");
                Check.True(pages.Any(p => p.Contains("(Key) Tj") && p.Contains("(row1) Tj")) || text.IndexOf("(Key) Tj", StringComparison.Ordinal) < text.IndexOf("(row1) Tj", StringComparison.Ordinal),
                    $"filler {filler}");
            }
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Space(double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Heading("h", size: 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Code("c", size: double.PositiveInfinity));
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter().Table(new[] { "a" }, Array.Empty<string[]>(), fontSize: -1));
        }

        public void Pdf_MeasuresWindows1252Characters()
        {
            var w = new PdfWriter(font: PdfFont.Helvetica);
            Check.Close(350 / 1000.0 * 10, w.Measure("•", 10), 1e-12);
            Check.Close(1000 / 1000.0 * 10, w.Measure("—", 10), 1e-12);
            Check.Close(556 / 1000.0 * 10, w.Measure("€", 10), 1e-12);
            Check.Close(667 / 1000.0 * 10, w.Measure("Ä", 10), 1e-12);
        }

        public void HexBin_HonoursExplicitRanges()
        {
            var pts = new List<(double, double)> { (1, 1), (5, 5), (5, 5), (5, 5), (100, 100), (100, 100), (100, 100), (100, 100), (100, 100) };
            string svg = HexBin.RenderSvg(pts, new HexBinOptions { XMin = 0.3, XMax = 9.7, YMin = 0.3, YMax = 9.7 });
            XDocument.Parse(svg);
            Check.False(svg.Contains("<title>5</title>"), "points outside the explicit range must not be binned");
            Check.True(svg.Contains("<title>3</title>"));
            Check.False(Regex.IsMatch(svg, @">10<"), "the axis must not round out past the explicit maximum");
        }
    }
}
