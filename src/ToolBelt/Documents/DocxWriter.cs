// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ToolBelt.Documents
{
    /// <summary>A run of text with its own formatting, for <see cref="DocxWriter.RichParagraph"/>.</summary>
    public readonly struct DocxRun
    {
        public DocxRun(string text, bool bold = false, bool italic = false, bool underline = false, bool code = false)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Bold = bold;
            Italic = italic;
            Underline = underline;
            Code = code;
        }

        public string Text { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public bool Underline { get; }

        /// <summary>Monospace (Consolas).</summary>
        public bool Code { get; }

        public static implicit operator DocxRun(string text) => new DocxRun(text);
        public static DocxRun B(string text) => new DocxRun(text, bold: true);
        public static DocxRun I(string text) => new DocxRun(text, italic: true);
        public static DocxRun Mono(string text) => new DocxRun(text, code: true);
    }

    /// <summary>
    /// A dependency-free writer for Word <c>.docx</c> (Office Open XML) documents — a ZIP of XML parts built with
    /// System.IO.Compression, no Open XML SDK. Covers the report essentials: headings, paragraphs with mixed bold /
    /// italic / underline / monospace runs, real (Word-numbered) bullet and numbered lists with nesting, bordered tables,
    /// embedded PNG images, page breaks, a running header and a footer with "Page X of Y", and document title/author.
    /// Headings are bold sized runs rather than styles, so no styles part is needed. Output is verified by opening it in
    /// Word in the tests where Word is installed.
    /// </summary>
    public sealed class DocxWriter
    {
        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const long EmuPerInch = 914400;
        private const double ContentWidthInches = 6.5;                   // Letter with 1" margins

        private readonly StringBuilder _body = new StringBuilder();
        private readonly List<byte[]> _images = new List<byte[]>();
        private readonly List<int> _numberedLists = new List<int>();     // numIds of numbered lists (each restarts at 1)
        private bool _usesBullets;
        private string? _header;
        private string? _footer;
        private bool _footerPageNumbers;
        private int _drawingId;

        /// <summary>Document title (File ▸ Info and the window title).</summary>
        public string? Title { get; set; }

        public string? Author { get; set; }

        /// <summary>Adds a heading. Level 1–6 maps to a decreasing font size; the text is bold.</summary>
        public DocxWriter Heading(string text, int level = 1)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (level < 1 || level > 6) throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1–6.");
            int halfPoints = Math.Max(32 - (level - 1) * 4, 20);
            // keepNext keeps a heading on the same page as what follows it.
            _body.Append("<w:p><w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"80\"/><w:outlineLvl w:val=\"").Append(level - 1)
                 .Append("\"/><w:rPr><w:b/><w:sz w:val=\"").Append(halfPoints)
                 .Append("\"/></w:rPr></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"").Append(halfPoints)
                 .Append("\"/></w:rPr>").Append(Text(text)).Append("</w:r></w:p>");
            return this;
        }

        /// <summary>Adds a paragraph, optionally bold and/or italic.</summary>
        public DocxWriter Paragraph(string text, bool bold = false, bool italic = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return RichParagraph(new DocxRun(text, bold, italic));
        }

        /// <summary>A paragraph of differently formatted runs: <c>RichParagraph("Status: ", DocxRun.B("PASS"))</c>.</summary>
        public DocxWriter RichParagraph(params DocxRun[] runs)
        {
            if (runs is null) throw new ArgumentNullException(nameof(runs));
            _body.Append("<w:p>");
            AppendRuns(runs);
            _body.Append("</w:p>");
            return this;
        }

        /// <summary>A bulleted list (Word list numbering, so it indents, wraps and continues like a real list).</summary>
        public DocxWriter BulletList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var leveled = new List<(int, string)>();
            foreach (var item in items) leveled.Add((0, item ?? ""));
            return List(leveled, numbered: false);
        }

        /// <summary>A numbered list (1, 2, 3 …), restarting at 1.</summary>
        public DocxWriter NumberedList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var leveled = new List<(int, string)>();
            foreach (var item in items) leveled.Add((0, item ?? ""));
            return List(leveled, numbered: true);
        }

        /// <summary>
        /// A nested list: each item has a level 0–8. Numbered lists use 1. / a. / i. by level and restart at 1;
        /// bullets alternate •, ◦, ▪.
        /// </summary>
        public DocxWriter List(IEnumerable<(int Level, string Text)> items, bool numbered)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            int numId;
            if (numbered)
            {
                numId = 2 + _numberedLists.Count;
                _numberedLists.Add(numId);
            }
            else
            {
                numId = 1;
                _usesBullets = true;
            }
            foreach (var (level, text) in items)
            {
                if (level < 0 || level > 8) throw new ArgumentOutOfRangeException(nameof(items), level, "List levels are 0–8.");
                _body.Append("<w:p><w:pPr><w:numPr><w:ilvl w:val=\"").Append(level).Append("\"/><w:numId w:val=\"").Append(numId)
                     .Append("\"/></w:numPr></w:pPr><w:r>").Append(Text(text ?? "")).Append("</w:r></w:p>");
            }
            return this;
        }

        /// <summary>Adds a bordered table with a bold, repeating header row.</summary>
        public DocxWriter Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        {
            if (headers is null) throw new ArgumentNullException(nameof(headers));
            if (headers.Count == 0) throw new ArgumentException("A table needs at least one column.", nameof(headers));
            if (rows is null) throw new ArgumentNullException(nameof(rows));

            _body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblBorders>");
            foreach (var edge in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
                _body.Append("<w:").Append(edge).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>");
            _body.Append("</w:tblBorders><w:tblCellMar><w:left w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>");

            _body.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
            foreach (var h in headers) AppendCell(h ?? "", bold: true);
            _body.Append("</w:tr>");

            foreach (var row in rows)
            {
                _body.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
                for (int c = 0; c < headers.Count; c++)
                    AppendCell(row != null && c < row.Count ? row[c] ?? "" : "", bold: false);
                _body.Append("</w:tr>");
            }
            _body.Append("</w:tbl>");
            // A paragraph after a table is required by the schema (a body can't end on a table alone).
            _body.Append("<w:p/>");
            return this;
        }

        /// <summary>
        /// Embeds a PNG image as its own paragraph. Width defaults to the image's size at 96 dpi, capped at the text width;
        /// height keeps the aspect ratio. <paramref name="altText"/> is read by screen readers.
        /// </summary>
        public DocxWriter Image(byte[] png, double? widthInches = null, string? altText = null)
        {
            if (png is null) throw new ArgumentNullException(nameof(png));
            var (pw, ph) = PngSize(png);
            double w = widthInches ?? Math.Min(pw / 96.0, ContentWidthInches);
            if (!(w > 0)) throw new ArgumentOutOfRangeException(nameof(widthInches), widthInches, "Width must be positive.");
            double h = w * ph / pw;
            long cx = (long)Math.Round(w * EmuPerInch), cy = (long)Math.Round(h * EmuPerInch);
            _images.Add(png);
            int index = _images.Count;
            int id = ++_drawingId;
            string rel = "rIdImg" + index.ToString(CultureInfo.InvariantCulture);
            string name = "Picture " + id.ToString(CultureInfo.InvariantCulture);
            string alt = Escape(altText ?? "");
            _body.Append("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>")
                 .Append("<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/>")
                 .Append("<wp:docPr id=\"").Append(id).Append("\" name=\"").Append(name).Append("\" descr=\"").Append(alt).Append("\"/>")
                 .Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>")
                 .Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>")
                 .Append("<pic:nvPicPr><pic:cNvPr id=\"").Append(id).Append("\" name=\"").Append(name).Append("\" descr=\"").Append(alt).Append("\"/><pic:cNvPicPr/></pic:nvPicPr>")
                 .Append("<pic:blipFill><a:blip r:embed=\"").Append(rel).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>")
                 .Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(cx).Append("\" cy=\"").Append(cy).Append("\"/></a:xfrm>")
                 .Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline>")
                 .Append("</w:drawing></w:r></w:p>");
            return this;
        }

        /// <summary>Starts a new page.</summary>
        public DocxWriter PageBreak()
        {
            _body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            return this;
        }

        /// <summary>Text shown at the top of every page (null to remove).</summary>
        public DocxWriter Header(string? text)
        {
            _header = text;
            return this;
        }

        /// <summary>A footer on every page: optional text, then "Page X of Y" when <paramref name="pageNumbers"/> (Word fills the numbers in).</summary>
        public DocxWriter Footer(string? text = null, bool pageNumbers = true)
        {
            _footer = text;
            _footerPageNumbers = pageNumbers;
            return this;
        }

        /// <summary>Serializes the document to <c>.docx</c> bytes.</summary>
        public byte[] Build()
        {
            bool hasHeader = _header != null, hasFooter = _footer != null || _footerPageNumbers;
            bool hasNumbering = _usesBullets || _numberedLists.Count > 0;
            bool hasProps = Title != null || Author != null;

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var types = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                    + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                    + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
                if (_images.Count > 0) types.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
                if (hasNumbering) types.Append("<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>");
                if (hasHeader) types.Append("<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>");
                if (hasFooter) types.Append("<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>");
                if (hasProps) types.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
                types.Append("</Types>");
                WriteEntry(zip, "[Content_Types].xml", types.ToString());

                WriteEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
                    + (hasProps ? "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" : "")
                    + "</Relationships>");

                var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                if (hasNumbering) rels.Append("<Relationship Id=\"rIdNum\" Type=\"" + R + "/numbering\" Target=\"numbering.xml\"/>");
                if (hasHeader) rels.Append("<Relationship Id=\"rIdHdr\" Type=\"" + R + "/header\" Target=\"header1.xml\"/>");
                if (hasFooter) rels.Append("<Relationship Id=\"rIdFtr\" Type=\"" + R + "/footer\" Target=\"footer1.xml\"/>");
                for (int i = 1; i <= _images.Count; i++)
                    rels.Append("<Relationship Id=\"rIdImg").Append(i).Append("\" Type=\"" + R + "/image\" Target=\"media/image").Append(i).Append(".png\"/>");
                rels.Append("</Relationships>");
                WriteEntry(zip, "word/_rels/document.xml.rels", rels.ToString());

                var sect = new StringBuilder("<w:sectPr>");
                if (hasHeader) sect.Append("<w:headerReference w:type=\"default\" r:id=\"rIdHdr\"/>");
                if (hasFooter) sect.Append("<w:footerReference w:type=\"default\" r:id=\"rIdFtr\"/>");
                sect.Append("<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>");

                WriteEntry(zip, "word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<w:document xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\""
                    + " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\""
                    + " xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\""
                    + " xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>"
                    + _body + sect + "</w:body></w:document>");

                if (hasNumbering) WriteEntry(zip, "word/numbering.xml", NumberingXml());
                if (hasHeader)
                    WriteEntry(zip, "word/header1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:hdr xmlns:w=\"" + W + "\">"
                        + "<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:color w:val=\"666666\"/><w:sz w:val=\"18\"/></w:rPr>" + Text(_header!) + "</w:r></w:p></w:hdr>");
                if (hasFooter) WriteEntry(zip, "word/footer1.xml", FooterXml());
                for (int i = 0; i < _images.Count; i++)
                {
                    var entry = zip.CreateEntry("word/media/image" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".png", CompressionLevel.NoCompression);
                    using var s = entry.Open();
                    s.Write(_images[i], 0, _images[i].Length);
                }
                if (hasProps)
                    WriteEntry(zip, "docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
                        + (Title != null ? "<dc:title>" + Escape(Title) + "</dc:title>" : "")
                        + (Author != null ? "<dc:creator>" + Escape(Author) + "</dc:creator>" : "")
                        + "</cp:coreProperties>");
            }
            return ms.ToArray();
        }

        /// <summary>Writes the document to a file.</summary>
        public void Save(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            File.WriteAllBytes(path, Build());
        }

        private string FooterXml()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:ftr xmlns:w=\"" + W + "\"><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>");
            const string Small = "<w:rPr><w:color w:val=\"666666\"/><w:sz w:val=\"18\"/></w:rPr>";
            if (_footer != null) sb.Append("<w:r>").Append(Small).Append(Text(_footerPageNumbers ? _footer + "   ·   " : _footer)).Append("</w:r>");
            if (_footerPageNumbers)
                sb.Append("<w:r>").Append(Small).Append(Text("Page ")).Append("</w:r>")
                  .Append("<w:fldSimple w:instr=\" PAGE \"><w:r>").Append(Small).Append("<w:t>1</w:t></w:r></w:fldSimple>")
                  .Append("<w:r>").Append(Small).Append(Text(" of ")).Append("</w:r>")
                  .Append("<w:fldSimple w:instr=\" NUMPAGES \"><w:r>").Append(Small).Append("<w:t>1</w:t></w:r></w:fldSimple>");
            sb.Append("</w:p></w:ftr>");
            return sb.ToString();
        }

        private string NumberingXml()
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"" + W + "\">");
            string[] bullets = { "•", "◦", "▪" };
            string[] formats = { "decimal", "lowerLetter", "lowerRoman" };
            // abstractNum 0: bullets; abstractNum 1: 1. a. i. by level.
            for (int abs = 0; abs < 2; abs++)
            {
                sb.Append("<w:abstractNum w:abstractNumId=\"").Append(abs).Append("\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
                for (int lvl = 0; lvl < 9; lvl++)
                {
                    int indent = 720 * (lvl + 1);
                    sb.Append("<w:lvl w:ilvl=\"").Append(lvl).Append("\"><w:start w:val=\"1\"/>");
                    if (abs == 0)
                        sb.Append("<w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"").Append(bullets[lvl % 3]).Append("\"/>");
                    else
                        sb.Append("<w:numFmt w:val=\"").Append(formats[lvl % 3]).Append("\"/><w:lvlText w:val=\"%").Append(lvl + 1).Append(".\"/>");
                    sb.Append("<w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"").Append(indent).Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");
                }
                sb.Append("</w:abstractNum>");
            }
            sb.Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
            // Each numbered list gets its own num with a restart override, so lists don't continue each other's numbering.
            foreach (int id in _numberedLists)
            {
                sb.Append("<w:num w:numId=\"").Append(id).Append("\"><w:abstractNumId w:val=\"1\"/>");
                for (int lvl = 0; lvl < 9; lvl++)
                    sb.Append("<w:lvlOverride w:ilvl=\"").Append(lvl).Append("\"><w:startOverride w:val=\"1\"/></w:lvlOverride>");
                sb.Append("</w:num>");
            }
            sb.Append("</w:numbering>");
            return sb.ToString();
        }

        private void AppendRuns(IEnumerable<DocxRun> runs)
        {
            foreach (var run in runs)
            {
                _body.Append("<w:r>");
                if (run.Bold || run.Italic || run.Underline || run.Code)
                {
                    _body.Append("<w:rPr>");
                    if (run.Code) _body.Append("<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/>");
                    if (run.Bold) _body.Append("<w:b/>");
                    if (run.Italic) _body.Append("<w:i/>");
                    if (run.Underline) _body.Append("<w:u w:val=\"single\"/>");
                    _body.Append("</w:rPr>");
                }
                _body.Append(Text(run.Text)).Append("</w:r>");
            }
        }

        private void AppendCell(string text, bool bold)
        {
            _body.Append("<w:tc><w:p><w:r>").Append(bold ? "<w:rPr><w:b/></w:rPr>" : "").Append(Text(text)).Append("</w:r></w:p></w:tc>");
        }

        // A <w:t> element (xml:space="preserve" keeps leading/trailing spaces); line breaks become <w:br/>, tabs <w:tab/>.
        private static string Text(string text)
        {
            var sb = new StringBuilder();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append("<w:br/>");
                string[] tabs = lines[i].Split('\t');
                for (int t = 0; t < tabs.Length; t++)
                {
                    if (t > 0) sb.Append("<w:tab/>");
                    sb.Append("<w:t xml:space=\"preserve\">").Append(Escape(tabs[t])).Append("</w:t>");
                }
            }
            return sb.ToString();
        }

        private static (int Width, int Height) PngSize(byte[] png)
        {
            byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (png.Length < 24) throw new ArgumentException("Not a PNG image.", nameof(png));
            for (int i = 0; i < 8; i++) if (png[i] != sig[i]) throw new ArgumentException("Not a PNG image.", nameof(png));
            if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R') throw new ArgumentException("PNG has no IHDR.", nameof(png));
            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            if (w <= 0 || h <= 0) throw new ArgumentException("PNG has an invalid size.", nameof(png));
            return (w, h);
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default:
                        // Characters XML 1.0 forbids would make Word refuse the file: other C0 controls, U+FFFE/U+FFFF and
                        // unpaired surrogates.
                        bool illegal = (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') || ch == '\uFFFE' || ch == '\uFFFF'
                                       || (char.IsHighSurrogate(ch) && (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])))
                                       || (char.IsLowSurrogate(ch) && (i == 0 || !char.IsHighSurrogate(text[i - 1])));
                        sb.Append(illegal ? '\uFFFD' : ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
