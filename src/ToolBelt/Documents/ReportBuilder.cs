// ToolBelt drop-in — also copy Documents/HtmlReport.cs, MarkdownReport.cs, PdfWriter.cs and DocxWriter.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Documents
{
    /// <summary>The flavour of a <see cref="ReportBuilder.Callout"/>.</summary>
    public enum CalloutKind
    {
        Note,
        Success,
        Warning,
        Error,
    }

    /// <summary>
    /// Composes a report once from typed sections — a title and metadata block, headings, paragraphs, lists,
    /// key-value property tables, data tables, figures, code blocks, callouts and a generated table of contents —
    /// and renders it to several formats. HTML is the richest target; the others degrade predictably:
    /// <list type="bullet">
    /// <item><b>HTML</b> (<see cref="ToHtml"/>): everything. Figures inline as SVG (or PNG), a linked table of
    /// contents, styled callouts. Self-contained single file.</item>
    /// <item><b>Markdown</b> (<see cref="ToMarkdown"/>): GitHub-flavoured — anchored contents links, <c>&gt; [!WARNING]</c>
    /// alerts, pipe tables. Figures become data-URI images, which some viewers (GitHub among them) do not display;
    /// their captions always remain.</item>
    /// <item><b>PDF</b> (<see cref="ToPdf"/>): monospace text so tables align exactly; long table rows wrap. Figures
    /// are replaced by a captioned placeholder (the PDF writer has no image support); contents lists headings
    /// without page numbers.</item>
    /// <item><b>Word</b> (<see cref="ToDocx"/>): headings, paragraphs, lists and real tables. Code loses its monospace
    /// styling, figures become captioned placeholders, and the contents list is static text.</item>
    /// </list>
    /// Figures are supplied already rendered (SVG markup and/or PNG bytes — e.g. from the visualization renderers),
    /// so this file has no dependency on any plotting code. Tables and figures are numbered automatically.
    /// </summary>
    public sealed class ReportBuilder
    {
        private readonly List<Block> _blocks = new List<Block>();
        private readonly List<KeyValuePair<string, string>> _metadata = new List<KeyValuePair<string, string>>();

        public ReportBuilder(string title)
        {
            if (title is null) throw new ArgumentNullException(nameof(title));
            if (title.Trim().Length == 0) throw new ArgumentException("Title must not be blank.", nameof(title));
            Title = title;
        }

        public string Title { get; }

        /// <summary>The number of content blocks added (metadata excluded).</summary>
        public int Count => _blocks.Count;

        /// <summary>Adds a metadata entry shown under the title (author, date, run id, ...).</summary>
        public ReportBuilder Metadata(string key, string value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            _metadata.Add(new KeyValuePair<string, string>(key, value ?? ""));
            return this;
        }

        /// <summary>Marks where the generated table of contents goes (it lists every heading in the report).</summary>
        public ReportBuilder TableOfContents() => Add(new Block(Kind.Toc));

        /// <summary>A section heading, level 1 (top) to 4. The report title sits above all of them.</summary>
        public ReportBuilder Heading(string text, int level = 1)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (level < 1 || level > 4) throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1–4.");
            return Add(new Block(Kind.Heading) { Text = text, Level = level });
        }

        public ReportBuilder Paragraph(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Add(new Block(Kind.Paragraph) { Text = text });
        }

        public ReportBuilder BulletList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var list = new List<string>();
            foreach (string item in items) list.Add(item ?? "");
            return Add(new Block(Kind.Bullets) { Items = list });
        }

        /// <summary>A two-column key/value table (settings, results, environment).</summary>
        public ReportBuilder Properties(IEnumerable<KeyValuePair<string, string>> properties)
        {
            if (properties is null) throw new ArgumentNullException(nameof(properties));
            var rows = new List<IReadOnlyList<string>>();
            foreach (var kv in properties) rows.Add(new[] { kv.Key ?? "", kv.Value ?? "" });
            return Add(new Block(Kind.Properties) { Rows = rows });
        }

        /// <summary>Convenience overload: <c>Properties(("Operator", "J. Doe"), ("Station", "3"))</c>.</summary>
        public ReportBuilder Properties(params (string Key, string Value)[] properties)
        {
            if (properties is null) throw new ArgumentNullException(nameof(properties));
            var list = new List<KeyValuePair<string, string>>();
            foreach (var (k, v) in properties) list.Add(new KeyValuePair<string, string>(k, v));
            return Properties(list);
        }

        /// <summary>A data table. Rows are padded or truncated to the header's column count. Numbered "Table n".</summary>
        public ReportBuilder Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, string? caption = null)
        {
            if (headers is null) throw new ArgumentNullException(nameof(headers));
            if (rows is null) throw new ArgumentNullException(nameof(rows));
            if (headers.Count == 0) throw new ArgumentException("A table needs at least one column.", nameof(headers));
            var normalized = new List<IReadOnlyList<string>>();
            foreach (var row in rows)
            {
                var cells = new string[headers.Count];
                for (int c = 0; c < cells.Length; c++)
                    cells[c] = row != null && c < row.Count ? row[c] ?? "" : "";
                normalized.Add(cells);
            }
            return Add(new Block(Kind.Table) { Headers = headers, Rows = normalized, Caption = caption });
        }

        /// <summary>
        /// A figure, numbered "Figure n". Supply SVG markup, PNG bytes, or both: HTML prefers the SVG (crisp at any
        /// size), Markdown prefers the PNG (more widely displayed). Text outputs show the caption as a placeholder.
        /// </summary>
        public ReportBuilder Figure(string caption, string? svg = null, byte[]? png = null)
        {
            if (caption is null) throw new ArgumentNullException(nameof(caption));
            if (string.IsNullOrWhiteSpace(svg) && (png is null || png.Length == 0))
                throw new ArgumentException("A figure needs SVG markup or PNG bytes.");
            return Add(new Block(Kind.Figure) { Caption = caption, Svg = StripXmlDeclaration(svg), Png = png });
        }

        /// <summary>A preformatted code or log block.</summary>
        public ReportBuilder Code(string code, string language = "")
        {
            if (code is null) throw new ArgumentNullException(nameof(code));
            return Add(new Block(Kind.Code) { Text = code.Replace("\r\n", "\n"), Language = language ?? "" });
        }

        /// <summary>A highlighted note, success, warning or error box.</summary>
        public ReportBuilder Callout(CalloutKind kind, string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Add(new Block(Kind.Callout) { Text = text, Callout = kind });
        }

        // ================= HTML =================

        /// <summary>A complete, self-contained HTML document.</summary>
        public string ToHtml()
        {
            var anchors = new Anchors();
            string titleId = anchors.Next(Title);
            var headings = CollectHeadings(anchors);
            var html = new HtmlReport().Title(Title);
            html.Raw("<style>" + ExtraCss + "</style>");
            html.Raw("<header><h1 id=\"" + Attr(titleId) + "\">" + Esc(Title) + "</h1>");
            if (_metadata.Count > 0)
            {
                var sb = new StringBuilder("<table class=\"meta\">");
                foreach (var kv in _metadata) sb.Append("<tr><th>").Append(Esc(kv.Key)).Append("</th><td>").Append(Esc(kv.Value)).Append("</td></tr>");
                html.Raw(sb.Append("</table>").ToString());
            }
            html.Raw("</header>");

            int table = 0, figure = 0, heading = 0;
            foreach (Block b in _blocks)
            {
                switch (b.Kind)
                {
                    case Kind.Toc:
                    {
                        var sb = new StringBuilder("<nav class=\"toc\"><p class=\"toc-title\">Contents</p><ul>");
                        foreach (var h in headings)
                            sb.Append("<li class=\"l").Append(h.Level).Append("\"><a href=\"#").Append(Attr(h.Id)).Append("\">")
                              .Append(Esc(h.Text)).Append("</a></li>");
                        html.Raw(sb.Append("</ul></nav>").ToString());
                        break;
                    }
                    case Kind.Heading:
                    {
                        int tag = b.Level + 1; // h1 is the report title
                        html.Raw("<h" + tag + " id=\"" + Attr(headings[heading++].Id) + "\">" + Esc(b.Text!) + "</h" + tag + ">");
                        break;
                    }
                    case Kind.Paragraph: html.Paragraph(b.Text!); break;
                    case Kind.Bullets: html.BulletList(b.Items!); break;
                    case Kind.Properties:
                    {
                        var sb = new StringBuilder("<table class=\"props\">");
                        foreach (var r in b.Rows!) sb.Append("<tr><th>").Append(Esc(r[0])).Append("</th><td>").Append(Esc(r[1])).Append("</td></tr>");
                        html.Raw(sb.Append("</table>").ToString());
                        break;
                    }
                    case Kind.Table:
                        table++;
                        if (!string.IsNullOrEmpty(b.Caption))
                            html.Raw("<p class=\"caption\">Table " + table.ToString(CultureInfo.InvariantCulture) + ": " + Esc(b.Caption!) + "</p>");
                        html.Table(b.Headers!, b.Rows!);
                        break;
                    case Kind.Figure:
                    {
                        figure++;
                        var sb = new StringBuilder("<figure>");
                        if (!string.IsNullOrWhiteSpace(b.Svg)) sb.Append(b.Svg);
                        else sb.Append("<img alt=\"").Append(Attr(b.Caption!)).Append("\" src=\"data:image/png;base64,")
                               .Append(Convert.ToBase64String(b.Png!)).Append("\">");
                        sb.Append("<figcaption>Figure ").Append(figure.ToString(CultureInfo.InvariantCulture)).Append(": ")
                          .Append(Esc(b.Caption!)).Append("</figcaption></figure>");
                        html.Raw(sb.ToString());
                        break;
                    }
                    case Kind.Code: html.CodeBlock(b.Text!, b.Language!); break;
                    case Kind.Callout:
                        html.Raw("<div class=\"callout " + CalloutClass(b.Callout) + "\"><strong>" + CalloutLabel(b.Callout) + ":</strong> "
                            + Esc(b.Text!) + "</div>");
                        break;
                }
            }
            return html.ToString();
        }

        private const string ExtraCss =
            "header{border-bottom:2px solid #ddd;margin-bottom:1.5rem}"
            + "table.meta,table.props{width:auto}table.meta th,table.props th{background:#fafafa;font-weight:600}"
            + "table.meta{border:none;margin:.5rem 0 1rem}table.meta th,table.meta td{border:none;padding:.1rem 1rem .1rem 0}"
            + ".caption{font-weight:600;margin-bottom:.25rem}"
            + "figure{margin:1.25rem 0;text-align:center}figure svg{max-width:100%;height:auto}"
            + "figcaption{font-size:.9rem;color:#555;margin-top:.4rem}"
            + ".toc{background:#fafafa;border:1px solid #e5e5e5;border-radius:4px;padding:.5rem 1rem;margin:1rem 0}"
            + ".toc ul{list-style:none;padding-left:0;margin:.25rem 0}.toc .l2{margin-left:1.25rem}"
            + ".toc .l3{margin-left:2.5rem}.toc .l4{margin-left:3.75rem}.toc-title{font-weight:600;margin:.25rem 0}"
            + ".callout{border-left:4px solid;border-radius:3px;padding:.6rem .9rem;margin:1rem 0}"
            + ".callout.note{border-color:#3b82f6;background:#eff6ff}.callout.success{border-color:#16a34a;background:#f0fdf4}"
            + ".callout.warning{border-color:#d97706;background:#fffbeb}.callout.error{border-color:#dc2626;background:#fef2f2}"
            + "@media print{.toc{break-after:auto}figure{break-inside:avoid}}";

        // ================= Markdown =================

        /// <summary>GitHub-flavoured Markdown. See the class summary for what this target loses.</summary>
        public string ToMarkdown()
        {
            var anchors = new Anchors();
            anchors.Next(Title);
            var headings = CollectHeadings(anchors);
            var md = new MarkdownReport().Heading(1, Title);
            if (_metadata.Count > 0)
            {
                var lines = new List<string>();
                foreach (var kv in _metadata) lines.Add(MarkdownReport.Bold(kv.Key + ":") + " " + EscMd(kv.Value));
                md.Raw(string.Join("  \n", lines)); // hard line breaks keep the block compact
            }

            int table = 0, figure = 0, heading = 0;
            foreach (Block b in _blocks)
            {
                switch (b.Kind)
                {
                    case Kind.Toc:
                    {
                        var sb = new StringBuilder("**Contents**\n\n");
                        foreach (var h in headings)
                            sb.Append(new string(' ', (h.Level - 1) * 2)).Append("- [").Append(EscMd(h.Text)).Append("](#").Append(h.Id).Append(")\n");
                        md.Raw(sb.ToString());
                        break;
                    }
                    case Kind.Heading: md.Heading(b.Level + 1, b.Text!); heading++; break;
                    case Kind.Paragraph: md.Paragraph(b.Text!); break;
                    case Kind.Bullets: md.BulletList(b.Items!); break;
                    case Kind.Properties: md.Table(new[] { "Property", "Value" }, b.Rows!); break;
                    case Kind.Table:
                        table++;
                        if (!string.IsNullOrEmpty(b.Caption))
                            md.Raw(MarkdownReport.Bold("Table " + table.ToString(CultureInfo.InvariantCulture) + ": " + b.Caption));
                        md.Table(b.Headers!, b.Rows!);
                        break;
                    case Kind.Figure:
                    {
                        figure++;
                        string label = "Figure " + figure.ToString(CultureInfo.InvariantCulture) + ": " + b.Caption;
                        string uri = b.Png != null && b.Png.Length > 0
                            ? "data:image/png;base64," + Convert.ToBase64String(b.Png)
                            : "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(b.Svg!));
                        md.Raw("![" + EscMd(label) + "](" + uri + ")\n\n" + MarkdownReport.Italic(label));
                        break;
                    }
                    case Kind.Code: md.CodeBlock(b.Text!, b.Language!); break;
                    case Kind.Callout:
                    {
                        var sb = new StringBuilder("> [!").Append(GithubAlert(b.Callout)).Append("]\n");
                        foreach (string line in b.Text!.Replace("\r\n", "\n").Split('\n')) sb.Append("> ").Append(line).Append('\n');
                        md.Raw(sb.ToString());
                        break;
                    }
                }
            }
            return md.ToString();
        }

        // ================= PDF =================

        /// <summary>
        /// A PDF in a monospace font (so tables align exactly). Figures become captioned placeholders and the contents
        /// list has no page numbers — see the class summary.
        /// </summary>
        public byte[] ToPdf(PdfPageSize pageSize = PdfPageSize.Letter, double fontSize = 9.5)
        {
            var pdf = new PdfWriter(pageSize, 54, PdfFont.Courier, fontSize);
            double width = pageSize == PdfPageSize.A4 ? 595 : 612;
            int columns = Math.Max(20, (int)((width - 2 * 54) / (fontSize * 0.6)));
            var headings = CollectHeadings(new Anchors());

            pdf.Heading(Title, fontSize * 1.8);
            if (_metadata.Count > 0) pdf.Lines(AlignedPairs(_metadata, columns)).Space(fontSize);

            int table = 0, figure = 0;
            foreach (Block b in _blocks)
            {
                switch (b.Kind)
                {
                    case Kind.Toc:
                    {
                        pdf.Heading("Contents", fontSize * 1.3);
                        var lines = new List<string>();
                        foreach (var h in headings) lines.Add(new string(' ', (h.Level - 1) * 2) + h.Text);
                        pdf.Lines(lines).Space(fontSize);
                        break;
                    }
                    case Kind.Heading: pdf.Heading(b.Text!, fontSize * (1.6 - 0.15 * b.Level)); break;
                    case Kind.Paragraph: pdf.Paragraph(b.Text!); break;
                    case Kind.Bullets:
                    {
                        var lines = new List<string>();
                        foreach (string item in b.Items!) lines.Add("  * " + item);
                        pdf.Lines(lines).Space(fontSize * 0.6);
                        break;
                    }
                    case Kind.Properties:
                    {
                        var pairs = new List<KeyValuePair<string, string>>();
                        foreach (var r in b.Rows!) pairs.Add(new KeyValuePair<string, string>(r[0], r[1]));
                        pdf.Lines(AlignedPairs(pairs, columns)).Space(fontSize * 0.6);
                        break;
                    }
                    case Kind.Table:
                        table++;
                        if (!string.IsNullOrEmpty(b.Caption)) pdf.Lines(new[] { "Table " + table.ToString(CultureInfo.InvariantCulture) + ": " + b.Caption });
                        pdf.Lines(TextTable(b.Headers!, b.Rows!)).Space(fontSize * 0.6);
                        break;
                    case Kind.Figure:
                        figure++;
                        pdf.Paragraph("[Figure " + figure.ToString(CultureInfo.InvariantCulture) + ": " + b.Caption + " - graphics are not rendered in PDF output]");
                        break;
                    case Kind.Code:
                    {
                        var lines = new List<string>();
                        foreach (string line in b.Text!.Split('\n')) lines.Add("  " + line);
                        pdf.Lines(lines).Space(fontSize * 0.6);
                        break;
                    }
                    case Kind.Callout: pdf.Paragraph(CalloutLabel(b.Callout).ToUpperInvariant() + ": " + b.Text); break;
                }
            }
            return pdf.Build();
        }

        // ================= Word =================

        /// <summary>A .docx document. Code loses monospace styling and figures become placeholders — see the class summary.</summary>
        public byte[] ToDocx()
        {
            var doc = new DocxWriter().Heading(Title, 1);
            if (_metadata.Count > 0)
            {
                var rows = new List<IReadOnlyList<string>>();
                foreach (var kv in _metadata) rows.Add(new[] { kv.Key, kv.Value });
                doc.Table(new[] { "Property", "Value" }, rows);
            }
            var headings = CollectHeadings(new Anchors());
            int table = 0, figure = 0;
            foreach (Block b in _blocks)
            {
                switch (b.Kind)
                {
                    case Kind.Toc:
                    {
                        doc.Paragraph("Contents", bold: true);
                        var items = new List<string>();
                        foreach (var h in headings) items.Add(new string(' ', (h.Level - 1) * 3) + h.Text);
                        doc.BulletList(items);
                        break;
                    }
                    case Kind.Heading: doc.Heading(b.Text!, b.Level + 1); break;
                    case Kind.Paragraph: doc.Paragraph(b.Text!); break;
                    case Kind.Bullets: doc.BulletList(b.Items!); break;
                    case Kind.Properties: doc.Table(new[] { "Property", "Value" }, b.Rows!); break;
                    case Kind.Table:
                        table++;
                        if (!string.IsNullOrEmpty(b.Caption)) doc.Paragraph("Table " + table.ToString(CultureInfo.InvariantCulture) + ": " + b.Caption, bold: true);
                        doc.Table(b.Headers!, b.Rows!);
                        break;
                    case Kind.Figure:
                        figure++;
                        doc.Paragraph("[Figure " + figure.ToString(CultureInfo.InvariantCulture) + ": " + b.Caption + " - see the HTML version for graphics]", italic: true);
                        break;
                    case Kind.Code:
                        foreach (string line in b.Text!.Split('\n')) doc.Paragraph(line.Length == 0 ? " " : line);
                        break;
                    case Kind.Callout: doc.Paragraph(CalloutLabel(b.Callout) + ": " + b.Text, bold: true); break;
                }
            }
            return doc.Build();
        }

        // ================= helpers =================

        private ReportBuilder Add(Block b)
        {
            _blocks.Add(b);
            return this;
        }

        private List<HeadingRef> CollectHeadings(Anchors anchors)
        {
            var list = new List<HeadingRef>();
            foreach (Block b in _blocks)
                if (b.Kind == Kind.Heading) list.Add(new HeadingRef(b.Text!, b.Level, anchors.Next(b.Text!)));
            return list;
        }

        /// <summary>The anchor id GitHub (and this HTML output) gives a heading: lower-case, punctuation dropped, spaces to hyphens.</summary>
        public static string Slug(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(ch);
                else if (ch == ' ') sb.Append('-');
            }
            return sb.Length == 0 ? "section" : sb.ToString();
        }

        private sealed class Anchors
        {
            private readonly Dictionary<string, int> _seen = new Dictionary<string, int>(StringComparer.Ordinal);

            // GitHub's de-duplication: the first "x" stays "x", repeats become "x-1", "x-2", ...
            public string Next(string text)
            {
                string slug = Slug(text);
                if (!_seen.TryGetValue(slug, out int n)) { _seen[slug] = 0; return slug; }
                _seen[slug] = ++n;
                string candidate = slug + "-" + n.ToString(CultureInfo.InvariantCulture);
                _seen[candidate] = 0;
                return candidate;
            }
        }

        private static List<string> AlignedPairs(IReadOnlyList<KeyValuePair<string, string>> pairs, int columns)
        {
            int keyWidth = 0;
            foreach (var kv in pairs) keyWidth = Math.Max(keyWidth, kv.Key.Length);
            keyWidth = Math.Min(keyWidth, Math.Max(8, columns / 3));
            var lines = new List<string>();
            foreach (var kv in pairs)
                lines.Add((kv.Key.Length > keyWidth ? kv.Key.Substring(0, keyWidth) : kv.Key.PadRight(keyWidth)) + " : " + kv.Value);
            return lines;
        }

        private static List<string> TextTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var widths = new int[headers.Count];
            for (int c = 0; c < headers.Count; c++) widths[c] = headers[c].Length;
            foreach (var r in rows)
                for (int c = 0; c < widths.Length; c++) widths[c] = Math.Max(widths[c], r[c].Length);
            string Row(IReadOnlyList<string> cells)
            {
                var sb = new StringBuilder();
                for (int c = 0; c < widths.Length; c++)
                {
                    if (c > 0) sb.Append(" | ");
                    sb.Append(cells[c].PadRight(widths[c]));
                }
                return sb.ToString().TrimEnd();
            }
            var lines = new List<string> { Row(headers) };
            var rule = new StringBuilder();
            for (int c = 0; c < widths.Length; c++)
            {
                if (c > 0) rule.Append("-+-");
                rule.Append('-', widths[c]);
            }
            lines.Add(rule.ToString());
            foreach (var r in rows) lines.Add(Row(r));
            return lines;
        }

        private static string? StripXmlDeclaration(string? svg)
        {
            if (string.IsNullOrWhiteSpace(svg)) return svg;
            string s = svg!.TrimStart();
            if (s.StartsWith("<?xml", StringComparison.Ordinal))
            {
                int end = s.IndexOf("?>", StringComparison.Ordinal);
                if (end >= 0) s = s.Substring(end + 2).TrimStart();
            }
            return s;
        }

        private static string CalloutLabel(CalloutKind k) => k switch
        {
            CalloutKind.Success => "Success",
            CalloutKind.Warning => "Warning",
            CalloutKind.Error => "Error",
            _ => "Note",
        };

        private static string CalloutClass(CalloutKind k) => CalloutLabel(k).ToLowerInvariant();

        private static string GithubAlert(CalloutKind k) => k switch
        {
            CalloutKind.Success => "TIP",
            CalloutKind.Warning => "WARNING",
            CalloutKind.Error => "CAUTION",
            _ => "NOTE",
        };

        private static string Esc(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default: sb.Append(ch); break;
                }
            return sb.ToString();
        }

        private static string Attr(string text) => Esc(text).Replace("\"", "&quot;");

        // Escape the characters that would otherwise start Markdown syntax inside inline text.
        private static string EscMd(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if ("\\`*_[]<>|#".IndexOf(ch) >= 0) sb.Append('\\');
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private enum Kind { Toc, Heading, Paragraph, Bullets, Properties, Table, Figure, Code, Callout }

        private sealed class Block
        {
            public Block(Kind kind) => Kind = kind;
            public Kind Kind { get; }
            public string? Text { get; set; }
            public int Level { get; set; }
            public List<string>? Items { get; set; }
            public IReadOnlyList<string>? Headers { get; set; }
            public List<IReadOnlyList<string>>? Rows { get; set; }
            public string? Caption { get; set; }
            public string? Svg { get; set; }
            public byte[]? Png { get; set; }
            public string? Language { get; set; }
            public CalloutKind Callout { get; set; }
        }

        private readonly struct HeadingRef
        {
            public HeadingRef(string text, int level, string id) { Text = text; Level = level; Id = id; }
            public string Text { get; }
            public int Level { get; }
            public string Id { get; }
        }
    }
}
