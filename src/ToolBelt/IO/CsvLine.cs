// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Parses and formats a single delimited (CSV) line following RFC 4180 quoting rules: a field may be
    /// wrapped in double quotes, a quote inside a quoted field is written as two quotes, and quoted
    /// fields may contain the delimiter, quotes, and line breaks. <see cref="Format"/> and
    /// <see cref="Parse"/> are inverses.
    /// </summary>
    public static class CsvLine
    {
        /// <summary>Splits one line into fields. An empty string yields a single empty field.</summary>
        public static IReadOnlyList<string> Parse(string line, char delimiter = ',')
        {
            if (line is null)
                throw new ArgumentNullException(nameof(line));

            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            field.Append('"'); // escaped quote
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == delimiter)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '"' && field.Length == 0)
                {
                    inQuotes = true; // a quote only opens a field at its start
                }
                else
                {
                    field.Append(c);
                }
            }

            fields.Add(field.ToString());
            return fields;
        }

        /// <summary>Joins fields into one line, quoting any field that needs it.</summary>
        public static string Format(IEnumerable<string> fields, char delimiter = ',')
        {
            if (fields is null)
                throw new ArgumentNullException(nameof(fields));

            var sb = new StringBuilder();
            bool first = true;
            foreach (string raw in fields)
            {
                if (!first)
                    sb.Append(delimiter);
                first = false;
                sb.Append(QuoteIfNeeded(raw ?? string.Empty, delimiter));
            }
            return sb.ToString();
        }

        private static string QuoteIfNeeded(string field, char delimiter)
        {
            bool mustQuote = field.IndexOf(delimiter) >= 0
                || field.IndexOf('"') >= 0
                || field.IndexOf('\n') >= 0
                || field.IndexOf('\r') >= 0;
            if (!mustQuote)
                return field;

            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
    }
}
