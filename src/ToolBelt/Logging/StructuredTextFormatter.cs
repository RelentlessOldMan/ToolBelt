// ToolBelt drop-in — fully self-contained (BCL only).
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
    /// timestamp format. This gives log files a machine-parseable shape (readable by a delimited/CSV reader)
    /// without abandoning the plain-string event model. Use <see cref="Format"/> as the sink's formatter.
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
            _fields = fields;
            _delimiter = delimiter ?? throw new ArgumentNullException(nameof(delimiter));
            _timestampFormat = timestampFormat ?? throw new ArgumentNullException(nameof(timestampFormat));
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
                sb.Append(Sanitize(FieldValue(e, _fields[i])));
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
                case LogField.Exception: return e.Exception?.ToString() ?? string.Empty;
                default: return string.Empty;
            }
        }

        // Keep each record on one line and keep fields separable: newlines and the delimiter become spaces.
        private string Sanitize(string value)
        {
            value = value.Replace("\r", " ").Replace("\n", " ");
            if (_delimiter.Length > 0 && _delimiter != " ") value = value.Replace(_delimiter, " ");
            return value;
        }
    }
}
