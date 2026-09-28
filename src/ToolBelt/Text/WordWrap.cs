// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Text
{
    /// <summary>
    /// Greedy word wrapping. Splits text on whitespace and packs as many words as fit into each line of
    /// at most <c>width</c> columns; a single word longer than the width is hard-broken across lines.
    /// Interior whitespace runs (including newlines) collapse to single spaces — the input is treated as
    /// one reflowable block. Output lines carry no leading or trailing spaces.
    /// </summary>
    /// <remarks>Width is measured in UTF-16 code units, so a hard-break of an over-long word can fall
    /// between the halves of a surrogate pair.</remarks>
    public static class WordWrap
    {
        /// <summary>Wraps <paramref name="text"/> to <paramref name="width"/> columns, returning the lines.</summary>
        public static IReadOnlyList<string> Wrap(string text, int width)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");

            var lines = new List<string>();
            var line = new System.Text.StringBuilder();

            foreach (string word in SplitWords(text))
            {
                string remaining = word;

                // Hard-break any word that cannot fit on a line by itself.
                while (remaining.Length > width)
                {
                    if (line.Length > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    lines.Add(remaining.Substring(0, width));
                    remaining = remaining.Substring(width);
                }

                if (remaining.Length == 0)
                    continue;

                int projected = line.Length == 0 ? remaining.Length : line.Length + 1 + remaining.Length;
                if (projected > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0)
                    line.Append(' ');
                line.Append(remaining);
            }

            if (line.Length > 0)
                lines.Add(line.ToString());

            return lines;
        }

        /// <summary>Wraps and joins the lines with <paramref name="newline"/> (default "\n").</summary>
        public static string WrapToString(string text, int width, string newline = "\n")
            => string.Join(newline, Wrap(text, width));

        private static IEnumerable<string> SplitWords(string text)
        {
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    if (start >= 0)
                    {
                        yield return text.Substring(start, i - start);
                        start = -1;
                    }
                }
                else if (start < 0)
                {
                    start = i;
                }
            }

            if (start >= 0)
                yield return text.Substring(start);
        }
    }
}
