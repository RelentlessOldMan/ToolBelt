// ToolBelt drop-in — also copy Logging/Log.cs and Logging/ScopedContext.cs.
using System;
using System.Collections.Generic;

namespace ToolBelt.Logging
{
    /// <summary>
    /// A thread-safe fan-out logger: it timestamps each message (through an injectable clock, so tests are
    /// deterministic), filters by minimum level, tags with a category, and forwards to every registered
    /// <see cref="ILogSink"/>. A sink that throws is isolated — it never breaks logging or the other sinks — and a clock
    /// that throws gives way to the system clock (<see cref="DateTimeOffset.Now"/>, the default) for that event.
    /// </summary>
    public sealed class Logger : IDisposable
    {
        private readonly List<ILogSink> _sinks = new List<ILogSink>();
        // Shared with every ForCategory child; holds whether the owning logger is disposed.
        private readonly Gate _gate = new Gate();
        private readonly Func<DateTimeOffset> _clock;
        private readonly bool _ownsSinks;

        public Logger(string category = "", LogLevel minimumLevel = LogLevel.Info, Func<DateTimeOffset>? clock = null)
        {
            Category = category ?? string.Empty;
            MinimumLevel = CheckLevel(minimumLevel, nameof(minimumLevel));
            _clock = clock ?? (() => DateTimeOffset.Now);
            _ownsSinks = true;
        }

        private sealed class Gate { public bool Disposed; }

        private Logger(string category, LogLevel minimumLevel, Func<DateTimeOffset> clock, List<ILogSink> sharedSinks, Gate gate)
        {
            Category = category;
            MinimumLevel = minimumLevel;
            _clock = clock;
            _sinks = sharedSinks;
            _gate = gate;
            _ownsSinks = false; // a child shares the parent's sinks and must not dispose them
        }

        public string Category { get; }

        /// <summary>Events below this level are ignored. Must be a defined <see cref="LogLevel"/>.</summary>
        // An undefined level (99) was accepted and silently dropped every event, Fatal included.
        public LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = CheckLevel(value, nameof(value));
        }

        private LogLevel _minimumLevel;

        private static LogLevel CheckLevel(LogLevel level, string paramName)
            => level >= LogLevel.Trace && level <= LogLevel.Fatal ? level
             : throw new ArgumentOutOfRangeException(paramName, level, "Level must be a defined LogLevel.");

        /// <summary>
        /// Registers a sink. Returns this for chaining. Throws <see cref="ObjectDisposedException"/> once the logger (or,
        /// for a <see cref="ForCategory"/> logger, the one it came from) is disposed; the sink stays the caller's.
        /// </summary>
        public Logger AddSink(ILogSink sink)
        {
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            lock (_gate)
            {
                // A sink added after Dispose was kept and never disposed (a FileSink's handle leaked).
                if (_gate.Disposed) throw new ObjectDisposedException(nameof(Logger));
                _sinks.Add(sink);
            }
            return this;
        }

        /// <summary>A logger for a different category that shares this logger's sinks and clock.</summary>
        public Logger ForCategory(string category)
        {
            if (category is null) throw new ArgumentNullException(nameof(category));
            return new Logger(category, MinimumLevel, _clock, _sinks, _gate);
        }

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            // An undefined level (99) went to every sink, past any minimum, and was written as "[99]".
            CheckLevel(level, nameof(level));
            if (level < MinimumLevel) return;
            // A clock that threw sent its exception to the caller of Info (logging sits in catch and finally blocks).
            DateTimeOffset timestamp;
            try { timestamp = _clock(); }
            catch { timestamp = DateTimeOffset.Now; }
            var logEvent = new LogEvent(timestamp, level, EffectiveCategory(), message ?? string.Empty, exception);

            ILogSink[] snapshot;
            lock (_gate) snapshot = _sinks.ToArray();
            foreach (ILogSink sink in snapshot)
            {
                try { sink.Emit(logEvent); }
                catch { /* a failing sink must not break logging or the remaining sinks */ }
            }
        }

        // Combines the logger's category with any ambient scope pushed via ScopedContext.
        private string EffectiveCategory()
        {
            string? scope = ScopedContext.Current;
            if (string.IsNullOrEmpty(scope)) return Category;
            return Category.Length == 0 ? scope! : Category + "/" + scope;
        }

        public void Trace(string message) => Log(LogLevel.Trace, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message, Exception? exception = null) => Log(LogLevel.Warning, message, exception);
        public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
        public void Fatal(string message, Exception? exception = null) => Log(LogLevel.Fatal, message, exception);

        public void Dispose()
        {
            if (!_ownsSinks) return;
            ILogSink[] snapshot;
            lock (_gate)
            {
                if (_gate.Disposed) return;
                _gate.Disposed = true;
                snapshot = _sinks.ToArray();
                _sinks.Clear();
            }
            foreach (ILogSink sink in snapshot)
            {
                try { sink.Dispose(); } catch { /* best-effort */ }
            }
        }
    }
}
