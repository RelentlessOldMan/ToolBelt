// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;

namespace ToolBelt.Logging
{
    /// <summary>A sink that hands each event to a callback — the simplest sink and a handy test probe.</summary>
    public sealed class DelegateSink : ILogSink
    {
        private readonly Action<LogEvent> _onEmit;
        public DelegateSink(Action<LogEvent> onEmit) => _onEmit = onEmit ?? throw new ArgumentNullException(nameof(onEmit));
        public void Emit(LogEvent logEvent) => _onEmit(logEvent);
        public void Dispose() { }
    }

    /// <summary>
    /// Writes formatted lines to a <see cref="TextWriter"/> (a file, a StringWriter in tests, or the console).
    /// Writes are serialized because a TextWriter is not thread-safe. Optionally owns and disposes the writer.
    /// </summary>
    public sealed class TextWriterSink : ILogSink
    {
        private readonly TextWriter _writer;
        private readonly Func<LogEvent, string> _formatter;
        private readonly bool _ownsWriter;
        private readonly object _gate = new object();

        public TextWriterSink(TextWriter writer, Func<LogEvent, string>? formatter = null, bool ownsWriter = false)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _formatter = formatter ?? LogFormatters.Plain;
            _ownsWriter = ownsWriter;
        }

        /// <summary>A sink writing to the process console output.</summary>
        public static TextWriterSink ForConsole(Func<LogEvent, string>? formatter = null)
            => new TextWriterSink(Console.Out, formatter ?? LogFormatters.Compact, ownsWriter: false);

        public void Emit(LogEvent logEvent)
        {
            string line = _formatter(logEvent);
            lock (_gate) _writer.WriteLine(line);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _writer.Flush(); } catch { /* best-effort */ }
                if (_ownsWriter) _writer.Dispose();
            }
        }
    }

    /// <summary>
    /// Keeps the most recent N events in a ring buffer for inclusion in a crash report or diagnostics dump.
    /// <see cref="Snapshot"/> returns them oldest-to-newest.
    /// </summary>
    public sealed class RollingMemorySink : ILogSink
    {
        private readonly int _capacity;
        private readonly Queue<LogEvent> _events;
        private readonly object _gate = new object();

        public RollingMemorySink(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            _capacity = capacity;
            _events = new Queue<LogEvent>(capacity);
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_gate)
            {
                if (_events.Count == _capacity) _events.Dequeue();
                _events.Enqueue(logEvent);
            }
        }

        /// <summary>The buffered events, oldest first.</summary>
        public IReadOnlyList<LogEvent> Snapshot()
        {
            lock (_gate) return new List<LogEvent>(_events);
        }

        public void Dispose() { }
    }
}
