// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Documents
{
    /// <summary>Column alignment for a Markdown table.</summary>
    public enum MarkdownAlign
    {
        /// <summary>No explicit alignment (renderer default).</summary>
        None,
        /// <summary>Left-aligned (<c>:---</c>).</summary>
        Left,
        /// <summary>Centered (<c>:---:</c>).</summary>
        Center,
        /// <summary>Right-aligned (<c>---:</c>).</summary>
        Right,
    }

    /// <summary>
    /// A fluent builder for GitHub-flavored Markdown documents — headings, paragraphs, lists, tables,
    /// code fences, quotes and rules. Every method returns the builder so calls chain; <see cref="ToString"/>
    /// yields the finished document. Cell and inline text are escaped so stray <c>|</c> or <c>*</c> don't
    /// corrupt the layout.
    /// </summary>
    public sealed class MarkdownReport
    {
        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>Adds an ATX heading of the given level (1–6).</summary>
        public MarkdownReport Heading(int level, string text)
        {
            if (level < 1 || level > 6) throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1–6.");
            if (text is null) throw new ArgumentNullException(nameof(text));
            Block(new string('#', level) + " " + text.Trim());
            return this;
        }

        /// <summary>Level-1 heading.</summary>
        public MarkdownReport H1(string text) => Heading(1, text);
        /// <summary>Level-2 heading.</summary>
        public MarkdownReport H2(string text) => Heading(2, text);
        /// <summary>Level-3 heading.</summary>
        public MarkdownReport H3(string text) => Heading(3, text);

        /// <summary>Adds a paragraph (a block separated by a blank line).</summary>
        public MarkdownReport Paragraph(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            Block(text.Trim());
            return this;
        }

        /// <summary>Adds a bulleted list.</summary>
        public MarkdownReport BulletList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var sb = new StringBuilder();
            foreach (var item in items) sb.Append("- ").Append((item ?? "").Trim()).Append('\n');
            if (sb.Length > 0) Block(sb.ToString().TrimEnd('\n'));
            return this;
        }

        /// <summary>Adds a numbered list (renderers renumber, so every marker is <c>1.</c>).</summary>
        public MarkdownReport NumberedList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var sb = new StringBuilder();
            foreach (var item in items) sb.Append("1. ").Append((item ?? "").Trim()).Append('\n');
            if (sb.Length > 0) Block(sb.ToString().TrimEnd('\n'));
            return this;
        }

        /// <summary>Adds a pipe table. Each row is padded/truncated to the header column count.</summary>
        public MarkdownReport Table(
            IReadOnlyList<string> headers,
            IEnumerable<IReadOnlyList<string>> rows,
            IReadOnlyList<MarkdownAlign>? alignments = null)
        {
            if (headers is null) throw new ArgumentNullException(nameof(headers));
            if (rows is null) throw new ArgumentNullException(nameof(rows));
            if (headers.Count == 0) throw new ArgumentException("A table needs at least one column.", nameof(headers));

            int cols = headers.Count;
            var sb = new StringBuilder();
            AppendRow(sb, headers, cols);

            sb.Append('|');
            for (int c = 0; c < cols; c++)
            {
                var a = alignments != null && c < alignments.Count ? alignments[c] : MarkdownAlign.None;
                sb.Append(a switch
                {
                    MarkdownAlign.Left => " :--- ",
                    MarkdownAlign.Center => " :---: ",
                    MarkdownAlign.Right => " ---: ",
                    _ => " --- ",
                });
                sb.Append('|');
            }
            sb.Append('\n');

            foreach (var row in rows) AppendRow(sb, row, cols);
            Block(sb.ToString().TrimEnd('\n'));
            return this;
        }

        /// <summary>Adds a fenced code block with an optional language hint.</summary>
        public MarkdownReport CodeBlock(string code, string language = "")
        {
            if (code is null) throw new ArgumentNullException(nameof(code));
            Block("```" + (language ?? "") + "\n" + code.TrimEnd('\n') + "\n```");
            return this;
        }

        /// <summary>Adds a block quote (each line prefixed with <c>&gt; </c>).</summary>
        public MarkdownReport Quote(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var sb = new StringBuilder();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                sb.Append("> ").Append(line).Append('\n');
            Block(sb.ToString().TrimEnd('\n'));
            return this;
        }

        /// <summary>Adds a standalone image.</summary>
        public MarkdownReport Image(string altText, string url)
        {
            if (url is null) throw new ArgumentNullException(nameof(url));
            Block($"![{Escape(altText ?? "")}]({url})");
            return this;
        }

        /// <summary>Adds a horizontal rule.</summary>
        public MarkdownReport HorizontalRule()
        {
            Block("---");
            return this;
        }

        /// <summary>Appends already-formatted Markdown verbatim as its own block.</summary>
        public MarkdownReport Raw(string markdown)
        {
            if (markdown is null) throw new ArgumentNullException(nameof(markdown));
            Block(markdown.Trim());
            return this;
        }

        /// <summary>Bold inline span.</summary>
        public static string Bold(string text) => "**" + Escape(text) + "**";
        /// <summary>Italic inline span.</summary>
        public static string Italic(string text) => "*" + Escape(text) + "*";
        /// <summary>Inline code span.</summary>
        public static string Code(string text) => "`" + (text ?? "").Replace("`", "\\`") + "`";
        /// <summary>Inline link.</summary>
        public static string Link(string text, string url) => "[" + Escape(text) + "](" + (url ?? "") + ")";

        /// <summary>The finished Markdown document.</summary>
        public override string ToString() => _sb.ToString().TrimEnd('\n') + "\n";

        private void AppendRow(StringBuilder sb, IReadOnlyList<string> cells, int cols)
        {
            sb.Append('|');
            for (int c = 0; c < cols; c++)
            {
                string value = cells != null && c < cells.Count ? cells[c] ?? "" : "";
                sb.Append(' ').Append(EscapeCell(value)).Append(' ').Append('|');
            }
            sb.Append('\n');
        }

        private void Block(string content)
        {
            if (_sb.Length > 0) _sb.Append('\n');
            _sb.Append(content).Append('\n');
        }

        // Escape the characters that would otherwise be read as Markdown syntax in inline text.
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (ch == '\\' || ch == '`' || ch == '*' || ch == '_' || ch == '[' || ch == ']'
                    || ch == '(' || ch == ')' || ch == '#' || ch == '!')
                    sb.Append('\\');
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // Table cells additionally escape pipes and collapse newlines (a cell is single-line).
        private static string EscapeCell(string text)
        {
            return Escape(text).Replace("\r\n", " ").Replace("\n", "<br>").Replace("|", "\\|");
        }
    }
}
