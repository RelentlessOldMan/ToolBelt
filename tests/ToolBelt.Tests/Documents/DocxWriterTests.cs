using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    public sealed class DocxWriterTests
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private static Dictionary<string, string> Unzip(byte[] docx)
        {
            var parts = new Dictionary<string, string>();
            using var ms = new MemoryStream(docx);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                parts[entry.FullName] = reader.ReadToEnd();
            }
            return parts;
        }

        private static XElement Document(byte[] docx) => XDocument.Parse(Unzip(docx)["word/document.xml"]).Root!;

        public void HasRequiredParts()
        {
            var parts = Unzip(new DocxWriter().Paragraph("hi").Build());
            Check.True(parts.ContainsKey("[Content_Types].xml"), "content types part");
            Check.True(parts.ContainsKey("_rels/.rels"), "package rels part");
            Check.True(parts.ContainsKey("word/document.xml"), "main document part");
        }

        public void AllPartsAreValidXml()
        {
            var parts = Unzip(new DocxWriter().Heading("H").Paragraph("p").Build());
            foreach (var kv in parts) XDocument.Parse(kv.Value); // throws if malformed
        }

        public void ParagraphTextPresent()
        {
            var doc = Document(new DocxWriter().Paragraph("Hello Word").Build());
            var texts = doc.Descendants(W + "t").Select(t => t.Value).ToList();
            Check.True(texts.Contains("Hello Word"), string.Join("|", texts));
        }

        public void BoldItalic_EmitRunProps()
        {
            var doc = Document(new DocxWriter().Paragraph("x", bold: true, italic: true).Build());
            var run = doc.Descendants(W + "r").First();
            var rpr = run.Element(W + "rPr")!;
            Check.NotNull(rpr.Element(W + "b"));
            Check.NotNull(rpr.Element(W + "i"));
        }

        public void Heading_IsBoldAndSized()
        {
            var doc = Document(new DocxWriter().Heading("Title", 1).Build());
            var rpr = doc.Descendants(W + "r").First().Element(W + "rPr")!;
            Check.NotNull(rpr.Element(W + "b"));
            Check.NotNull(rpr.Element(W + "sz"));
        }

        public void BulletList_UsesWordNumbering()
        {
            var bytes = new DocxWriter().BulletList(new[] { "one", "two" }).Build();
            var doc = Document(bytes);
            var items = doc.Descendants(W + "p").Where(p => p.Descendants(W + "numPr").Any()).ToList();
            Check.Equal(2, items.Count);
            Check.Equal("one", items[0].Descendants(W + "t").Single().Value);           // no glyph baked into the text
            Check.Equal("1", items[0].Descendants(W + "numId").Single().Attribute(W + "val")!.Value);
            var numbering = XDocument.Parse(Unzip(bytes)["word/numbering.xml"]).Root!;
            Check.Equal("bullet", numbering.Descendants(W + "numFmt").First().Attribute(W + "val")!.Value);
        }

        public void Table_HasRowsAndCells()
        {
            var doc = Document(new DocxWriter().Table(
                new[] { "H1", "H2" },
                new List<IReadOnlyList<string>> { new[] { "a", "b" }, new[] { "c", "d" } }).Build());

            var tbl = doc.Descendants(W + "tbl").First();
            var rows = tbl.Elements(W + "tr").ToList();
            Check.Equal(3, rows.Count); // header + 2 data rows
            Check.Equal(2, rows[0].Elements(W + "tc").Count());
            var cellTexts = tbl.Descendants(W + "t").Select(t => t.Value).ToList();
            foreach (var expected in new[] { "H1", "H2", "a", "b", "c", "d" })
                Check.True(cellTexts.Contains(expected), $"missing cell {expected}");
        }

        public void Table_ShortRowPadded()
        {
            var doc = Document(new DocxWriter().Table(
                new[] { "A", "B", "C" },
                new List<IReadOnlyList<string>> { new[] { "only" } }).Build());
            var dataRow = doc.Descendants(W + "tr").Last();
            Check.Equal(3, dataRow.Elements(W + "tc").Count()); // padded to header width
        }

        public void EscapesXmlSpecials()
        {
            var doc = Document(new DocxWriter().Paragraph("a<b> & \"c\"").Build());
            var text = doc.Descendants(W + "t").First().Value;
            Check.Equal("a<b> & \"c\"", text); // XML parser round-trips the escaped entities
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new DocxWriter().Paragraph(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => new DocxWriter().Heading("x", 0));
            Check.Throws<ArgumentNullException>(() => new DocxWriter().BulletList(null!));
        }
    }
}
