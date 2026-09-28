// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Fixed-width (column-position) record parsing and emission, for legacy instrument and mainframe-style
    /// output where the delimited case does not apply. Fields are laid out in consecutive columns of the
    /// given widths.
    /// </summary>
    public static class FixedWidth
    {
        /// <summary>Splits a line into fields of the given column widths (trimming surrounding spaces by default).</summary>
        public static string[] Parse(string line, IReadOnlyList<int> widths, bool trim = true)
        {
            if (line is null) throw new ArgumentNullException(nameof(line));
            ValidateWidths(widths);

            var result = new string[widths.Count];
            int pos = 0;
            for (int i = 0; i < widths.Count; i++)
            {
                int w = widths[i];
                string field = pos < line.Length
                    ? line.Substring(pos, Math.Min(w, line.Length - pos))
                    : string.Empty;
                result[i] = trim ? field.Trim() : field;
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
                throw new ArgumentException("fields and widths must have the same length.");

            var sb = new StringBuilder();
            for (int i = 0; i < fields.Count; i++)
            {
                string f = fields[i] ?? string.Empty;
                int w = widths[i];
                if (f.Length > w) f = f.Substring(0, w);
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
