// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Fixed-width (column-position) record parsing and emission, for legacy instrument and mainframe-style
    /// output where the delimited case does not apply. Fields are laid out in consecutive columns of the
    /// given widths, counted in UTF-16 characters. A column boundary never splits a surrogate pair: <see cref="Format"/>
    /// truncates before the pair, and <see cref="Parse"/> throws <see cref="FormatException"/> for a line whose boundary
    /// falls inside one.
    /// </summary>
    public static class FixedWidth
    {
        /// <summary>Splits a line into fields of the given column widths (trimming surrounding spaces by default).</summary>
        public static string[] Parse(string line, IReadOnlyList<int> widths, bool trim = true)
        {
            if (line is null) throw new ArgumentNullException(nameof(line));
            ValidateWidths(widths);

            var result = new string[widths.Count];
            long pos = 0;   // long: a run of huge widths must not wrap around to a negative position
            for (int i = 0; i < widths.Count; i++)
            {
                int w = widths[i];
                string field = pos < line.Length
                    ? line.Substring((int)pos, (int)Math.Min(w, line.Length - pos))
                    : string.Empty;
                long end = pos + w;
                if (end < line.Length && char.IsLowSurrogate(line[(int)end]) && char.IsHighSurrogate(line[(int)end - 1]))
                    throw new FormatException($"The boundary after column {i} (at {end}) splits a surrogate pair.");
                // Spaces only, as documented: Trim() also took tabs and other whitespace that were part of the data.
                result[i] = trim ? field.Trim(' ') : field;
                pos += w;
            }
            return result;
        }

        /// <summary>Formats fields into fixed columns, padding to width and truncating overflow.</summary>
        public static string Format(IReadOnlyList<string> fields, IReadOnlyList<int> widths, bool leftAlign = true)
        {
            if (fields is null) throw new ArgumentNullException(nameof(fields));
            ValidateWidths(widths);
            if (fields.Count != widths.Count)
                throw new ArgumentException("fields and widths must have the same length.", nameof(widths));

            var sb = new StringBuilder();
            for (int i = 0; i < fields.Count; i++)
            {
                string f = fields[i] ?? string.Empty;
                int w = widths[i];
                // Not through a surrogate pair: half of one is not a character, and broke the text when encoded.
                if (f.Length > w) f = f.Substring(0, char.IsHighSurrogate(f[w - 1]) && char.IsLowSurrogate(f[w]) ? w - 1 : w);
                sb.Append(leftAlign ? f.PadRight(w) : f.PadLeft(w));
            }
            return sb.ToString();
        }

        private static void ValidateWidths(IReadOnlyList<int> widths)
        {
            if (widths is null) throw new ArgumentNullException(nameof(widths));
            for (int i = 0; i < widths.Count; i++)
                if (widths[i] < 1)
                    throw new ArgumentOutOfRangeException(nameof(widths), widths[i], "Each width must be at least 1.");
        }
    }
}
