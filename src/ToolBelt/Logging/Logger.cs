// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Logging
{
    /// <summary>
    /// A thread-safe fan-out logger: it timestamps each message (through an injectable clock, so tests are
    /// deterministic), filters by minimum level, tags with a category, and forwards to every registered
    /// <see cref="ILogSink"/>. A sink that throws is isolated — it never breaks logging or the other sinks.
    /// </summary>
    public sealed class Logger : IDisposable
    {
        private readonly List<ILogSink> _sinks = new List<ILogSink>();
        private readonly object _gate = new object();
        private readonly Func<DateTimeOffset> _clock;
        private readonly bool _ownsSinks;

        public Logger(string category = "", LogLevel minimumLevel = LogLevel.Info, Func<DateTimeOffset>? clock = null)
        {
            Category = category ?? string.Empty;
            MinimumLevel = minimumLevel;
            _clock = clock ?? (() => DateTimeOffset.Now);
            _ownsSinks = true;
        }

        private Logger(string category, LogLevel minimumLevel, Func<DateTimeOffset> clock, List<ILogSink> sharedSinks, object gate)
        {
            Category = category;
            MinimumLevel = minimumLevel;
            _clock = clock;
            _sinks = sharedSinks;
            _gate = gate;
            _ownsSinks = false; // a child shares the parent's sinks and must not dispose them
        }

        public string Category { get; }

        /// <summary>Events below this level are ignored.</summary>
        public LogLevel MinimumLevel { get; set; }

        /// <summary>Registers a sink. Returns this for chaining.</summary>
        public Logger AddSink(ILogSink sink)
        {
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            lock (_gate) _sinks.Add(sink);
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
            if (level < MinimumLevel) return;
            var logEvent = new LogEvent(_clock(), level, EffectiveCategory(), message ?? string.Empty, exception);

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
            lock (_gate) { snapshot = _sinks.ToArray(); _sinks.Clear(); }
            foreach (ILogSink sink in snapshot)
            {
                try { sink.Dispose(); } catch { /* best-effort */ }
            }
        }
    }
}
