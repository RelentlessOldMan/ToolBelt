// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ToolBelt.Documents
{
    /// <summary>A standard PDF base-14 font (no embedding required).</summary>
    public enum PdfFont
    {
        /// <summary>Monospace; every glyph is 600/1000 em wide.</summary>
        Courier,
        /// <summary>Proportional sans-serif.</summary>
        Helvetica,
        /// <summary>Bold proportional sans-serif.</summary>
        HelveticaBold,
        /// <summary>Proportional serif.</summary>
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
    /// A dependency-free PDF writer for reports: wrapped text and headings flowing across auto-paginated pages in the
    /// standard base-14 fonts (no embedding), bordered tables whose header row repeats on each page, raster images (PNG or
    /// raw RGBA, with transparency), a bookmark outline built from headings, optional "Page X of Y" footers and document
    /// title/author. Text is measured with the real base-14 advance widths, so wrapping is exact in every font. Characters
    /// outside Windows-1252 are written as '?'. Every byte is spec-conformant (validated by the tests' own parser).
    /// </summary>
    public sealed class PdfWriter
    {
        private sealed class Page
        {
            public readonly StringBuilder Content = new StringBuilder();
            public readonly SortedSet<int> Images = new SortedSet<int>();
        }

        private sealed class PdfImage
        {
            public int Width, Height;
            public byte[] Rgb = Array.Empty<byte>();
            public byte[]? Alpha;
        }

        private readonly double _pageWidth, _pageHeight, _margin, _fontSize, _leading;
        private readonly PdfFont _font;
        private readonly List<Page> _pages = new List<Page>();
        private readonly List<PdfImage> _images = new List<PdfImage>();
        private readonly List<(string Title, int Level, int Page, double Y)> _outline = new List<(string, int, int, double)>();
        private Page _current = null!;
        private double _y;                                                    // top of the free space on the current page

        /// <summary>Creates a writer with a page size, uniform margin, base font and body font size.</summary>
        public PdfWriter(PdfPageSize pageSize = PdfPageSize.Letter, double margin = 54, PdfFont font = PdfFont.Courier, double fontSize = 11)
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
            NewPage();
        }

        /// <summary>Document title (shown by viewers in the title bar and properties).</summary>
        public string? Title { get; set; }

        public string? Author { get; set; }

        /// <summary>Stamp "Page X of Y" at the bottom of every page.</summary>
        public bool PageNumbers { get; set; }

        /// <summary>Number of pages so far.</summary>
        public int PageCount => _pages.Count;

        private double UsableWidth => _pageWidth - 2 * _margin;

        /// <summary>Starts a fresh page; subsequent content begins at the top margin.</summary>
        public PdfWriter NewPage()
        {
            _current = new Page();
            _pages.Add(_current);
            _y = _pageHeight - _margin;
            return this;
        }

        /// <summary>
        /// Adds a bold heading (≈1.5× body size by default) and, when <paramref name="bookmark"/>, an entry in the PDF
        /// outline at <paramref name="level"/> (1 = top). A heading near the bottom of a page moves to the next page so it
        /// isn't separated from what follows.
        /// </summary>
        public PdfWriter Heading(string text, double? size = null, int level = 1, bool bookmark = true)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be at least 1.");
            double s = size ?? _fontSize * 1.5;
            if (_y - s - 3 * _leading < _margin) NewPage();
            if (bookmark) _outline.Add((text, level, _pages.Count - 1, _y));
            WriteWrapped(text, s, bold: true, indent: 0);
            _y -= s * 0.5;
            return this;
        }

        /// <summary>Adds a paragraph of body text (wrapped, auto-paginated), then half a line of space.</summary>
        public PdfWriter Paragraph(string text, bool bold = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            WriteWrapped(text, _fontSize, bold, indent: 0);
            _y -= _leading * 0.5;
            return this;
        }

        /// <summary>Adds each string as its own wrapped line (no inter-line gap) — handy for tabular/log output.</summary>
        public PdfWriter Lines(IEnumerable<string> lines)
        {
            if (lines is null) throw new ArgumentNullException(nameof(lines));
            foreach (var line in lines) WriteWrapped(line ?? "", _fontSize, false, 0);
            return this;
        }

        /// <summary>A bulleted list; continuation lines hang under the text, not the bullet.</summary>
        public PdfWriter BulletList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var leveled = new List<(int, string)>();
            foreach (var item in items) leveled.Add((0, item ?? ""));
            return BulletList(leveled);
        }

        /// <summary>A nested bulleted list: each level (0 = outermost) indents further and alternates • and –.</summary>
        public PdfWriter BulletList(IEnumerable<(int Level, string Text)> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            double bulletWidth = Measure("•  ", _fontSize, false), step = _fontSize * 1.6;
            foreach (var (level, text) in items)
            {
                if (level < 0) throw new ArgumentOutOfRangeException(nameof(items), level, "List levels must not be negative.");
                double offset = Math.Min(level * step, UsableWidth / 2);
                EnsureRoom(_fontSize);
                EmitText(level % 2 == 0 ? "•" : "–", _margin + offset, _y - _fontSize, _fontSize, false);
                WriteWrapped(text ?? "", _fontSize, false, offset + bulletWidth);
            }
            _y -= _leading * 0.5;
            return this;
        }

        /// <summary>
        /// A monospace (Courier) block, indented with a grey bar in the margin: line breaks and spacing are kept, long lines
        /// wrap at the text width.
        /// </summary>
        public PdfWriter Code(string text, double? size = null)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            double s = size ?? _fontSize * 0.9, indent = 10;
            int perLine = Math.Max(1, (int)((UsableWidth - indent) / (s * 0.6)));
            foreach (var raw in text.Replace("\r\n", "\n").Replace("\t", "    ").Split('\n'))
            {
                for (int start = 0; start == 0 || start < raw.Length; start += perLine)
                {
                    string piece = raw.Length == 0 ? "" : raw.Substring(start, Math.Min(perLine, raw.Length - start));
                    EnsureRoom(s);
                    double baseline = _y - s;
                    _current.Content.Append("0.75 g ").Append(Num(_margin)).Append(' ').Append(Num(baseline - s * 0.3)).Append(" 2 ")
                            .Append(Num(s * 1.3)).Append(" re f 0 g\n");
                    EmitText(piece, _margin + indent, baseline, s, "F3");
                    _y -= s * 1.3;
                    if (raw.Length == 0) break;
                }
            }
            _y -= _leading * 0.5;
            return this;
        }

        /// <summary>Adds vertical space of <paramref name="points"/> PDF points.</summary>
        public PdfWriter Space(double points)
        {
            _y -= points;
            if (_y - _fontSize < _margin) NewPage();
            return this;
        }

        /// <summary>
        /// A bordered table with a shaded bold header row that repeats at the top of each page the table continues on.
        /// Columns are sized to their content (shrunk proportionally if too wide) unless <paramref name="columnWeights"/>
        /// gives relative widths that fill the text width. Cell text wraps; a row never splits across pages (a row taller
        /// than a page is clipped at the bottom margin).
        /// </summary>
        public PdfWriter Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, IReadOnlyList<double>? columnWeights = null, double? fontSize = null)
        {
            if (headers is null) throw new ArgumentNullException(nameof(headers));
            if (rows is null) throw new ArgumentNullException(nameof(rows));
            int n = headers.Count;
            if (n == 0) throw new ArgumentException("A table needs at least one column.", nameof(headers));
            if (columnWeights != null && (columnWeights.Count != n || HasNonPositive(columnWeights)))
                throw new ArgumentException("Column weights must be positive, one per column.", nameof(columnWeights));
            double size = fontSize ?? _fontSize * 0.9, lead = size * 1.3, pad = 3;
            var data = new List<string[]>();
            foreach (var r in rows)
            {
                var cells = new string[n];
                for (int c = 0; c < n; c++) cells[c] = r != null && c < r.Count ? r[c] ?? "" : "";
                data.Add(cells);
            }

            var widths = new double[n];
            if (columnWeights != null)
            {
                double sum = 0;
                foreach (double w in columnWeights) sum += w;
                for (int c = 0; c < n; c++) widths[c] = UsableWidth * columnWeights[c] / sum;
            }
            else
            {
                for (int c = 0; c < n; c++)
                {
                    double w = Measure(headers[c] ?? "", size, true);
                    foreach (var cells in data) w = Math.Max(w, Measure(cells[c], size, false));
                    widths[c] = w + 2 * pad;
                }
                double total = 0;
                foreach (double w in widths) total += w;
                if (total > UsableWidth) for (int c = 0; c < n; c++) widths[c] *= UsableWidth / total;
            }

            var header = new string[n];
            for (int c = 0; c < n; c++) header[c] = headers[c] ?? "";
            DrawRow(header, widths, size, lead, pad, isHeader: true);
            foreach (var cells in data)
            {
                double h = RowHeight(cells, widths, size, lead, pad, false);
                if (_y - h < _margin && _y < _pageHeight - _margin - 1)
                {
                    NewPage();
                    DrawRow(header, widths, size, lead, pad, isHeader: true);
                }
                DrawRow(cells, widths, size, lead, pad, isHeader: false);
            }
            _y -= _leading * 0.6;
            return this;
        }

        /// <summary>Embeds a PNG (8-bit, non-interlaced; any colour type, transparency kept) — see <see cref="Image(int, int, byte[], double?)"/>.</summary>
        public PdfWriter ImagePng(byte[] png, double? widthPoints = null)
        {
            var (w, h, rgba) = DecodePng(png ?? throw new ArgumentNullException(nameof(png)));
            return Image(w, h, rgba, widthPoints);
        }

        /// <summary>
        /// Places an RGBA image (row-major, 4 bytes per pixel) centred on its own line. Width defaults to the pixel width at
        /// 96 dpi, capped to the text width; the image shrinks further if needed to fit a page, and moves to a new page if it
        /// doesn't fit in the space left. Transparency becomes a soft mask.
        /// </summary>
        public PdfWriter Image(int width, int height, byte[] rgba, double? widthPoints = null)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");
            if (rgba is null) throw new ArgumentNullException(nameof(rgba));
            if (rgba.Length != (long)width * height * 4) throw new ArgumentException("RGBA buffer length must be width × height × 4.", nameof(rgba));
            double dw = widthPoints ?? Math.Min(width * 0.75, UsableWidth);
            if (!(dw > 0)) throw new ArgumentOutOfRangeException(nameof(widthPoints), widthPoints, "Width must be positive.");
            dw = Math.Min(dw, UsableWidth);
            double dh = dw * height / width, maxH = _pageHeight - 2 * _margin;
            if (dh > maxH) { dw *= maxH / dh; dh = maxH; }
            if (_y - dh < _margin) NewPage();

            var img = new PdfImage { Width = width, Height = height, Rgb = new byte[width * height * 3] };
            bool anyAlpha = false;
            for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
            {
                img.Rgb[j] = rgba[i]; img.Rgb[j + 1] = rgba[i + 1]; img.Rgb[j + 2] = rgba[i + 2];
                if (rgba[i + 3] != 255) anyAlpha = true;
            }
            if (anyAlpha)
            {
                img.Alpha = new byte[width * height];
                for (int i = 0; i < img.Alpha.Length; i++) img.Alpha[i] = rgba[i * 4 + 3];
            }
            _images.Add(img);
            int index = _images.Count;
            _current.Images.Add(index);
            double x = _margin + (UsableWidth - dw) / 2, y = _y - dh;
            _current.Content.Append("q ").Append(Num(dw)).Append(" 0 0 ").Append(Num(dh)).Append(' ').Append(Num(x)).Append(' ').Append(Num(y))
                    .Append(" cm /Im").Append(index).Append(" Do Q\n");
            _y = y - _leading * 0.5;
            return this;
        }

        /// <summary>A thin horizontal line across the text width.</summary>
        public PdfWriter HorizontalRule()
        {
            EnsureRoom(_leading);
            double y = _y - _leading / 2;
            _current.Content.Append("0.6 G 0.5 w ").Append(Num(_margin)).Append(' ').Append(Num(y)).Append(" m ")
                    .Append(Num(_pageWidth - _margin)).Append(' ').Append(Num(y)).Append(" l S 0 G\n");
            _y -= _leading;
            return this;
        }

        /// <summary>Width of <paramref name="text"/> in points in this writer's font (bold variant if <paramref name="bold"/>).</summary>
        public double Measure(string text, double size, bool bold = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            int[]? table = Widths(bold);
            double units = 0;
            foreach (char ch in text)
            {
                int code = WinAnsi(ch);
                units += table == null ? 600 : (code >= 32 && code <= 126 ? table[code - 32] : 556);
            }
            return units / 1000 * size;
        }

        /// <summary>Serializes the document to PDF bytes.</summary>
        public byte[] Build()
        {
            int pageCount = _pages.Count;
            // Object ids: 1 catalog, 2 pages, 3-5 fonts (body, bold, Courier for code), then images (+ soft masks),
            // pages (+ contents), outline, info.
            int next = 6;
            var imageIds = new int[_images.Count];
            var maskIds = new int[_images.Count];
            for (int i = 0; i < _images.Count; i++)
            {
                imageIds[i] = next++;
                maskIds[i] = _images[i].Alpha != null ? next++ : 0;
            }
            var pageIds = new int[pageCount];
            var contentIds = new int[pageCount];
            for (int i = 0; i < pageCount; i++) { pageIds[i] = next++; contentIds[i] = next++; }
            int outlineRoot = _outline.Count > 0 ? next++ : 0;
            var itemIds = new int[_outline.Count];
            for (int i = 0; i < _outline.Count; i++) itemIds[i] = next++;
            int infoId = next++;
            int objectCount = next - 1;
            var offsets = new long[objectCount + 1];

            using var ms = new MemoryStream();
            void Write(string s) { var b = Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
            void Begin(int id) { offsets[id] = ms.Length; Write(id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n"); }
            void Stream(int id, string dict, byte[] bytes)
            {
                Begin(id);
                Write("<< " + dict + " /Length " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
                ms.Write(bytes, 0, bytes.Length);
                Write("\nendstream\nendobj\n");
            }

            Write("%PDF-1.4\n");
            ms.WriteByte((byte)'%'); ms.WriteByte(0xE2); ms.WriteByte(0xE3); ms.WriteByte(0xCF); ms.WriteByte(0xD3); ms.WriteByte((byte)'\n');

            Begin(1);
            Write("<< /Type /Catalog /Pages 2 0 R" + (outlineRoot > 0 ? $" /Outlines {outlineRoot} 0 R /PageMode /UseOutlines" : "") + " >>\nendobj\n");

            Begin(2);
            var kids = new StringBuilder();
            for (int i = 0; i < pageCount; i++) kids.Append(i > 0 ? " " : "").Append(pageIds[i]).Append(" 0 R");
            Write($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\nendobj\n");

            Begin(3);
            Write($"<< /Type /Font /Subtype /Type1 /BaseFont /{BaseFontName(_font, false)} /Encoding /WinAnsiEncoding >>\nendobj\n");
            Begin(4);
            Write($"<< /Type /Font /Subtype /Type1 /BaseFont /{BaseFontName(_font, true)} /Encoding /WinAnsiEncoding >>\nendobj\n");
            Begin(5);
            Write("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n");

            for (int i = 0; i < _images.Count; i++)
            {
                var img = _images[i];
                string common = $"/Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /BitsPerComponent 8 /Filter /FlateDecode";
                Stream(imageIds[i], common + " /ColorSpace /DeviceRGB" + (maskIds[i] > 0 ? $" /SMask {maskIds[i]} 0 R" : ""), Zlib(img.Rgb));
                if (maskIds[i] > 0) Stream(maskIds[i], common + " /ColorSpace /DeviceGray", Zlib(img.Alpha!));
            }

            for (int i = 0; i < pageCount; i++)
            {
                var page = _pages[i];
                var xobjects = new StringBuilder();
                foreach (int im in page.Images) xobjects.Append("/Im").Append(im).Append(' ').Append(imageIds[im - 1]).Append(" 0 R ");
                Begin(pageIds[i]);
                Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(_pageWidth)} {Num(_pageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >>"
                    + (xobjects.Length > 0 ? " /XObject << " + xobjects + ">>" : "") + $" >> /Contents {contentIds[i]} 0 R >>\nendobj\n");
                string content = page.Content.ToString();
                if (PageNumbers)
                {
                    string label = "Page " + (i + 1).ToString(CultureInfo.InvariantCulture) + " of " + pageCount.ToString(CultureInfo.InvariantCulture);
                    double size = Math.Max(6, _fontSize * 0.75);
                    content += "0.4 g BT /F1 " + Num(size) + " Tf " + Num((_pageWidth - Measure(label, size)) / 2) + " " + Num(Math.Max(12, _margin / 2))
                               + " Td (" + Escape(label) + ") Tj ET 0 g\n";
                }
                Stream(contentIds[i], "", Latin1.GetBytes(content));
            }

            if (outlineRoot > 0) WriteOutline(Write, Begin, outlineRoot, itemIds, pageIds);

            Begin(infoId);
            Write("<< /Producer " + TextString("ToolBelt PdfWriter") + (Title != null ? " /Title " + TextString(Title) : "")
                  + (Author != null ? " /Author " + TextString(Author) : "") + " >>\nendobj\n");

            long xref = ms.Length;
            Write($"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
            for (int id = 1; id <= objectCount; id++) Write(offsets[id].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Write($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R /Info {infoId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return ms.ToArray();
        }

        /// <summary>Writes the PDF to a file.</summary>
        public void Save(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            File.WriteAllBytes(path, Build());
        }

        // ---------- outline ----------

        private void WriteOutline(Action<string> write, Action<int> begin, int rootId, int[] ids, int[] pageIds)
        {
            int n = _outline.Count;
            var parent = new int[n];                                          // index of parent item, −1 = root
            var stack = new List<int>();
            for (int i = 0; i < n; i++)
            {
                while (stack.Count > 0 && _outline[stack[stack.Count - 1]].Level >= _outline[i].Level) stack.RemoveAt(stack.Count - 1);
                parent[i] = stack.Count > 0 ? stack[stack.Count - 1] : -1;
                stack.Add(i);
            }
            List<int> Children(int p)
            {
                var c = new List<int>();
                for (int i = 0; i < n; i++) if (parent[i] == p) c.Add(i);
                return c;
            }
            int Descendants(int p)
            {
                int count = 0;
                foreach (int c in Children(p)) count += 1 + Descendants(c);
                return count;
            }
            var top = Children(-1);
            begin(rootId);
            write($"<< /Type /Outlines /First {ids[top[0]]} 0 R /Last {ids[top[top.Count - 1]]} 0 R /Count {n} >>\nendobj\n");
            for (int i = 0; i < n; i++)
            {
                var siblings = Children(parent[i]);
                int pos = siblings.IndexOf(i);
                var kids = Children(i);
                var (title, _, page, y) = _outline[i];
                var sb = new StringBuilder("<< /Title ").Append(TextString(title))
                    .Append(" /Parent ").Append(parent[i] < 0 ? rootId : ids[parent[i]]).Append(" 0 R");
                if (pos > 0) sb.Append(" /Prev ").Append(ids[siblings[pos - 1]]).Append(" 0 R");
                if (pos < siblings.Count - 1) sb.Append(" /Next ").Append(ids[siblings[pos + 1]]).Append(" 0 R");
                if (kids.Count > 0)
                    sb.Append(" /First ").Append(ids[kids[0]]).Append(" 0 R /Last ").Append(ids[kids[kids.Count - 1]]).Append(" 0 R /Count ").Append(Descendants(i));
                sb.Append(" /Dest [").Append(pageIds[page]).Append(" 0 R /XYZ 0 ").Append(Num(y)).Append(" null] >>\nendobj\n");
                begin(ids[i]);
                write(sb.ToString());
            }
        }

        // ---------- text ----------

        private void WriteWrapped(string text, double size, bool bold, double indent)
        {
            double width = UsableWidth - indent;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
                foreach (var line in Wrap(raw, width, size, bold))
                {
                    EnsureRoom(size);
                    EmitText(line, _margin + indent, _y - size, size, bold);
                    _y -= size * 1.35;
                }
        }

        private void EnsureRoom(double size)
        {
            if (_y - size < _margin) NewPage();
        }

        private void EmitText(string text, double x, double baseline, double size, bool bold) => EmitText(text, x, baseline, size, bold ? "F2" : "F1");

        private void EmitText(string text, double x, double baseline, double size, string font)
        {
            _current.Content.Append("BT /").Append(font).Append(' ').Append(Num(size)).Append(" Tf ")
                    .Append(Num(x)).Append(' ').Append(Num(baseline)).Append(" Td (").Append(Escape(text)).Append(") Tj ET\n");
        }

        private List<string> Wrap(string text, double width, double size, bool bold)
        {
            var lines = new List<string>();
            if (text.Length == 0) { lines.Add(""); return lines; }
            var line = new StringBuilder();
            foreach (var word in text.Split(' '))
            {
                string w = word;
                // A word wider than the line is hard-split.
                while (Measure(w, size, bold) > width && w.Length > 1)
                {
                    if (line.Length > 0) { lines.Add(line.ToString()); line.Clear(); }
                    int fit = 1;
                    while (fit < w.Length && Measure(w.Substring(0, fit + 1), size, bold) <= width) fit++;
                    lines.Add(w.Substring(0, fit));
                    w = w.Substring(fit);
                }
                string candidate = line.Length == 0 ? w : line + " " + w;
                if (line.Length == 0 || Measure(candidate, size, bold) <= width) { line.Clear(); line.Append(candidate); }
                else { lines.Add(line.ToString()); line.Clear(); line.Append(w); }
            }
            if (line.Length > 0) lines.Add(line.ToString());
            return lines;
        }

        private double RowHeight(string[] cells, double[] widths, double size, double lead, double pad, bool bold)
        {
            int maxLines = 1;
            for (int c = 0; c < cells.Length; c++) maxLines = Math.Max(maxLines, CellLines(cells[c], widths[c], size, pad, bold).Count);
            return maxLines * lead + 2 * pad;
        }

        private List<string> CellLines(string text, double width, double size, double pad, bool bold)
        {
            var lines = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n')) lines.AddRange(Wrap(raw, Math.Max(1, width - 2 * pad), size, bold));
            return lines;
        }

        private void DrawRow(string[] cells, double[] widths, double size, double lead, double pad, bool isHeader)
        {
            double h = RowHeight(cells, widths, size, lead, pad, isHeader);
            double top = _y, bottom = Math.Max(_margin, top - h), x = _margin;
            var sb = _current.Content;
            if (isHeader)
            {
                double total = 0;
                foreach (double w in widths) total += w;
                sb.Append("0.9 g ").Append(Num(x)).Append(' ').Append(Num(bottom)).Append(' ').Append(Num(total)).Append(' ').Append(Num(top - bottom)).Append(" re f 0 g\n");
            }
            sb.Append("0.5 w 0.5 G\n");
            for (int c = 0; c < cells.Length; c++)
            {
                sb.Append(Num(x)).Append(' ').Append(Num(bottom)).Append(' ').Append(Num(widths[c])).Append(' ').Append(Num(top - bottom)).Append(" re S\n");
                double baseline = top - pad - size * 0.8;
                foreach (string line in CellLines(cells[c], widths[c], size, pad, isHeader))
                {
                    if (baseline < bottom + 1) break;                         // a row taller than the page is clipped
                    EmitText(line, x + pad, baseline, size, isHeader);
                    baseline -= lead;
                }
                x += widths[c];
            }
            sb.Append("0 G\n");
            _y = bottom;
        }

        private static bool HasNonPositive(IReadOnlyList<double> values)
        {
            foreach (double v in values) if (!(v > 0) || double.IsInfinity(v)) return true;
            return false;
        }

        // ---------- encoding ----------

        // Windows-1252 code for a character (what /WinAnsiEncoding means), or '?' if it has none.
        private static int WinAnsi(char ch)
        {
            if (ch < 128 || (ch >= 160 && ch <= 255)) return ch;
            switch (ch)
            {
                case '€': return 0x80; case '‚': return 0x82; case 'ƒ': return 0x83; case '„': return 0x84; case '…': return 0x85;
                case '†': return 0x86; case '‡': return 0x87; case 'ˆ': return 0x88; case '‰': return 0x89; case 'Š': return 0x8A;
                case '‹': return 0x8B; case 'Œ': return 0x8C; case 'Ž': return 0x8E; case '‘': return 0x91; case '’': return 0x92;
                case '“': return 0x93; case '”': return 0x94; case '•': return 0x95; case '–': return 0x96; case '—': return 0x97;
                case '˜': return 0x98; case '™': return 0x99; case 'š': return 0x9A; case '›': return 0x9B; case 'œ': return 0x9C;
                case 'ž': return 0x9E; case 'Ÿ': return 0x9F;
                default: return '?';
            }
        }

        // A PDF literal string body: escape \ ( ), map to WinAnsi, control characters become '?'.
        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                int code = WinAnsi(ch);
                if (code == '\\' || code == '(' || code == ')') sb.Append('\\').Append((char)code);
                else if (code < 32) sb.Append('?');
                else sb.Append((char)code);
            }
            return sb.ToString();
        }

        // A PDF text string for metadata/bookmarks: UTF-16BE with BOM, as hex, so any character survives.
        private static string TextString(string s)
        {
            var sb = new StringBuilder("<FEFF");
            foreach (char ch in s) sb.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
            return sb.Append('>').ToString();
        }

        private static string BaseFontName(PdfFont font, bool bold) => font switch
        {
            PdfFont.Helvetica => bold ? "Helvetica-Bold" : "Helvetica",
            PdfFont.HelveticaBold => "Helvetica-Bold",
            PdfFont.TimesRoman => bold ? "Times-Bold" : "Times-Roman",
            _ => bold ? "Courier-Bold" : "Courier",
        };

        private int[]? Widths(bool bold) => _font switch
        {
            PdfFont.Helvetica => bold ? HelveticaBoldWidths : HelveticaWidths,
            PdfFont.HelveticaBold => HelveticaBoldWidths,
            PdfFont.TimesRoman => bold ? TimesBoldWidths : TimesWidths,
            _ => null,                                                         // Courier: 600 everywhere
        };

        private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static byte[] Zlib(byte[] data)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (byte x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a;
            ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
            return ms.ToArray();
        }

        // Minimal PNG decoder for report images: 8-bit, non-interlaced, any colour type (incl. palette and tRNS).
        private static (int Width, int Height, byte[] Rgba) DecodePng(byte[] png)
        {
            byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (png.Length < 33) throw new ArgumentException("Not a PNG image.", nameof(png));
            for (int i = 0; i < 8; i++) if (png[i] != sig[i]) throw new ArgumentException("Not a PNG image.", nameof(png));
            int width = 0, height = 0, depth = 0, type = 0, interlace = 0;
            byte[]? palette = null, trns = null;
            var idat = new MemoryStream();
            for (int pos = 8; pos + 8 <= png.Length;)
            {
                int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                if (len < 0 || pos + 12L + len > png.Length) throw new ArgumentException("Truncated PNG.", nameof(png));
                string kind = Encoding.ASCII.GetString(png, pos + 4, 4);
                int body = pos + 8;
                if (kind == "IHDR")
                {
                    width = (png[body] << 24) | (png[body + 1] << 16) | (png[body + 2] << 8) | png[body + 3];
                    height = (png[body + 4] << 24) | (png[body + 5] << 16) | (png[body + 6] << 8) | png[body + 7];
                    depth = png[body + 8]; type = png[body + 9]; interlace = png[body + 12];
                }
                else if (kind == "PLTE") { palette = new byte[len]; Array.Copy(png, body, palette, 0, len); }
                else if (kind == "tRNS") { trns = new byte[len]; Array.Copy(png, body, trns, 0, len); }
                else if (kind == "IDAT") idat.Write(png, body, len);
                else if (kind == "IEND") break;
                pos += 12 + len;
            }
            if (width <= 0 || height <= 0 || (long)width * height > 50_000_000) throw new ArgumentException("Unsupported PNG size.", nameof(png));
            if (depth != 8 || interlace != 0)
                throw new NotSupportedException("PdfWriter embeds 8-bit non-interlaced PNGs; re-encode the image (for example with PngWriter).");
            int channels = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new ArgumentException("Bad PNG colour type.", nameof(png)) };
            if (type == 3 && palette == null) throw new ArgumentException("Palette PNG without PLTE.", nameof(png));
            byte[] z = idat.ToArray();
            if (z.Length < 6) throw new ArgumentException("PNG has no image data.", nameof(png));
            int stride = width * channels;
            var raw = new byte[(long)height * (stride + 1)];
            using (var inflate = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int got = inflate.Read(raw, read, raw.Length - read);
                    if (got == 0) throw new ArgumentException("Truncated PNG image data.", nameof(png));
                    read += got;
                }
            }
            var rgba = new byte[width * height * 4];
            var prev = new byte[stride];
            var cur = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int filter = raw[(long)y * (stride + 1)];
                Array.Copy(raw, (long)y * (stride + 1) + 1, cur, 0, stride);
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= channels ? cur[i - channels] : 0, b = prev[i], c = i >= channels ? prev[i - channels] : 0;
                    int pred = filter switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) >> 1,
                        4 => Paeth(a, b, c),
                        _ => throw new ArgumentException("Bad PNG filter.", nameof(png)),
                    };
                    cur[i] = (byte)(cur[i] + pred);
                }
                for (int x = 0; x < width; x++)
                {
                    int s = x * channels, d = (y * width + x) * 4;
                    switch (type)
                    {
                        case 0:
                            rgba[d] = rgba[d + 1] = rgba[d + 2] = cur[s];
                            rgba[d + 3] = trns != null && trns.Length >= 2 && trns[1] == cur[s] && trns[0] == 0 ? (byte)0 : (byte)255;
                            break;
                        case 2:
                            rgba[d] = cur[s]; rgba[d + 1] = cur[s + 1]; rgba[d + 2] = cur[s + 2];
                            rgba[d + 3] = trns != null && trns.Length >= 6 && trns[1] == cur[s] && trns[3] == cur[s + 1] && trns[5] == cur[s + 2]
                                          && trns[0] == 0 && trns[2] == 0 && trns[4] == 0 ? (byte)0 : (byte)255;
                            break;
                        case 3:
                            int p = cur[s] * 3;
                            if (p + 2 >= palette!.Length) throw new ArgumentException("PNG palette index out of range.", nameof(png));
                            rgba[d] = palette[p]; rgba[d + 1] = palette[p + 1]; rgba[d + 2] = palette[p + 2];
                            rgba[d + 3] = trns != null && cur[s] < trns.Length ? trns[cur[s]] : (byte)255;
                            break;
                        case 4:
                            rgba[d] = rgba[d + 1] = rgba[d + 2] = cur[s];
                            rgba[d + 3] = cur[s + 1];
                            break;
                        default:
                            rgba[d] = cur[s]; rgba[d + 1] = cur[s + 1]; rgba[d + 2] = cur[s + 2]; rgba[d + 3] = cur[s + 3];
                            break;
                    }
                }
                (prev, cur) = (cur, prev);
            }
            return (width, height, rgba);
        }

        private static int Paeth(int a, int b, int c)
        {
            int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        // Base-14 advance widths (1/1000 em) for U+0020..U+007E. Taken from the metric-compatible Windows fonts
        // (Arial = Helvetica, Times New Roman = Times) and identical to Adobe's Core 14 AFM files.
        private static readonly int[] HelveticaWidths =
        {
            278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
            1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
            333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584,
        };

        private static readonly int[] HelveticaBoldWidths =
        {
            278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,333,333,584,584,584,611,
            975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,
            333,556,611,556,611,556,333,611,611,278,278,556,278,889,611,611,611,611,389,556,333,611,556,778,556,556,500,389,280,389,584,
        };

        private static readonly int[] TimesWidths =
        {
            250,333,408,500,500,833,778,180,333,333,500,564,250,333,250,278,500,500,500,500,500,500,500,500,500,500,278,278,564,564,564,444,
            921,722,667,667,722,611,556,722,722,333,389,722,611,889,722,722,556,722,667,556,611,722,722,944,722,722,611,333,278,333,469,500,
            333,444,500,444,500,444,333,500,500,278,278,500,278,778,500,500,500,500,333,389,278,500,500,722,500,500,444,480,200,480,541,
        };

        private static readonly int[] TimesBoldWidths =
        {
            250,333,555,500,500,1000,833,278,333,333,500,570,250,333,250,278,500,500,500,500,500,500,500,500,500,500,333,333,570,570,570,500,
            930,722,667,722,722,667,611,778,778,389,500,778,667,944,722,778,611,778,722,556,667,722,722,1000,722,722,667,333,278,333,581,500,
            333,500,556,444,556,444,333,500,556,278,333,556,278,833,556,500,556,556,444,389,333,556,500,722,500,500,444,394,220,394,520,
        };

#if NETSTANDARD2_0
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
#else
        private static readonly Encoding Latin1 = Encoding.Latin1;
#endif
    }
}
