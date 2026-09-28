// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Logging
{
    /// <summary>Severity of a log event, in increasing order.</summary>
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Fatal,
    }

    /// <summary>
    /// A single log record. By deliberate design a log message is a plain string with a level, category and
    /// optional exception — not a structured or templated event. This keeps the stack dependency-free; a
    /// machine-parseable format is a formatter concern, not an event-model concern.
    /// </summary>
    public sealed class LogEvent
    {
        public LogEvent(DateTimeOffset timestamp, LogLevel level, string category, string message, Exception? exception)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category ?? string.Empty;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public DateTimeOffset Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public Exception? Exception { get; }
    }

    /// <summary>
    /// A destination for log events. Implementations must be safe to call from multiple threads (the logger
    /// fans out concurrently). Dispose releases any owned resources; simple sinks may no-op.
    /// </summary>
    public interface ILogSink : IDisposable
    {
        void Emit(LogEvent logEvent);
    }
}
