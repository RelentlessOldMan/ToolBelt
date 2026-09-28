using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    public sealed class PdfWriterTests
    {
#if NETSTANDARD2_0
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
#else
        private static readonly Encoding Latin1 = Encoding.Latin1;
#endif

        // In Latin-1 every byte maps to exactly one char, so string indices == byte offsets.
        private static string AsText(byte[] pdf) => Latin1.GetString(pdf);

        public void HeaderAndTrailer()
        {
            var pdf = new PdfWriter().Paragraph("Hello, world.").Build();
            var text = AsText(pdf);
            Check.True(text.StartsWith("%PDF-1.4"), "starts with PDF header");
            Check.True(text.Contains("%%EOF"), "ends with EOF marker");
            Check.True(text.Contains("/Type /Catalog"), "has a catalog");
        }

        public void TextLiteralAppearsAndIsEscaped()
        {
            var pdf = new PdfWriter().Paragraph("balance (net) = a\\b").Build();
            var text = AsText(pdf);
            Check.True(text.Contains(@"balance \(net\) = a\\b"), "parens and backslash escaped in the content stream");
        }

        public void StartxrefPointsAtXref()
        {
            var pdf = new PdfWriter().Paragraph("x").Build();
            var text = AsText(pdf);
            int idx = text.LastIndexOf("startxref", StringComparison.Ordinal);
            Check.True(idx >= 0, "has startxref");
            int nl = text.IndexOf('\n', idx);
            int offset = int.Parse(text.Substring(nl + 1, text.IndexOf('\n', nl + 1) - nl - 1).Trim(), CultureInfo.InvariantCulture);
            Check.True(text.Substring(offset).StartsWith("xref"), "startxref offset lands on the xref keyword");
        }

        // Parse the xref table and confirm every recorded offset lands on "<id> 0 obj".
        public void XrefOffsetsAreCorrect()
        {
            var pdf = new PdfWriter().Heading("Title").Paragraph("Body one.").Paragraph("Body two.").Build();
            var text = AsText(pdf);

            // Anchor on the newline-prefixed keyword so we don't match the "xref" inside "startxref".
            int xrefIdx = text.LastIndexOf("\nxref\n", StringComparison.Ordinal);
            int p = xrefIdx + "\nxref\n".Length;
            int firstNl = text.IndexOf('\n', p);
            var header = text.Substring(p, firstNl - p).Split(' ');
            int count = int.Parse(header[1], CultureInfo.InvariantCulture);
            int entriesStart = firstNl + 1;

            // Entry 0 is the free head; entries 1.. are our objects, each exactly 20 bytes.
            for (int id = 1; id < count; id++)
            {
                string entry = text.Substring(entriesStart + id * 20, 20);
                Check.Equal('n', entry[17]); // in-use marker
                long offset = long.Parse(entry.Substring(0, 10), CultureInfo.InvariantCulture);
                Check.True(text.Substring((int)offset).StartsWith($"{id} 0 obj"), $"obj {id} offset lands on its definition");
            }
        }

        public void PageCountMatchesPagesEmitted()
        {
            var many = new List<string>();
            for (int i = 0; i < 200; i++) many.Add($"line {i:D3} lorem ipsum dolor sit amet");
            var pdf = new PdfWriter().Lines(many).Build();
            var text = AsText(pdf);

            int c = text.IndexOf("/Count ", StringComparison.Ordinal);
            int end = text.IndexOf(' ', c + 7);
            if (end < 0) end = text.IndexOf('>', c + 7);
            int pageCount = int.Parse(text.Substring(c + 7, end - (c + 7)).Trim(), CultureInfo.InvariantCulture);
            Check.True(pageCount >= 4, $"200 lines should paginate; got {pageCount} pages");

            // /Count must equal the number of actual /Type /Page objects.
            int pageObjs = CountOccurrences(text, "/Type /Page ");
            Check.Equal(pageCount, pageObjs);
        }

        public void ExplicitNewPage()
        {
            var pdf = new PdfWriter().Paragraph("p1").NewPage().Paragraph("p2").Build();
            var text = AsText(pdf);
            Check.True(text.Contains("/Count 2"), "explicit NewPage yields two pages");
        }

        public void FontSelection_SetsBaseFont()
        {
            var pdf = new PdfWriter(font: PdfFont.Helvetica).Paragraph("x").Build();
            Check.True(AsText(pdf).Contains("/BaseFont /Helvetica"), "honors the chosen base font");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(margin: -1));
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(fontSize: 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(margin: 400)); // no room on Letter
            Check.Throws<ArgumentNullException>(() => new PdfWriter().Paragraph(null!));
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
            return count;
        }
    }
}
