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
    /// yields the finished document. Cell and inline-helper text is escaped so stray <c>|</c>, <c>*</c> or raw HTML
    /// (<c>&lt;</c>, <c>&amp;</c>) don't corrupt the layout; headings and paragraphs are Markdown as given, so they can
    /// carry <see cref="Bold"/>, <see cref="Link"/> and friends. Code fences and spans grow longer than any backtick
    /// run inside them. Two lists in a row stay separate (the second uses the other marker), and the line breaks in an
    /// inline span, code span, link text or image alt text become spaces.
    /// </summary>
    public sealed class MarkdownReport
    {
        private readonly StringBuilder _sb = new StringBuilder();
        // The marker of the list just written ('\0' when the last block was not a list): the same marker after the blank
        // line continued that list, so two calls rendered as one (loose) list.
        private char _lastMarker;

        /// <summary>Adds an ATX heading of the given level (1–6).</summary>
        public MarkdownReport Heading(int level, string text)
        {
            if (level < 1 || level > 6) throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1–6.");
            if (text is null) throw new ArgumentNullException(nameof(text));
            // A heading is one line: "a\nb" rendered as the heading "a" and a paragraph "b".
            text = OneLine(text).Trim();
            // A trailing run of '#' after a space (or alone) is a closing sequence, so "Issue #" rendered as "Issue".
            int run = text.Length;
            while (run > 0 && text[run - 1] == '#') run--;
            if (run < text.Length && (run == 0 || text[run - 1] == ' ' || text[run - 1] == '\t')) text = text.Insert(run, "\\");
            Block(new string('#', level) + " " + text);
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
            // A paragraph is Markdown, as Raw is: one starting "1. x" right after a numbered list joined it as an item, and
            // one starting "- x" took in the bullet list after it.
            return Raw(text);
        }

        /// <summary>Adds a bulleted list.</summary>
        public MarkdownReport BulletList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var sb = new StringBuilder();
            char marker = _lastMarker == '-' ? '*' : '-';                       // a '*' or '+' list is already different
            foreach (var item in items) sb.Append(marker).Append(' ').Append(ItemText(item, 2)).Append('\n');
            if (sb.Length > 0) { Block(sb.ToString().TrimEnd('\n')); _lastMarker = marker; }
            return this;
        }

        /// <summary>Adds a numbered list (renderers renumber, so every marker is <c>1.</c>).</summary>
        public MarkdownReport NumberedList(IEnumerable<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            var sb = new StringBuilder();
            char marker = _lastMarker == '.' ? ')' : '.';
            foreach (var item in items) sb.Append('1').Append(marker).Append(' ').Append(ItemText(item, 3)).Append('\n');
            if (sb.Length > 0) { Block(sb.ToString().TrimEnd('\n')); _lastMarker = marker; }
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
                // An undefined value was written as no alignment.
                if (a < MarkdownAlign.None || a > MarkdownAlign.Right)
                    throw new ArgumentOutOfRangeException(nameof(alignments), a, "Unknown alignment.");
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
            // The fence must be longer than any backtick run in the code, or a ``` line inside would close it early.
            string fence = new string('`', Math.Max(3, LongestRun(code, '`') + 1));
            string info = (language ?? "").Replace("`", "").Replace("\r", " ").Replace("\n", " ").Trim();
            // Empty code wrote a blank line between the fences, which renders as one empty line of code.
            string body = code.TrimEnd('\r', '\n');
            Block(fence + info + "\n" + (body.Length == 0 ? "" : body + "\n") + fence);
            return this;
        }

        /// <summary>Adds a block quote (each line prefixed with <c>&gt; </c>).</summary>
        public MarkdownReport Quote(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var sb = new StringBuilder();
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                sb.Append("> ").Append(line).Append('\n');
            Block(sb.ToString().TrimEnd('\n'));
            return this;
        }

        /// <summary>Adds a standalone image.</summary>
        public MarkdownReport Image(string altText, string url)
        {
            if (url is null) throw new ArgumentNullException(nameof(url));
            Block($"![{Escape(OneLine(altText))}]({Destination(url)})");
            return this;
        }

        /// <summary>Adds a horizontal rule.</summary>
        public MarkdownReport HorizontalRule()
        {
            Block("---");
            return this;
        }

        /// <summary>
        /// Appends already-formatted Markdown verbatim as its own block, less any blank lines before it and white space
        /// after it.
        /// </summary>
        public MarkdownReport Raw(string markdown)
        {
            if (markdown is null) throw new ArgumentNullException(nameof(markdown));
            // Not Trim: the first line's indentation is Markdown ("    x = 1" is code), and trimming it made the block a
            // paragraph with a lazy continuation line.
            string block = markdown.TrimEnd();
            int from = 0;
            for (int k = 0; k < block.Length && char.IsWhiteSpace(block[k]); k++)
                if (block[k] == '\n' || block[k] == '\r') from = k + 1;
            block = block.Substring(from);
            // A block that starts with an item of the list just written joined that list ("1. x" after a numbered list):
            // an empty HTML comment between them ends the list.
            if (_lastMarker != '\0' && FirstItemMarker(block) == _lastMarker) Block("<!-- -->");
            Block(block);
            // Raw Markdown that ends in a list (a hand-built table of contents) is a list too: the next list must not join it.
            // The list may end in a continuation or lazy line ("- a\n  b", "- a\nlazy"), so the last item line anywhere in
            // the block sets the marker; only the last line was looked at, and the next list joined it. Only an outer item
            // counts: a nested one ("- a\n  * b") set '*', so the next list took '-' and joined the outer list.
            _lastMarker = '\0';
            int contentAt = -1;                                                 // where the open outer item's text starts
            foreach (string line in block.Replace("\r\n", "\n").Split('\n', '\r'))
            {
                int indent = 0, k = 0;
                for (; k < line.Length && (line[k] == ' ' || line[k] == '\t'); k++) indent = line[k] == '\t' ? indent + 4 - indent % 4 : indent + 1;
                if (k == line.Length) continue;                                 // blank
                if (contentAt >= 0 && indent >= contentAt) continue;            // inside the open item: nested or continued
                string rest = line.Substring(k);
                char marker = indent <= 3 ? ListMarker(rest) : '\0';
                if (marker != '\0') { _lastMarker = marker; contentAt = indent + ContentOffset(rest); }
            }
            return this;
        }

        // The marker of the block's first line when it is an outer list item, else '\0'.
        private static char FirstItemMarker(string block)
        {
            int end = block.IndexOfAny(new[] { '\r', '\n' });
            string line = end < 0 ? block : block.Substring(0, end);
            // Up to three spaces of indentation still make a list item; four make code.
            int k = 0;
            while (k < line.Length && k < 4 && line[k] == ' ') k++;
            return k < 4 && (k == line.Length || line[k] != '\t') ? ListMarker(line.Substring(k)) : '\0';
        }

        // Where an item's text starts after its marker: past one to four spaces, else (none, or five or more) one column.
        private static int ContentOffset(string item)
        {
            int p = item[0] == '-' || item[0] == '*' || item[0] == '+' ? 1 : 0;
            if (p == 0) { while (item[p] >= '0' && item[p] <= '9') p++; p++; }
            int n = 0;
            while (p + n < item.Length && item[p + n] == ' ') n++;
            return n >= 1 && n <= 4 && p + n < item.Length ? p + n : p + 1;
        }

        // The marker of a list item line ("- x" → '-', "3. x" → '.'), else '\0'.
        private static char ListMarker(string line)
        {
            // An empty item ("-", "1.") is an item too.
            if (line.Length >= 1 && (line[0] == '-' || line[0] == '*' || line[0] == '+') && (line.Length == 1 || line[1] == ' ' || line[1] == '\t')) return line[0];
            int i = 0;
            while (i < line.Length && i < 9 && line[i] >= '0' && line[i] <= '9') i++;
            if (i > 0 && i < line.Length && (line[i] == '.' || line[i] == ')') && (i + 1 == line.Length || line[i + 1] == ' ' || line[i + 1] == '\t')) return line[i];
            return '\0';
        }

        /// <summary>Bold inline span (edge whitespace stays outside the markers, which must touch the text; empty gives "").</summary>
        public static string Bold(string text) => Span(text, "**");
        /// <summary>Italic inline span (edge whitespace stays outside the markers, which must touch the text; empty gives "").</summary>
        public static string Italic(string text) => Span(text, "*");

        private static string Span(string text, string marker)
        {
            // A blank line inside ended the paragraph and left both halves with stray markers; a line starting "- " began a
            // list. A span is inline, so its line breaks become spaces.
            text = OneLine(text);
            string core = text.Trim();
            if (core.Length == 0) return text;   // "****" would be a thematic break
            int lead = text.IndexOf(core, StringComparison.Ordinal);
            return text.Substring(0, lead) + marker + Escape(core) + marker + text.Substring(lead + core.Length);
        }

        private static bool RuleChars(string text)
        {
            int end = text.IndexOf('\n');
            if (end < 0) end = text.Length;
            if (end == 0 || (text[0] != '-' && text[0] != '*' && text[0] != '_')) return false;
            for (int i = 0; i < end; i++)
                if (text[i] != text[0] && text[i] != ' ' && text[i] != '\t') return false;
            return true;
        }

        // When every space-separated token of the first line is a block marker (a bullet, an ordered-list number, a
        // heading's hashes or '>'), escapes the first, so the line is text.
        private static string EscapeMarkerLine(string text)
        {
            int end = text.IndexOf('\n');
            if (end < 0) end = text.Length;
            string[] tokens = text.Substring(0, end).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return text;
            foreach (string t in tokens) if (MarkerEnd(t) < 0) return text;
            int at = MarkerEnd(tokens[0]);
            return text.Substring(0, at) + "\\" + text.Substring(at);
        }

        // Where to put the escaping backslash if t is a block marker (before an ordered number's '.' or ')', else at 0),
        // or -1 when it is not one.
        private static int MarkerEnd(string t)
        {
            if (t == "+" || t == "-" || t == "*" || t == ">") return 0;
            if (t.Length <= 6 && t.Trim('#').Length == 0) return 0;
            int d = 0;
            while (d < t.Length && t[d] >= '0' && t[d] <= '9') d++;
            return d >= 1 && d <= 9 && d == t.Length - 1 && (t[d] == '.' || t[d] == ')') ? d : -1;
        }

        // A multi-line item: continuation lines are indented to the item's content column, so a blank line inside it
        // continues the item rather than ending the list.
        private static string ItemText(string? item, int indent)
        {
            string text = (item ?? "").Trim().Replace("\r\n", "\n").Replace('\r', '\n');
            // A first line made only of '-', '*' or '_' joins the marker into a thematic break ("- --", "* **") or is one
            // itself ("- ***"), or is a nested empty item ("- -"): the item became <hr> and its text was lost.
            if (RuleChars(text)) text = "\\" + text;
            // A first line of block markers alone ("+", "1.", "1)", "#", ">", "+ 2.") opened an empty list, heading or
            // quote, and the text was lost the same way.
            else text = EscapeMarkerLine(text);
            if (text.IndexOf('\n') < 0) return text;
            string pad = new string(' ', indent);
            var lines = text.Split('\n');
            for (int i = 1; i < lines.Length; i++) if (lines[i].Length > 0) lines[i] = pad + lines[i];
            return string.Join("\n", lines);
        }
        /// <summary>Inline code span.</summary>
        public static string Code(string text)
        {
            // Backslash escapes don't work inside a code span: use a longer delimiter, padded when the text touches a
            // backtick (CommonMark strips one space from each side).
            // A blank line inside ended the paragraph, leaving stray backticks.
            text = OneLine(text);
            if (text.Length == 0) return "";   // two backticks are not a code span
            string fence = new string('`', LongestRun(text, '`') + 1);
            bool pad = text.Length > 0 && (text[0] == '`' || text[text.Length - 1] == '`'
                       || (text[0] == ' ' && text[text.Length - 1] == ' ' && text.Trim().Length > 0));
            return pad ? fence + " " + text + " " + fence : fence + text + fence;
        }
        /// <summary>Inline link.</summary>
        public static string Link(string text, string url) => "[" + Escape(OneLine(text)) + "](" + Destination(url ?? "") + ")";

        // Inline text is one line: CR, LF and the Unicode line and paragraph separators become spaces (a blank line in a
        // link's text or an image's alt text ended the paragraph and left literal brackets behind).
        private static string OneLine(string? text)
            => (text ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\u2028', ' ').Replace('\u2029', ' ');

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

        // A link or image destination: as is when CommonMark reads it back unchanged, else in angle brackets, which
        // allow spaces and parentheses (a raw space or an unbalanced ')' would leave the whole link as literal text).
        private static string Destination(string url)
        {
            bool plain = true;
            int depth = 0;
            foreach (char ch in url)
            {
                if (ch <= ' ' || ch == '<' || ch == '>' || ch == '\\' || ch == '\u007f') plain = false;
                else if (ch == '(') depth++;
                else if (ch == ')' && --depth < 0) plain = false;
            }
            // An entity in the URL ("&amp;", "&#38;") was decoded by the renderer, so the link pointed elsewhere.
            if (plain && depth == 0) return EscapeEntities(url);
            var sb = new StringBuilder("<");
            foreach (char ch in url)
            {
                if (ch == '\n') sb.Append("%0A");
                else if (ch == '\r') sb.Append("%0D");
                else if (ch == '<' || ch == '>' || ch == '\\') sb.Append('\\').Append(ch);
                else sb.Append(ch);
            }
            return EscapeEntities(sb.Append('>').ToString());
        }

        // Backslash-escapes each '&' that starts something entity-shaped ("&name;" or "&#...;"); others stay as written.
        private static string EscapeEntities(string url)
        {
            if (url.IndexOf('&') < 0) return url;
            var sb = new StringBuilder(url.Length + 4);
            for (int i = 0; i < url.Length; i++)
            {
                if (url[i] == '&')
                {
                    int j = i + 1;
                    while (j < url.Length && (char.IsLetterOrDigit(url[j]) || url[j] == '#')) j++;
                    if (j > i + 1 && j < url.Length && url[j] == ';') sb.Append('\\');
                }
                sb.Append(url[i]);
            }
            return sb.ToString();
        }

        private static int LongestRun(string text, char ch)
        {
            int best = 0, run = 0;
            foreach (char c in text)
            {
                run = c == ch ? run + 1 : 0;
                if (run > best) best = run;
            }
            return best;
        }

        private void Block(string content)
        {
            _lastMarker = '\0';
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
                    || ch == '(' || ch == ')' || ch == '#' || ch == '!' || ch == '<' || ch == '>' || ch == '&' || ch == '~')
                    sb.Append('\\');
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // Table cells additionally escape pipes and turn every line ending (CRLF, LF or a lone CR) into <br>:
        // a cell is single-line, and a raw CR would end the row.
        private static string EscapeCell(string text)
        {
            return Escape(text).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "<br>").Replace("|", "\\|");
        }
    }
}
