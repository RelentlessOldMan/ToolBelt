using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ToolBelt.Documents;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Documents
{
    /// <summary>DocxWriter lists, runs, images, page breaks, header/footer and properties — structurally, and in real Word when installed.</summary>
    public sealed class DocxFeaturesTests
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        private static Dictionary<string, byte[]> Parts(byte[] docx)
        {
            var parts = new Dictionary<string, byte[]>();
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            foreach (var e in zip.Entries)
            {
                using var ms = new MemoryStream();
                e.Open().CopyTo(ms);
                parts[e.FullName] = ms.ToArray();
            }
            return parts;
        }

        private static XElement Xml(Dictionary<string, byte[]> parts, string name) => XDocument.Parse(Encoding.UTF8.GetString(parts[name])).Root!;

        private static byte[] SamplePng(int w = 120, int h = 60)
        {
            var img = new ImageBuffer(w, h, new Rgba(240, 240, 255));
            img.DrawLine(0, 0, w - 1, h - 1, new Rgba(200, 0, 0));
            return PngWriter.Encode(img);
        }

        private static DocxWriter Sample()
        {
            var d = new DocxWriter { Title = "Feature Test", Author = "ToolBelt" }
                .Header("Header text")
                .Footer("Confidential")
                .Heading("Lists")
                .RichParagraph("Status: ", DocxRun.B("PASS"), " and ", DocxRun.I("italic"), " and ", DocxRun.Mono("code()"), new DocxRun(" u", underline: true))
                .List(new[] { (0, "top"), (1, "nested"), (2, "deeper"), (0, "top again") }, numbered: false)
                .NumberedList(new[] { "first", "second" })
                .Paragraph("between")
                .NumberedList(new[] { "restart" })
                .List(new[] { (0, "one"), (1, "one-a"), (1, "one-b") }, numbered: true)
                .PageBreak()
                .Heading("Figures", 2)
                .Image(SamplePng(), altText: "A diagonal line")
                .Image(SamplePng(1200, 300))                                         // wider than the page: capped
                .Table(new[] { "A", "B" }, new List<IReadOnlyList<string>> { new[] { "1", "2" } })
                .Paragraph("line one\nline two\ttabbed");
            return d;
        }

        public void Structure_PartsRelationshipsAndXml()
        {
            var parts = Parts(Sample().Build());
            foreach (string name in new[] { "word/document.xml", "word/numbering.xml", "word/header1.xml", "word/footer1.xml",
                                            "word/_rels/document.xml.rels", "docProps/core.xml", "word/media/image1.png", "word/media/image2.png" })
                Check.True(parts.ContainsKey(name), "missing part " + name);
            foreach (var kv in parts.Where(p => p.Key.EndsWith(".xml") || p.Key.EndsWith(".rels")))
                XDocument.Parse(Encoding.UTF8.GetString(kv.Value));                    // all well-formed
            string types = Encoding.UTF8.GetString(parts["[Content_Types].xml"]);
            Check.True(types.Contains("Extension=\"png\"") && types.Contains("/word/numbering.xml") && types.Contains("/word/footer1.xml"));
            var rels = Xml(parts, "word/_rels/document.xml.rels");
            var targets = rels.Elements().Select(e => e.Attribute("Target")!.Value).ToList();
            foreach (string t in new[] { "numbering.xml", "header1.xml", "footer1.xml", "media/image1.png", "media/image2.png" })
                Check.True(targets.Contains(t), "missing relationship to " + t);
            Check.True(SamplePng().SequenceEqual(parts["word/media/image1.png"]), "image bytes stored verbatim");
        }

        public void Structure_ListsRunsAndText()
        {
            var parts = Parts(Sample().Build());
            var doc = Xml(parts, "word/document.xml");
            var numIds = doc.Descendants(W + "numId").Select(n => n.Attribute(W + "val")!.Value).Distinct().ToList();
            Check.Equal("1,2,3,4", string.Join(",", numIds));                        // bullets share 1; each numbered list its own
            var numbering = Xml(parts, "word/numbering.xml");
            Check.Equal(3, numbering.Elements(W + "num").Count(n => n.Elements(W + "lvlOverride").Any()));
            var runs = doc.Descendants(W + "r").ToList();
            Check.True(runs.Any(r => r.Element(W + "rPr")?.Element(W + "b") != null && r.Value == "PASS"));
            Check.True(runs.Any(r => r.Element(W + "rPr")?.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value == "Consolas"));
            Check.True(runs.Any(r => r.Element(W + "rPr")?.Element(W + "u") != null));
            Check.Equal(1, doc.Descendants(W + "br").Count(b => b.Attribute(W + "type")?.Value == "page"));
            Check.True(doc.Descendants(W + "tab").Any(), "tab becomes <w:tab/>");
            var core = Encoding.UTF8.GetString(parts["docProps/core.xml"]);
            Check.True(core.Contains("<dc:title>Feature Test</dc:title>"));
        }

        public void Images_SizeFromIhdrAndCapped()
        {
            var doc = Xml(Parts(Sample().Build()), "word/document.xml");
            XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
            var extents = doc.Descendants(wp + "extent").Select(e => (long.Parse(e.Attribute("cx")!.Value), long.Parse(e.Attribute("cy")!.Value))).ToList();
            Check.Equal(2, extents.Count);
            Check.Equal((120L * 914400 / 96, 60L * 914400 / 96), extents[0]);       // 96 dpi
            Check.Equal((long)(6.5 * 914400), extents[1].Item1);                       // capped to the text width
            Check.Equal(extents[1].Item1 / 4, extents[1].Item2);                       // aspect kept
            Check.Throws<ArgumentException>(() => new DocxWriter().Image(new byte[] { 1, 2, 3 }));
        }

        public void ControlCharactersCannotCorruptTheDocument()
        {
            var doc = Xml(Parts(new DocxWriter().Paragraph("bell\a and nul\0").Build()), "word/document.xml");
            Check.True(doc.Value.Contains("bell�"), doc.Value);
        }

        public void ReportBuilder_EmbedsPngFiguresAndKeepsCodeMonospace()
        {
            var parts = Parts(new ReportBuilder("R").Figure("Chart", png: SamplePng()).Figure("Vector only", svg: "<svg xmlns='http://www.w3.org/2000/svg'/>")
                .Code("x = 1\ny = 2").ToDocx());
            Check.True(parts.ContainsKey("word/media/image1.png"));
            string xml = Encoding.UTF8.GetString(parts["word/document.xml"]);
            Check.True(xml.Contains("Figure 1: Chart") && xml.Contains("[Figure 2: Vector only - see the HTML version for graphics]"));
            Check.True(xml.Contains("Consolas"));
            Check.True(parts.ContainsKey("word/footer1.xml"));
        }

        // ---------- real Word ----------

        public void OpensInWordWithExpectedLayout()
        {
            Type? wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType is null) return;                                             // Word not installed: structural tests above stand
            using var tmp = new TempDirectory();
            string path = tmp.Combine("feature.docx");
            Sample().Save(path);

            dynamic word = Activator.CreateInstance(wordType)!;
            try
            {
                word.Visible = false;
                word.DisplayAlerts = 0;
                dynamic doc = word.Documents.Open(path, false, true);                  // ConfirmConversions=false, ReadOnly=true
                try
                {
                    Check.Equal(2, (int)doc.ComputeStatistics(2));                     // wdStatisticPages: the page break works
                    Check.Equal(2, (int)doc.InlineShapes.Count);
                    Check.Equal("Feature Test", (string)doc.BuiltInDocumentProperties["Title"].Value);
                    string header = ((string)doc.Sections[1].Headers[1].Range.Text).Trim();
                    Check.Equal("Header text", header);
                    dynamic footer = doc.Sections[1].Footers[1].Range;
                    footer.Fields.Update();
                    string footerText = ((string)footer.Text).Trim();
                    Check.True(footerText.StartsWith("Confidential", StringComparison.Ordinal) && footerText.EndsWith("Page 1 of 2", StringComparison.Ordinal), footerText);

                    var listStrings = new Dictionary<string, string>();
                    foreach (dynamic p in doc.Paragraphs)
                    {
                        string text = ((string)p.Range.Text).TrimEnd('\r');
                        string ls = (string)p.Range.ListFormat.ListString;
                        if (!string.IsNullOrEmpty(ls)) listStrings[text] = ls;
                    }
                    Check.Equal("1.", listStrings["first"]);
                    Check.Equal("2.", listStrings["second"]);
                    Check.Equal("1.", listStrings["restart"]);                         // a new list restarts
                    Check.Equal("1.", listStrings["one"]);
                    Check.Equal("a.", listStrings["one-a"]);
                    Check.Equal("b.", listStrings["one-b"]);
                    Check.Equal("•", listStrings["top"]);
                    Check.Equal("◦", listStrings["nested"]);
                }
                finally
                {
                    doc.Close(0);
                }
            }
            finally
            {
                word.Quit(0);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(word);
            }
        }
    }
}
