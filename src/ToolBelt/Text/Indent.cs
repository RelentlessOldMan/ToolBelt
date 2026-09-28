// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Text
{
    /// <summary>
    /// Indents and dedents multi-line text. Lines are split on <c>\n</c> and rejoined with <c>\n</c>
    /// (a trailing <c>\r</c> on each line is preserved). Useful for code generation and for cleaning up
    /// verbatim / heredoc-style string blocks.
    /// </summary>
    public static class Indent
    {
        /// <summary>Prefixes every line with <paramref name="prefix"/>. Blank lines are skipped unless <paramref name="indentBlankLines"/> is set.</summary>
        public static string IndentLines(string text, string prefix, bool indentBlankLines = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (prefix is null) throw new ArgumentNullException(nameof(prefix));

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string content = lines[i].TrimEnd('\r');
                bool blank = content.Length == 0;
                if (!blank || indentBlankLines)
                    lines[i] = prefix + lines[i];
            }
            return string.Join("\n", lines);
        }

        /// <summary>Removes the longest whitespace prefix common to all non-blank lines.</summary>
        public static string Dedent(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));

            string[] lines = text.Split('\n');

            string? common = null;
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Trim().Length == 0)
                    continue; // ignore blank lines when measuring
                string lead = LeadingWhitespace(line);
                common = common is null ? lead : CommonPrefix(common, lead);
                if (common.Length == 0)
                    break;
            }

            if (string.IsNullOrEmpty(common))
                return text;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(common, StringComparison.Ordinal))
                    lines[i] = lines[i].Substring(common!.Length);
            }
            return string.Join("\n", lines);
        }

        private static string LeadingWhitespace(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
                i++;
            return line.Substring(0, i);
        }

        private static string CommonPrefix(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n && a[i] == b[i])
                i++;
            return a.Substring(0, i);
        }
    }
}
