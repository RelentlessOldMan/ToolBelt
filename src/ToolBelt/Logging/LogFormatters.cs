// ToolBelt drop-in — also copy Logging/Log.cs.
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
            if (e.Exception != null) sb.Append(System.Environment.NewLine).Append(Describe(e.Exception));
            return sb.ToString();
        }

        // An exception whose ToString throws (a broken override) took the log call down. ToString recurses once per
        // level of inner exceptions and copies the inner text at each: a chain 20,000 deep overflowed the stack (which
        // no catch stops) and one 10,000 deep took 12 s, and a cycle made by reflection overflowed too. Up to 64 levels
        // (and 10,000 exceptions) keep the runtime's own text; past that, each of the first 256 exceptions gets its type,
        // message and stack, walked without recursion.
        private static string Describe(Exception ex)
        {
            var walk = new System.Collections.Generic.Stack<(Exception Exception, int Depth)>();
            walk.Push((ex, 0));
            bool deep = false;
            for (int visits = 0; walk.Count > 0 && !deep; visits++)
            {
                var (e, depth) = walk.Pop();
                if (depth > 64 || visits == 10_000) deep = true;
                else if (e is AggregateException agg) foreach (Exception inner in agg.InnerExceptions) walk.Push((inner, depth + 1));
                else if (e.InnerException != null) walk.Push((e.InnerException, depth + 1));
            }
            if (!deep)
            {
                try { return ex.ToString(); }
                catch (Exception inner) { return ex.GetType().FullName + " (its ToString threw " + inner.GetType().FullName + ")"; }
            }
            var seen = new System.Collections.Generic.List<Exception>();
            var todo = new System.Collections.Generic.Stack<Exception>();
            todo.Push(ex);
            bool again = false, more = false;
            while (todo.Count > 0)
            {
                Exception e = todo.Pop();
                if (seen.Exists(s => ReferenceEquals(s, e))) { again = true; continue; }
                if (seen.Count == 256) { more = true; break; }
                seen.Add(e);
                if (e is AggregateException agg)
                {
                    var inner = agg.InnerExceptions;
                    for (int i = inner.Count - 1; i >= 0; i--) todo.Push(inner[i]);
                }
                else if (e.InnerException != null) todo.Push(e.InnerException);
            }
            var sb = new StringBuilder();
            for (int i = 0; i < seen.Count; i++)
            {
                Exception e = seen[i];
                if (i > 0) sb.Append(System.Environment.NewLine).Append(" ---> ");
                sb.Append(e.GetType().FullName).Append(": ");
                try { sb.Append(e.Message); }
                catch (Exception inner) { sb.Append("(its Message threw ").Append(inner.GetType().FullName).Append(')'); }
                string? trace;
                try { trace = e.StackTrace; }
                catch (Exception) { trace = null; }
                if (!string.IsNullOrEmpty(trace)) sb.Append(System.Environment.NewLine).Append(trace);
            }
            if (again) sb.Append(System.Environment.NewLine).Append(" ---> (an exception shown above appears again)");
            if (more) sb.Append(System.Environment.NewLine).Append(" ---> (more inner exceptions not shown)");
            return sb.ToString();
        }

        /// <summary>e.g. <c>09:15:00 INFO message</c> — terse, for consoles.</summary>
        public static string Compact(LogEvent e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            string time = e.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return e.Exception is null
                ? $"{time} {Level(e.Level).Trim()} {e.Message}"
                : $"{time} {Level(e.Level).Trim()} {e.Message} | {e.Exception.GetType().Name}: {MessageOf(e.Exception)}";
        }

        // An exception whose Message getter throws made Compact throw, and the console lost the event.
        private static string MessageOf(Exception ex)
        {
            try { return ex.Message; }
            catch (Exception inner) { return "(its Message threw " + inner.GetType().FullName + ")"; }
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
            if (e.Exception != null) { sb.Append(",\"exception\":"); AppendJsonString(sb, Describe(e.Exception)); }
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
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                // An unpaired surrogate is escaped: written raw, a UTF-8 sink replaced it with U+FFFD and lost it.
                if (char.IsSurrogate(c))
                {
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { sb.Append(c).Append(value[++i]); continue; }
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    continue;
                }
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
