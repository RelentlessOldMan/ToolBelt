// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Text;

namespace ToolBelt.Logging
{
    /// <summary>
    /// Ready-made line formatters (a formatter is just <c>Func&lt;LogEvent, string&gt;</c>). Plain and compact
    /// human-readable forms, plus single-line JSON so log files can be consumed by a JSON-lines reader without
    /// changing the plain-string event model.
    /// </summary>
    public static class LogFormatters
    {
        /// <summary>e.g. <c>2026-09-28T09:15:00.0000000+00:00 [INFO ] Category: message</c> (+ exception on the next line).</summary>
        public static string Plain(LogEvent e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            var sb = new StringBuilder();
            sb.Append(e.Timestamp.ToString("o", CultureInfo.InvariantCulture));
            sb.Append(" [").Append(Level(e.Level)).Append("] ");
            if (e.Category.Length > 0) sb.Append(e.Category).Append(": ");
            sb.Append(e.Message);
            if (e.Exception != null) sb.Append(System.Environment.NewLine).Append(e.Exception);
            return sb.ToString();
        }

        /// <summary>e.g. <c>09:15:00 INFO message</c> — terse, for consoles.</summary>
        public static string Compact(LogEvent e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            string time = e.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return e.Exception is null
                ? $"{time} {Level(e.Level).Trim()} {e.Message}"
                : $"{time} {Level(e.Level).Trim()} {e.Message} | {e.Exception.GetType().Name}: {e.Exception.Message}";
        }

        /// <summary>A single-line JSON object with escaped fields, for machine parsing (JSON-lines).</summary>
        public static string JsonLine(LogEvent e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"timestamp\":\"").Append(e.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append('"');
            sb.Append(",\"level\":\"").Append(e.Level).Append('"');
            sb.Append(",\"category\":"); AppendJsonString(sb, e.Category);
            sb.Append(",\"message\":"); AppendJsonString(sb, e.Message);
            if (e.Exception != null) { sb.Append(",\"exception\":"); AppendJsonString(sb, e.Exception.ToString()); }
            sb.Append('}');
            return sb.ToString();
        }

        private static string Level(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return "TRACE";
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warning: return "WARN ";
                case LogLevel.Error: return "ERROR";
                case LogLevel.Fatal: return "FATAL";
                default: return level.ToString().ToUpperInvariant();
            }
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
