using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;
using ToolBelt.Tests.Visualization;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Documents
{
    /// <summary>PdfWriter tables, images, outline, page numbers, encoding and measurement.</summary>
    public sealed class PdfFeaturesTests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        // ---------- a small PDF reader for the tests ----------

        private sealed class Pdf
        {
            public readonly byte[] Bytes;
            public readonly string Text;
            public readonly Dictionary<int, int> Offsets = new Dictionary<int, int>();

            public Pdf(byte[] bytes)
            {
                Bytes = bytes;
                Text = Latin1.GetString(bytes);
                int xref = int.Parse(Regex.Match(Text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value, CultureInfo.InvariantCulture);
                Check.True(Text.Substring(xref).StartsWith("xref\n", StringComparison.Ordinal), "startxref lands on xref");
                var header = Regex.Match(Text.Substring(xref), @"^xref\n0 (\d+)\n");
                int count = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                int p = xref + header.Length + 20;                              // skip the free entry
                for (int id = 1; id < count; id++, p += 20)
                {
                    int off = int.Parse(Text.Substring(p, 10), CultureInfo.InvariantCulture);
                    Check.True(Text.Substring(off).StartsWith(id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n", StringComparison.Ordinal), $"xref entry {id}");
                    Offsets[id] = off;
                }
            }

            /// <summary>The dictionary text of object <paramref name="id"/>.</summary>
            public string Dict(int id)
            {
                int start = Text.IndexOf("<<", Offsets[id], StringComparison.Ordinal);
                int depth = 0;
                for (int i = start; i < Text.Length - 1; i++)
                {
                    if (Text[i] == '<' && Text[i + 1] == '<') { depth++; i++; }
                    else if (Text[i] == '>' && Text[i + 1] == '>') { depth--; i++; if (depth == 0) return Text.Substring(start, i - start + 1); }
                }
                throw new InvalidDataException("unterminated dictionary");
            }

            public byte[] StreamData(int id)
            {
                string dict = Dict(id);
                int length = int.Parse(Regex.Match(dict, @"/Length (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
                int s = Text.IndexOf("stream\n", Offsets[id], StringComparison.Ordinal) + 7;
                Check.True(Text.Substring(s + length).StartsWith("\nendstream", StringComparison.Ordinal), $"/Length of object {id} is exact");
                return Bytes.Skip(s).Take(length).ToArray();
            }

            public static int? Ref(string dict, string key)
            {
                var m = Regex.Match(dict, "/" + key + @" (\d+) 0 R");
                return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
            }

            public List<int> PageIds()
            {
                string kids = Regex.Match(Dict(2), @"/Kids \[([^\]]*)\]").Groups[1].Value;
                return Regex.Matches(kids, @"(\d+) 0 R").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            }

            public string PageContent(int pageIndex) => Latin1.GetString(StreamData(Ref(Dict(PageIds()[pageIndex]), "Contents")!.Value));
        }

        private static byte[] Inflate(byte[] zlib)
        {
            using var ds = new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            ds.CopyTo(ms);
            return ms.ToArray();
        }

        private static string DecodeTextString(string hex)
        {
            Check.True(hex.StartsWith("<FEFF", StringComparison.Ordinal), hex);
            var sb = new StringBuilder();
            for (int i = 5; i + 4 <= hex.Length - 1; i += 4) sb.Append((char)int.Parse(hex.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ---------- tests ----------

        public void Table_RepeatsHeaderOnEveryPage()
        {
            var rows = Enumerable.Range(0, 120).Select(i => (IReadOnlyList<string>)new[] { "row" + i, "v" + i }).ToList();
            var pdf = new Pdf(new PdfWriter(font: PdfFont.Helvetica).Table(new[] { "Key", "Value" }, rows).Build());
            int pages = pdf.PageIds().Count;
            Check.True(pages >= 3, $"{pages} pages");
            for (int p = 0; p < pages; p++)
                Check.Equal(1, Regex.Matches(pdf.PageContent(p), @"/F2 [\d.]+ Tf [\d.]+ [\d.]+ Td \(Key\) Tj").Count);   // bold header, once per page
            string all = string.Concat(Enumerable.Range(0, pages).Select(pdf.PageContent));
            for (int i = 0; i < 120; i++) Check.True(all.Contains("(row" + i.ToString(CultureInfo.InvariantCulture) + ") Tj"), "row " + i);
        }

        public void Table_WrapsCellsWithinTheirColumns()
        {
            var w = new PdfWriter(font: PdfFont.Helvetica, fontSize: 10);
            string longText = string.Join(" ", Enumerable.Repeat("wrapping words", 30));
            var pdf = new Pdf(w.Table(new[] { "A", "B" }, new[] { new[] { "x", longText } }, columnWeights: new[] { 1.0, 1.0 }).Build());
            string content = pdf.PageContent(0);
            double columnWidth = (612 - 108) / 2.0 - 6;
            var lines = Regex.Matches(content, @"\(([^)]*wrapping[^)]*)\) Tj").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Check.True(lines.Count > 3, "cell text wrapped onto several lines");
            foreach (string line in lines) Check.True(w.Measure(line, 9) <= columnWidth + 1e-9, $"'{line}' overflows its column");
            Check.Throws<ArgumentException>(() => new PdfWriter().Table(new[] { "A" }, Array.Empty<string[]>(), new[] { 0.0 }));
        }

        public void Paragraph_LinesNeverExceedTheTextWidth()
        {
            foreach (PdfFont font in new[] { PdfFont.Helvetica, PdfFont.TimesRoman, PdfFont.Courier, PdfFont.HelveticaBold })
            {
                var w = new PdfWriter(font: font, fontSize: 11);
                var rng = new ToolBelt.Numerics.DeterministicRandom((int)font);
                string text = string.Join(" ", Enumerable.Range(0, 400).Select(_ => new string('a', rng.Next(1, 12)) + "WM.il"));
                string content = new Pdf(w.Paragraph(text).Build()).PageContent(0);
                foreach (Match m in Regex.Matches(content, @"\(([^)]*)\) Tj"))
                    Check.True(w.Measure(m.Groups[1].Value, 11) <= 612 - 108 + 1e-9, $"{font}: line too wide");
            }
        }

        public void Measure_AgreesWithTheIndependentHelveticaTable()
        {
            var w = new PdfWriter(font: PdfFont.Helvetica);
            foreach (string s in new[] { "Hello, World!", "The quick brown fox jumps over the lazy dog 0123456789", "{[(|)]}~@#$%^&*" })
                Check.Close(SvgUtils.EstimateTextWidth(s, 12), w.Measure(s, 12), 1e-9, s);
            Check.Close(600 * 5 / 1000.0 * 10, new PdfWriter(font: PdfFont.Courier).Measure("abcde", 10), 1e-12);
            Check.True(new PdfWriter(font: PdfFont.Helvetica).Measure("W", 10, bold: true) > new PdfWriter(font: PdfFont.Helvetica).Measure("i", 10, bold: true));
        }

        public void Images_WithAndWithoutTransparency()
        {
            var opaque = new ImageBuffer(4, 3, new Rgba(10, 20, 30));
            var clear = new ImageBuffer(5, 2, new Rgba(200, 100, 50, 128));
            clear.SetPixel(1, 1, new Rgba(1, 2, 3, 0));
            var pdf = new Pdf(new PdfWriter().ImagePng(PngWriter.Encode(opaque)).Image(5, 2, clear.Pixels).Build());

            var images = pdf.Offsets.Keys.Where(id => pdf.Dict(id).Contains("/Subtype /Image") && pdf.Dict(id).Contains("/DeviceRGB")).ToList();
            Check.Equal(2, images.Count);
            string first = pdf.Dict(images[0]), second = pdf.Dict(images[1]);
            Check.True(first.Contains("/Width 4 /Height 3") && Pdf.Ref(first, "SMask") == null, "opaque image has no soft mask");
            byte[] rgb = Inflate(pdf.StreamData(images[0]));
            Check.Equal(4 * 3 * 3, rgb.Length);
            Check.True(rgb.Where((b, i) => b != new byte[] { 10, 20, 30 }[i % 3]).Count() == 0, "pixels survive");
            int mask = Pdf.Ref(second, "SMask")!.Value;
            Check.True(pdf.Dict(mask).Contains("/DeviceGray"));
            byte[] alpha = Inflate(pdf.StreamData(mask));
            Check.Equal(128, (int)alpha[0]);
            Check.Equal(0, (int)alpha[1 * 5 + 1]);
            Check.True(pdf.PageContent(0).Contains("/Im1 Do") && pdf.PageContent(0).Contains("/Im2 Do"));
            Check.True(pdf.Dict(pdf.PageIds()[0]).Contains("/XObject << /Im1 "));
        }

        public void Images_ValidationAndUnsupportedPng()
        {
            Check.Throws<ArgumentException>(() => new PdfWriter().Image(2, 2, new byte[3]));
            Check.Throws<ArgumentException>(() => new PdfWriter().ImagePng(new byte[] { 1, 2, 3 }));
            // A real 16-bit PNG: reported as unsupported rather than mis-drawn.
            var fixture = PngFixtureData.Cases.First(c => c.Name == "type6_16bit");
            Check.Throws<NotSupportedException>(() => new PdfWriter().ImagePng(Convert.FromBase64String(fixture.Png)));
            // Every 8-bit non-interlaced fixture decodes to the same pixels PngReader gives.
            foreach (var c in PngFixtureData.Cases.Where(c => !c.Name.Contains("adam7") && c.Name.Contains("_8bit")))
            {
                var pdf = new Pdf(new PdfWriter().ImagePng(Convert.FromBase64String(c.Png)).Build());
                int img = pdf.Offsets.Keys.First(id => pdf.Dict(id).Contains("/DeviceRGB"));
                byte[] rgb = Inflate(pdf.StreamData(img));
                byte[] expected = Convert.FromBase64String(c.Rgba);
                for (int i = 0; i < c.Width * c.Height; i++)
                    for (int k = 0; k < 3; k++) Check.Equal(expected[i * 4 + k], rgb[i * 3 + k], $"{c.Name} pixel {i}");
            }
        }

        public void Outline_IsAConsistentTree()
        {
            var w = new PdfWriter { Title = "Ünïcode title" }
                .Heading("One").Paragraph("a").Heading("One.A", level: 2).Heading("One.B", level: 2).Heading("One.B.i", level: 3)
                .NewPage().Heading("Two").Heading("Unlisted", bookmark: false);
            var pdf = new Pdf(w.Build());
            int root = Pdf.Ref(pdf.Dict(1), "Outlines")!.Value;
            Check.True(pdf.Dict(1).Contains("/PageMode /UseOutlines"));
            Check.True(pdf.Dict(root).Contains("/Count 5"));

            var titles = new List<string>();
            void Walk(int parent, int depth)
            {
                int? item = Pdf.Ref(pdf.Dict(parent), "First"), prev = null;
                while (item is int id)
                {
                    string d = pdf.Dict(id);
                    Check.Equal(parent, Pdf.Ref(d, "Parent"));
                    Check.Equal(prev, Pdf.Ref(d, "Prev"));
                    titles.Add(new string('-', depth) + DecodeTextString(Regex.Match(d, @"/Title (<[0-9A-F]+>)").Groups[1].Value));
                    Check.True(Regex.IsMatch(d, @"/Dest \[\d+ 0 R /XYZ 0 [\d.]+ null\]"));
                    if (Pdf.Ref(d, "First") != null) Walk(id, depth + 1);
                    prev = id;
                    item = Pdf.Ref(d, "Next");
                    if (item == null) Check.Equal(id, Pdf.Ref(pdf.Dict(parent), "Last"));
                }
            }
            Walk(root, 0);
            Check.Equal("One|-One.A|-One.B|--One.B.i|Two", string.Join("|", titles));
            // "Two" points at the second page.
            var twoId = pdf.Offsets.Keys.First(id => pdf.Dict(id).Contains("/Title " + "<FEFF" + string.Concat("Two".Select(ch => ((int)ch).ToString("X4")))));
            Check.Equal(pdf.PageIds()[1], int.Parse(Regex.Match(pdf.Dict(twoId), @"/Dest \[(\d+)").Groups[1].Value, CultureInfo.InvariantCulture));
            string info = pdf.Dict(int.Parse(Regex.Match(pdf.Text, @"/Info (\d+) 0 R").Groups[1].Value, CultureInfo.InvariantCulture));
            Check.Equal("Ünïcode title", DecodeTextString(Regex.Match(info, @"/Title (<[0-9A-F]+>)").Groups[1].Value));
        }

        public void PageNumbersEncodingAndCode()
        {
            var w = new PdfWriter(font: PdfFont.Helvetica) { PageNumbers = true }
                .Paragraph("Price: €5 – “quoted” • bullet ✓").NewPage().Code("int x = 1;\n\tindented");
            var pdf = new Pdf(w.Build());
            Check.True(pdf.PageContent(0).Contains("(Page 1 of 2) Tj") && pdf.PageContent(1).Contains("(Page 2 of 2) Tj"));
            byte[] content = pdf.StreamData(Pdf.Ref(pdf.Dict(pdf.PageIds()[0]), "Contents")!.Value);
            string latin = Latin1.GetString(content);
            int at = latin.IndexOf("Price: ", StringComparison.Ordinal) + 7;
            Check.Equal(0x80, (int)content[at]);                                      // € in WinAnsi
            Check.True(latin.Contains("\x96 \x93quoted\x94 \x95 bullet ?"), "dash, quotes, bullet mapped; ✓ has no WinAnsi code");
            string page2 = pdf.PageContent(1);
            Check.True(page2.Contains("/F3 ") && page2.Contains("(    indented) Tj"), "code is Courier with tabs expanded");
            Check.True(pdf.Dict(pdf.PageIds()[1]).Contains("/F3 5 0 R"));
        }

        public void HeadingIsNotOrphanedAtThePageBottom()
        {
            // Whatever is above it, a heading's baseline always leaves room for three body lines before the bottom margin.
            for (int filler = 30; filler <= 46; filler++)
            {
                var w = new PdfWriter(font: PdfFont.Helvetica, fontSize: 11);
                for (int i = 0; i < filler; i++) w.Paragraph("x");
                var pdf = new Pdf(w.Heading("Section").Build());
                string page = pdf.PageContent(pdf.PageIds().Count - 1);
                var m = Regex.Match(page, @"([\d.]+) Td \(Section\) Tj");
                Check.True(m.Success, "heading on the last page");
                double baseline = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                Check.True(baseline - 3 * 11 * 1.35 >= 54 - 1e-9, $"filler {filler}: heading at y={baseline} is orphaned");
            }
        }
    }
}
