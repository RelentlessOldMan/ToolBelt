// ToolBelt drop-in — also copy Logging/Log.cs and Logging/LogFormatters.cs.
using System;
using System.Collections.Generic;
using System.IO;

namespace ToolBelt.Logging
{
    /// <summary>A sink that hands each event to a callback — the simplest sink and a handy test probe.</summary>
    public sealed class DelegateSink : ILogSink
    {
        private readonly Action<LogEvent> _onEmit;
        private volatile bool _disposed;
        public DelegateSink(Action<LogEvent> onEmit) => _onEmit = onEmit ?? throw new ArgumentNullException(nameof(onEmit));

        public void Emit(LogEvent logEvent)
        {
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));
            // Every other sink refuses an event after Dispose; this one kept calling back.
            if (_disposed) throw new ObjectDisposedException(nameof(DelegateSink));
            _onEmit(logEvent);
        }

        public void Dispose() => _disposed = true;
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
        private bool _disposed;

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
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));   // every sink does the same
            string line = _formatter(logEvent);
            lock (_gate)
            {
                // A writer the sink doesn't own (the console) was still written after Dispose.
                if (_disposed) throw new ObjectDisposedException(nameof(TextWriterSink));
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try { _writer.Flush(); } catch { /* best-effort */ }
                // Disposing flushes again: a writer whose flush failed threw out of Dispose.
                if (_ownsWriter) { try { _writer.Dispose(); } catch { /* best-effort */ } }
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
        private bool _disposed;

        public RollingMemorySink(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            _capacity = capacity;
            _events = new Queue<LogEvent>(Math.Min(capacity, 1024)); // grows as needed: the full capacity was allocated up front
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));   // every sink does the same
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RollingMemorySink));   // was kept after Dispose
                if (_events.Count == _capacity) _events.Dequeue();
                _events.Enqueue(logEvent);
            }
        }

        /// <summary>The buffered events, oldest first (still readable after Dispose).</summary>
        public IReadOnlyList<LogEvent> Snapshot()
        {
            lock (_gate) return new List<LogEvent>(_events);
        }

        public void Dispose()
        {
            lock (_gate) _disposed = true;
        }
    }
}
