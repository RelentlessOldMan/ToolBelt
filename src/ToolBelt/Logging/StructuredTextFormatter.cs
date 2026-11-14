// ToolBelt drop-in — also copy Logging/Log.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Logging
{
    /// <summary>A field of a log event, for building a structured line format.</summary>
    public enum LogField
    {
        Timestamp,
        Level,
        Category,
        Message,
        Exception,
    }

    /// <summary>
    /// A configurable line formatter: choose which fields appear, in what order, with which delimiter and
    /// timestamp format. This gives log files a machine-parseable shape (split each line on the delimiter) without
    /// abandoning the plain-string event model. Use <see cref="Format"/> as the sink's formatter. Newlines and the
    /// delimiter inside a value become spaces; nothing else is escaped, so a quoting CSV reader may misread a value
    /// that contains a <c>"</c>, and with the default space delimiter a value containing spaces is not separable —
    /// put free text such as the message last, or pick a delimiter like a tab.
    /// </summary>
    public sealed class StructuredTextFormatter
    {
        private readonly IReadOnlyList<LogField> _fields;
        private readonly string _delimiter;
        private readonly string _timestampFormat;

        public StructuredTextFormatter(IReadOnlyList<LogField> fields, string delimiter = " ", string timestampFormat = "o")
        {
            if (fields is null) throw new ArgumentNullException(nameof(fields));
            if (fields.Count == 0) throw new ArgumentException("At least one field is required.", nameof(fields));
            _fields = new List<LogField>(fields); // a copy: clearing the caller's list later emptied every line
            _delimiter = delimiter ?? throw new ArgumentNullException(nameof(delimiter));
            _timestampFormat = timestampFormat ?? throw new ArgumentNullException(nameof(timestampFormat));
            try { new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString(timestampFormat, CultureInfo.InvariantCulture); }
            catch (FormatException ex) { throw new ArgumentException("Invalid timestamp format: " + ex.Message, nameof(timestampFormat), ex); }
        }

        /// <summary>A tab-delimited timestamp/level/category/message formatter (a delimited preset).</summary>
        public static StructuredTextFormatter Delimited(string delimiter = "\t")
            => new StructuredTextFormatter(new[] { LogField.Timestamp, LogField.Level, LogField.Category, LogField.Message }, delimiter);

        /// <summary>Formats an event into a single line. Delimiters inside a field value are replaced with a space.</summary>
        public string Format(LogEvent e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            var sb = new StringBuilder();
            for (int i = 0; i < _fields.Count; i++)
            {
                if (i > 0) sb.Append(_delimiter);
                sb.Append(Sanitize(FieldValue(e, _fields[i]), last: i == _fields.Count - 1));
            }
            return sb.ToString();
        }

        private string FieldValue(LogEvent e, LogField field)
        {
            switch (field)
            {
                case LogField.Timestamp: return e.Timestamp.ToString(_timestampFormat, CultureInfo.InvariantCulture);
                case LogField.Level: return e.Level.ToString();
                case LogField.Category: return e.Category;
                case LogField.Message: return e.Message;
                case LogField.Exception: return e.Exception == null ? string.Empty : Describe(e.Exception);
                default: return string.Empty;
            }
        }

        // Keep each record on one line and keep fields separable: newlines and the delimiter become spaces.
        // An exception whose ToString throws (a broken override) took the log call down.
        private static string Describe(Exception ex)
        {
            try { return ex.ToString(); }
            catch (Exception inner) { return ex.GetType().FullName + " (its ToString threw " + inner.GetType().FullName + ")"; }
        }

        private string Sanitize(string value, bool last)
        {
            // The Unicode line breaks too (NEL, LS, PS): a viewer that honours them split the record.
            value = value.Replace("\r", " ").Replace("\n", " ").Replace("\u0085", " ").Replace("\u2028", " ").Replace("\u2029", " ");
            // Until none is left: one pass of Replace can make a new delimiter when the delimiter has a space in it
            // (" | | " became " |  | ", and ",, " became ", , " for ", "). Repeating Replace took quadratic time (a
            // 100,000-character value took 3 s), so one pass keeps the output free of the delimiter as it goes: a
            // delimiter can only appear at its end, and becomes a space there, which is checked again.
            if (_delimiter.Length > 0 && _delimiter != " " && value.IndexOf(_delimiter, StringComparison.Ordinal) >= 0)
                value = _delimiter.Length == 1 ? value.Replace(_delimiter, " ") : RemoveDelimiters(value);
            if (!last) value = TrimOverlap(value);
            return value;
        }

        private string RemoveDelimiters(string value)
        {
            int n = _delimiter.Length;
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(c);
                while (EndsWithDelimiter(sb))
                {
                    sb.Length -= n;
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }

        private bool EndsWithDelimiter(StringBuilder sb)
        {
            int n = _delimiter.Length, start = sb.Length - n;
            if (start < 0) return false;
            for (int i = 0; i < n; i++)
                if (sb[start + i] != _delimiter[i]) return false;
            return true;
        }

        // A value whose end runs into the delimiter that follows it ("x-" then "--", "a |" then " | ") made a delimiter
        // inside the value, and the line split one place early. Such a tail becomes spaces.
        private string TrimOverlap(string value)
        {
            int n = _delimiter.Length;
            if (n < 2 || value.Length == 0) return value;
            var sb = new StringBuilder(value);
            bool changed = true;
            while (changed && sb.Length > 0)
            {
                changed = false;
                for (int k = Math.Min(n - 1, sb.Length); k >= 1; k--)
                {
                    if (!TailRunsIntoDelimiter(sb, k)) continue;
                    // Each step blanks or drops a character, so it ends (a space dropped: the delimiter starts with one).
                    if (sb[sb.Length - 1] != ' ') sb[sb.Length - 1] = ' ';
                    else sb.Length--;
                    changed = true;
                    break;
                }
            }
            return sb.ToString();
        }

        // The last k characters plus the start of the delimiter form a whole delimiter.
        private bool TailRunsIntoDelimiter(StringBuilder sb, int k)
        {
            int n = _delimiter.Length;
            for (int i = 0; i < k; i++)
                if (sb[sb.Length - k + i] != _delimiter[i]) return false;
            return string.CompareOrdinal(_delimiter, k, _delimiter, 0, n - k) == 0;
        }
    }
}
