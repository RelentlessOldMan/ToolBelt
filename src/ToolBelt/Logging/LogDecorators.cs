// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Logging
{
    /// <summary>Wraps a sink with a predicate — only matching events pass through. A composable decorator.</summary>
    public sealed class FilterSink : ILogSink
    {
        private readonly ILogSink _inner;
        private readonly Func<LogEvent, bool> _predicate;
        private readonly bool _ownsInner;

        public FilterSink(ILogSink inner, Func<LogEvent, bool> predicate, bool ownsInner = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _ownsInner = ownsInner;
        }

        /// <summary>A filter that only lets through events at or above <paramref name="minimum"/>.</summary>
        public static FilterSink MinimumLevel(ILogSink inner, LogLevel minimum, bool ownsInner = false)
            => new FilterSink(inner, e => e.Level >= minimum, ownsInner);

        public void Emit(LogEvent logEvent)
        {
            if (_predicate(logEvent)) _inner.Emit(logEvent);
        }

        public void Dispose()
        {
            if (_ownsInner) _inner.Dispose();
        }
    }

    /// <summary>
    /// Routes each event to every sink whose predicate matches (e.g. errors to one file, everything to
    /// another). An event matching no route is dropped; add a catch-all route (<c>_ =&gt; true</c>) if that
    /// is not wanted.
    /// </summary>
    public sealed class RouterSink : ILogSink
    {
        private readonly List<(Func<LogEvent, bool> Predicate, ILogSink Sink)> _routes = new List<(Func<LogEvent, bool>, ILogSink)>();
        private readonly bool _ownsSinks;

        public RouterSink(bool ownsSinks = false) => _ownsSinks = ownsSinks;

        public RouterSink AddRoute(Func<LogEvent, bool> predicate, ILogSink sink)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            _routes.Add((predicate, sink));
            return this;
        }

        /// <summary>Routes events at or above <paramref name="minimum"/> to <paramref name="sink"/>.</summary>
        public RouterSink AddLevelRoute(LogLevel minimum, ILogSink sink) => AddRoute(e => e.Level >= minimum, sink);

        public void Emit(LogEvent logEvent)
        {
            foreach (var route in _routes)
                if (route.Predicate(logEvent))
                    route.Sink.Emit(logEvent);
        }

        public void Dispose()
        {
            if (!_ownsSinks) return;
            foreach (var route in _routes)
            {
                try { route.Sink.Dispose(); } catch { /* best-effort */ }
            }
        }
    }

    /// <summary>
    /// Suppresses a flood of the identical message, forwarding the first occurrence and, once the window has
    /// elapsed (or a different message arrives), a "(repeated N times)" summary for what it swallowed. A
    /// single repeating error can no longer fill a disk. Uses an injectable clock and tracks only the current
    /// run of identical messages, so its memory is bounded.
    /// </summary>
    public sealed class RateLimitedSink : ILogSink
    {
        private readonly ILogSink _inner;
        private readonly TimeSpan _window;
        private readonly Func<DateTimeOffset> _clock;
        private readonly bool _ownsInner;
        private readonly object _gate = new object();

        private string? _currentKey;
        private DateTimeOffset _windowStart;
        private int _suppressed;
        private LogEvent? _lastSuppressed;

        public RateLimitedSink(ILogSink inner, TimeSpan window, Func<DateTimeOffset>? clock = null, bool ownsInner = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window), window, "Window must be positive.");
            _window = window;
            _clock = clock ?? (() => DateTimeOffset.Now);
            _ownsInner = ownsInner;
        }

        public void Emit(LogEvent logEvent)
        {
            string key = logEvent.Level + "|" + logEvent.Category + "|" + logEvent.Message;
            DateTimeOffset now = _clock();
            lock (_gate)
            {
                if (key == _currentKey && now - _windowStart < _window)
                {
                    _suppressed++;
                    _lastSuppressed = logEvent;
                    return;
                }
                FlushSummary(now);
                _inner.Emit(logEvent);
                _currentKey = key;
                _windowStart = now;
                _suppressed = 0;
                _lastSuppressed = null;
            }
        }

        private void FlushSummary(DateTimeOffset now)
        {
            if (_suppressed > 0 && _lastSuppressed != null)
            {
                var summary = new LogEvent(now, _lastSuppressed.Level, _lastSuppressed.Category,
                    _lastSuppressed.Message + $" (repeated {_suppressed} times)", _lastSuppressed.Exception);
                _inner.Emit(summary);
            }
            _suppressed = 0;
            _lastSuppressed = null;
        }

        public void Dispose()
        {
            lock (_gate) FlushSummary(_clock());
            if (_ownsInner) _inner.Dispose();
        }
    }
}
