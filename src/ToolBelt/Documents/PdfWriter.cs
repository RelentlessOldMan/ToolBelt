// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ToolBelt.Documents
{
    /// <summary>A standard PDF base-14 font (no embedding required).</summary>
    public enum PdfFont
    {
        /// <summary>Monospace. Line wrapping is exact because every glyph is 600/1000 em wide.</summary>
        Courier,
        /// <summary>Proportional sans-serif. Wrapping uses an average glyph width, so it is approximate.</summary>
        Helvetica,
        /// <summary>Bold proportional sans-serif (approximate wrapping).</summary>
        HelveticaBold,
        /// <summary>Proportional serif (approximate wrapping).</summary>
        TimesRoman,
    }

    /// <summary>A standard page size in PDF points (1/72 inch).</summary>
    public enum PdfPageSize
    {
        /// <summary>US Letter, 612 × 792 pt.</summary>
        Letter,
        /// <summary>ISO A4, 595 × 842 pt.</summary>
        A4,
    }

    /// <summary>
    /// A minimal, dependency-free PDF writer: flows wrapped text and headings across auto-paginated pages
    /// using the standard base-14 fonts (no font embedding), and emits a structurally valid file with a
    /// correct cross-reference table. Not a full page-layout engine — it covers text reports, which is the
    /// common need — but every byte it produces is spec-conformant (validated by the tests' own parser).
    /// </summary>
    public sealed class PdfWriter
    {
        private readonly double _pageWidth;
        private readonly double _pageHeight;
        private readonly double _margin;
        private readonly PdfFont _font;
        private readonly double _fontSize;
        private readonly double _leading;
        private readonly double _widthFactor;

        private readonly List<StringBuilder> _pages = new List<StringBuilder>();
        private StringBuilder _current = null!;
        private double _cursorY;

        /// <summary>Creates a writer with a page size, uniform margin, base font and body font size.</summary>
        public PdfWriter(PdfPageSize pageSize = PdfPageSize.Letter, double margin = 54,
            PdfFont font = PdfFont.Courier, double fontSize = 11)
        {
            if (margin < 0) throw new ArgumentOutOfRangeException(nameof(margin), margin, "Margin must be non-negative.");
            if (!(fontSize > 0)) throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be positive.");
            (_pageWidth, _pageHeight) = pageSize == PdfPageSize.A4 ? (595.0, 842.0) : (612.0, 792.0);
            if (margin * 2 >= _pageWidth || margin * 2 >= _pageHeight)
                throw new ArgumentOutOfRangeException(nameof(margin), margin, "Margins leave no room for content.");
            _margin = margin;
            _font = font;
            _fontSize = fontSize;
            _leading = fontSize * 1.35;
            // Average glyph advance as a fraction of the em. Courier is exact; the rest are approximations.
            _widthFactor = font == PdfFont.Courier ? 0.6 : 0.5;
            NewPage();
        }

        /// <summary>Starts a fresh page; subsequent text begins at the top margin.</summary>
        public PdfWriter NewPage()
        {
            _current = new StringBuilder();
            _pages.Add(_current);
            _cursorY = _pageHeight - _margin - _fontSize;
            return this;
        }

        /// <summary>Adds a heading (larger, at ~1.5× the body size by default), then a little space.</summary>
        public PdfWriter Heading(string text, double? size = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            double s = size ?? _fontSize * 1.5;
            WriteWrapped(text, s);
            _cursorY -= s * 0.5;
            return this;
        }

        /// <summary>Adds a paragraph of body text (wrapped, auto-paginated), then a blank line.</summary>
        public PdfWriter Paragraph(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            WriteWrapped(text, _fontSize);
            _cursorY -= _leading * 0.5;
            return this;
        }

        /// <summary>Adds each string as its own wrapped line (no inter-line gap) — handy for tabular/log output.</summary>
        public PdfWriter Lines(IEnumerable<string> lines)
        {
            if (lines is null) throw new ArgumentNullException(nameof(lines));
            foreach (var line in lines) WriteWrapped(line ?? "", _fontSize);
            return this;
        }

        /// <summary>Adds vertical space of <paramref name="points"/> PDF points.</summary>
        public PdfWriter Space(double points)
        {
            _cursorY -= points;
            if (_cursorY < _margin) NewPage();
            return this;
        }

        /// <summary>Serializes the document to PDF bytes.</summary>
        public byte[] Build()
        {
            // Object numbering: 1 Catalog, 2 Pages, 3 Font, then per page {Page, Content}.
            int pageCount = _pages.Count;
            int objectCount = 3 + pageCount * 2;
            var offsets = new long[objectCount + 1]; // 1-based

            using var ms = new MemoryStream();
            void Write(string s)
            {
                var bytes = Latin1.GetBytes(s);
                ms.Write(bytes, 0, bytes.Length);
            }
            void BeginObject(int id) { offsets[id] = ms.Length; Write($"{id} 0 obj\n"); }

            Write("%PDF-1.4\n");
            // A binary comment marks the file as containing binary data (convention).
            ms.WriteByte((byte)'%'); ms.WriteByte(0xE2); ms.WriteByte(0xE3); ms.WriteByte(0xCF); ms.WriteByte(0xD3);
            ms.WriteByte((byte)'\n');

            BeginObject(1);
            Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            BeginObject(2);
            var kids = new StringBuilder();
            for (int i = 0; i < pageCount; i++)
            {
                if (i > 0) kids.Append(' ');
                kids.Append(4 + i * 2).Append(" 0 R");
            }
            Write($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\nendobj\n");

            BeginObject(3);
            Write($"<< /Type /Font /Subtype /Type1 /BaseFont /{BaseFontName(_font)} /Encoding /WinAnsiEncoding >>\nendobj\n");

            for (int i = 0; i < pageCount; i++)
            {
                int pageId = 4 + i * 2;
                int contentId = 5 + i * 2;

                BeginObject(pageId);
                Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(_pageWidth)} {Num(_pageHeight)}] "
                    + $"/Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>\nendobj\n");

                string content = _pages[i].ToString();
                var contentBytes = Latin1.GetBytes(content);
                BeginObject(contentId);
                Write($"<< /Length {contentBytes.Length} >>\nstream\n");
                ms.Write(contentBytes, 0, contentBytes.Length);
                Write("endstream\nendobj\n");
            }

            long xrefOffset = ms.Length;
            Write($"xref\n0 {objectCount + 1}\n");
            Write("0000000000 65535 f \n");
            for (int id = 1; id <= objectCount; id++)
                Write(offsets[id].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

            Write($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

            return ms.ToArray();
        }

        /// <summary>Writes the PDF to a file.</summary>
        public void Save(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            File.WriteAllBytes(path, Build());
        }

        private void WriteWrapped(string text, double fontSize)
        {
            double usable = _pageWidth - 2 * _margin;
            int maxChars = Math.Max(1, (int)(usable / (fontSize * _widthFactor)));
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                foreach (var line in WrapLine(raw, maxChars))
                    EmitLine(line, fontSize);
        }

        private void EmitLine(string line, double fontSize)
        {
            if (_cursorY < _margin)
            {
                NewPage();
                _cursorY = _pageHeight - _margin - fontSize;
            }
            double x = _margin;
            double y = _cursorY;
            _current.Append("BT /F1 ").Append(Num(fontSize)).Append(" Tf ")
                    .Append(Num(x)).Append(' ').Append(Num(y)).Append(" Td (")
                    .Append(EscapePdfText(line)).Append(") Tj ET\n");
            _cursorY -= fontSize * 1.35;
        }

        private static IEnumerable<string> WrapLine(string text, int maxChars)
        {
            if (text.Length == 0) { yield return ""; yield break; }
            var words = text.Split(' ');
            var line = new StringBuilder();
            foreach (var word in words)
            {
                string w = word;
                // A word longer than the line width is hard-split.
                while (w.Length > maxChars)
                {
                    if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                    yield return w.Substring(0, maxChars);
                    w = w.Substring(maxChars);
                }
                if (line.Length == 0) line.Append(w);
                else if (line.Length + 1 + w.Length <= maxChars) line.Append(' ').Append(w);
                else { yield return line.ToString(); line.Clear(); line.Append(w); }
            }
            if (line.Length > 0) yield return line.ToString();
        }

        private static string BaseFontName(PdfFont font) => font switch
        {
            PdfFont.Courier => "Courier",
            PdfFont.Helvetica => "Helvetica",
            PdfFont.HelveticaBold => "Helvetica-Bold",
            PdfFont.TimesRoman => "Times-Roman",
            _ => "Courier",
        };

        // PDF literal strings escape backslash and parentheses; map non-Latin-1 to '?'.
        private static string EscapePdfText(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (ch == '\\' || ch == '(' || ch == ')') { sb.Append('\\').Append(ch); }
                else if (ch < 32 || ch > 255) sb.Append('?');
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

#if NETSTANDARD2_0
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
#else
        private static readonly Encoding Latin1 = Encoding.Latin1;
#endif
    }
}
