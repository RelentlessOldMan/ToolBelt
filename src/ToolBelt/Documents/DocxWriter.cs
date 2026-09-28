// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ToolBelt.Documents
{
    /// <summary>
    /// A minimal, dependency-free writer for Word <c>.docx</c> (Office Open XML) documents. A .docx is
    /// just a ZIP of XML parts, so this builds the three parts a conformant reader needs
    /// (<c>[Content_Types].xml</c>, <c>_rels/.rels</c>, <c>word/document.xml</c>) with
    /// System.IO.Compression — no Open XML SDK. Covers headings, paragraphs, bold/italic runs, bullet
    /// lists and bordered tables: the report essentials. Headings are rendered as bold, sized runs (so no
    /// separate styles part is required); bullet lists are prefixed paragraphs rather than true numbering.
    /// </summary>
    public sealed class DocxWriter
    {
        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private readonly StringBuilder _body = new StringBuilder();

        /// <summary>Adds a heading. Level 1–6 maps to a decreasing font size; the text is bold.</summary>
        public DocxWriter Heading(string text, int level = 1)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (level < 1 || level > 6) throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1–6.");
            int halfPoints = (32 - (level - 1) * 4); // 16pt down to ~6pt, in half-points below
            halfPoints = Math.Max(halfPoints, 20);
            _body.Append("<w:p><w:pPr><w:rPr><w:b/><w:sz w:val=\"").Append(halfPoints)
                 .Append("\"/></w:rPr></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"").Append(halfPoints)
                 .Append("\"/></w:rPr>").Append(Run(text)).Append("</w:r></w:p>");
            return this;
        }

        /// <summary>Adds a paragraph, optionally bold and/or italic.</summary>
        public DocxWriter Paragraph(string text, bool bold = false, bool italic = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            _body.Append("<w:p><w:r>").Append(RunProps(bold, italic)).Append(Run(text)).Append("</w:r></w:p>");
            return this;
        }

        /// <summary>Adds a bulleted list. Items are rendered as paragraphs prefixed with a bullet glyph.</summary>
        public DocxWriter BulletList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            foreach (var item in items)
                _body.Append("<w:p><w:r>").Append(Run("• " + (item ?? ""))).Append("</w:r></w:p>");
            return this;
        }

        /// <summary>Adds a bordered table with a bold header row.</summary>
        public DocxWriter Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        {
            if (headers is null) throw new ArgumentNullException(nameof(headers));
            if (rows is null) throw new ArgumentNullException(nameof(rows));

            _body.Append("<w:tbl><w:tblPr><w:tblBorders>");
            foreach (var edge in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
                _body.Append("<w:").Append(edge).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>");
            _body.Append("</w:tblBorders></w:tblPr>");

            _body.Append("<w:tr>");
            foreach (var h in headers) AppendCell(h ?? "", bold: true);
            _body.Append("</w:tr>");

            foreach (var row in rows)
            {
                _body.Append("<w:tr>");
                for (int c = 0; c < headers.Count; c++)
                    AppendCell(row != null && c < row.Count ? row[c] ?? "" : "", bold: false);
                _body.Append("</w:tr>");
            }
            _body.Append("</w:tbl>");
            // A paragraph after a table is required by the schema (a body can't end on a table alone).
            _body.Append("<w:p/>");
            return this;
        }

        /// <summary>Serializes the document to <c>.docx</c> bytes.</summary>
        public byte[] Build()
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                    + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                    + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                    + "</Types>");

                WriteEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
                    + "</Relationships>");

                WriteEntry(zip, "word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<w:document xmlns:w=\"" + W + "\"><w:body>"
                    + _body
                    + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>"
                    + "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>"
                    + "</w:body></w:document>");
            }
            return ms.ToArray();
        }

        /// <summary>Writes the document to a file.</summary>
        public void Save(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            File.WriteAllBytes(path, Build());
        }

        private void AppendCell(string text, bool bold)
        {
            _body.Append("<w:tc><w:p><w:r>").Append(RunProps(bold, false)).Append(Run(text)).Append("</w:r></w:p></w:tc>");
        }

        private static string RunProps(bool bold, bool italic)
        {
            if (!bold && !italic) return "";
            var sb = new StringBuilder("<w:rPr>");
            if (bold) sb.Append("<w:b/>");
            if (italic) sb.Append("<w:i/>");
            sb.Append("</w:rPr>");
            return sb.ToString();
        }

        // A <w:t> element; xml:space="preserve" keeps leading/trailing spaces.
        private static string Run(string text) => "<w:t xml:space=\"preserve\">" + Escape(text) + "</w:t>";

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
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
