// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Documents
{
    /// <summary>
    /// A fluent builder for self-contained HTML documents — headings, paragraphs, lists, tables, code,
    /// images and links, wrapped in a minimal responsive stylesheet. All text is HTML-escaped, so caller
    /// data can never break out into markup. <see cref="ToString"/> returns a complete <c>&lt;html&gt;</c>
    /// document; <see cref="ToFragment"/> returns just the body content for embedding.
    /// </summary>
    public sealed class HtmlReport
    {
        private readonly StringBuilder _body = new StringBuilder();
        private string _title = "Report";

        private const string DefaultCss =
            "body{font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;line-height:1.5;"
            + "max-width:56rem;margin:2rem auto;padding:0 1rem;color:#1a1a1a}"
            + "table{border-collapse:collapse;width:100%;margin:1rem 0}"
            + "th,td{border:1px solid #ddd;padding:.4rem .6rem;text-align:left}"
            + "th{background:#f4f4f4}code{background:#f4f4f4;padding:.1rem .3rem;border-radius:3px}"
            + "pre{background:#f4f4f4;padding:1rem;overflow:auto;border-radius:4px}"
            + "pre code{background:none;padding:0}blockquote{border-left:4px solid #ddd;margin:1rem 0;"
            + "padding:.2rem 1rem;color:#555}img{max-width:100%}";

        /// <summary>Sets the document title (used in <c>&lt;title&gt;</c> and, unless suppressed, an <c>&lt;h1&gt;</c>).</summary>
        public HtmlReport Title(string title)
        {
            _title = title ?? throw new ArgumentNullException(nameof(title));
            return this;
        }

        /// <summary>Adds a heading of the given level (1–6).</summary>
        public HtmlReport Heading(int level, string text)
        {
            if (level < 1 || level > 6) throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1–6.");
            if (text is null) throw new ArgumentNullException(nameof(text));
            _body.Append($"<h{level}>").Append(Escape(text)).Append($"</h{level}>\n");
            return this;
        }

        /// <summary>Level-1 heading.</summary>
        public HtmlReport H1(string text) => Heading(1, text);
        /// <summary>Level-2 heading.</summary>
        public HtmlReport H2(string text) => Heading(2, text);
        /// <summary>Level-3 heading.</summary>
        public HtmlReport H3(string text) => Heading(3, text);

        /// <summary>Adds a paragraph.</summary>
        public HtmlReport Paragraph(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            _body.Append("<p>").Append(Escape(text)).Append("</p>\n");
            return this;
        }

        /// <summary>Adds a bulleted (<c>ul</c>) list.</summary>
        public HtmlReport BulletList(IEnumerable<string> items) => List("ul", items);

        /// <summary>Adds a numbered (<c>ol</c>) list.</summary>
        public HtmlReport NumberedList(IEnumerable<string> items) => List("ol", items);

        /// <summary>Adds a table with a header row.</summary>
        public HtmlReport Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        {
            if (headers is null) throw new ArgumentNullException(nameof(headers));
            if (rows is null) throw new ArgumentNullException(nameof(rows));

            _body.Append("<table>\n<thead>\n<tr>");
            foreach (var h in headers) _body.Append("<th>").Append(Escape(h ?? "")).Append("</th>");
            _body.Append("</tr>\n</thead>\n<tbody>\n");
            foreach (var row in rows)
            {
                _body.Append("<tr>");
                foreach (var cell in row) _body.Append("<td>").Append(Escape(cell ?? "")).Append("</td>");
                _body.Append("</tr>\n");
            }
            _body.Append("</tbody>\n</table>\n");
            return this;
        }

        /// <summary>Adds a preformatted code block.</summary>
        public HtmlReport CodeBlock(string code, string language = "")
        {
            if (code is null) throw new ArgumentNullException(nameof(code));
            string cls = string.IsNullOrEmpty(language) ? "" : $" class=\"language-{Escape(language)}\"";
            _body.Append("<pre><code").Append(cls).Append('>').Append(Escape(code)).Append("</code></pre>\n");
            return this;
        }

        /// <summary>Adds a block quote.</summary>
        public HtmlReport Quote(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            _body.Append("<blockquote>").Append(Escape(text)).Append("</blockquote>\n");
            return this;
        }

        /// <summary>Adds an image by URL (or a <c>data:</c> URI — see <see cref="ImageData"/>).</summary>
        public HtmlReport Image(string altText, string url)
        {
            if (url is null) throw new ArgumentNullException(nameof(url));
            _body.Append("<img alt=\"").Append(EscapeAttr(altText ?? "")).Append("\" src=\"")
                 .Append(EscapeAttr(url)).Append("\">\n");
            return this;
        }

        /// <summary>Embeds binary image data as a base-64 <c>data:</c> URI (e.g. a PNG from the Visualization division).</summary>
        public HtmlReport ImageData(string altText, byte[] data, string mimeType = "image/png")
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            return Image(altText, $"data:{mimeType};base64,{Convert.ToBase64String(data)}");
        }

        /// <summary>Adds a horizontal rule.</summary>
        public HtmlReport HorizontalRule()
        {
            _body.Append("<hr>\n");
            return this;
        }

        /// <summary>Appends already-formed HTML verbatim (NOT escaped — the caller owns its safety).</summary>
        public HtmlReport Raw(string html)
        {
            if (html is null) throw new ArgumentNullException(nameof(html));
            _body.Append(html);
            if (!html.EndsWith("\n", StringComparison.Ordinal)) _body.Append('\n');
            return this;
        }

        /// <summary>An escaped inline hyperlink, for composing into paragraph text via <see cref="Raw"/>.</summary>
        public static string Link(string text, string url)
            => $"<a href=\"{EscapeAttr(url ?? "")}\">{Escape(text ?? "")}</a>";

        /// <summary>Just the body content, without the surrounding document scaffolding.</summary>
        public string ToFragment() => _body.ToString();

        /// <summary>The complete HTML document.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
            sb.Append("<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append("<title>").Append(Escape(_title)).Append("</title>\n");
            sb.Append("<style>").Append(DefaultCss).Append("</style>\n");
            sb.Append("</head>\n<body>\n");
            sb.Append(_body);
            sb.Append("</body>\n</html>\n");
            return sb.ToString();
        }

        private HtmlReport List(string tag, IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            _body.Append('<').Append(tag).Append(">\n");
            foreach (var item in items) _body.Append("<li>").Append(Escape(item ?? "")).Append("</li>\n");
            _body.Append("</").Append(tag).Append(">\n");
            return this;
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
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private static string EscapeAttr(string text)
        {
            return Escape(text).Replace("\"", "&quot;");
        }
    }
}
