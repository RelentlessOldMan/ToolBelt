// ToolBelt drop-in — also copy Logging/Log.cs.
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

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
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));   // every sink does the same
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
    /// is not wanted. A route whose predicate or sink throws does not stop the others: every route is tried, then the
    /// failure is rethrown (an <see cref="AggregateException"/> if several failed).
    /// </summary>
    public sealed class RouterSink : ILogSink
    {
        // Copy-on-write: a route added while an event was being routed threw "Collection was modified".
        private (Func<LogEvent, bool> Predicate, ILogSink Sink)[] _routes = new (Func<LogEvent, bool>, ILogSink)[0];
        private readonly object _gate = new object();
        private readonly bool _ownsSinks;

        public RouterSink(bool ownsSinks = false) => _ownsSinks = ownsSinks;

        public RouterSink AddRoute(Func<LogEvent, bool> predicate, ILogSink sink)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            lock (_gate)
            {
                var routes = new (Func<LogEvent, bool>, ILogSink)[_routes.Length + 1];
                Array.Copy(_routes, routes, _routes.Length);
                routes[_routes.Length] = (predicate, sink);
                System.Threading.Volatile.Write(ref _routes, routes);
            }
            return this;
        }

        /// <summary>Routes events at or above <paramref name="minimum"/> to <paramref name="sink"/>.</summary>
        public RouterSink AddLevelRoute(LogLevel minimum, ILogSink sink) => AddRoute(e => e.Level >= minimum, sink);

        public void Emit(LogEvent logEvent)
        {
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));   // every sink does the same
            List<Exception>? errors = null;
            foreach (var route in System.Threading.Volatile.Read(ref _routes))
            {
                try
                {
                    if (route.Predicate(logEvent))
                        route.Sink.Emit(logEvent);
                }
                catch (Exception ex)
                {
                    (errors ??= new List<Exception>()).Add(ex);
                }
            }
            if (errors == null) return;
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException(errors);
        }

        public void Dispose()
        {
            if (!_ownsSinks) return;
            foreach (var route in System.Threading.Volatile.Read(ref _routes))
            {
                try { route.Sink.Dispose(); } catch { /* best-effort */ }
            }
        }
    }

    /// <summary>
    /// Suppresses a flood of the identical message (same level, category and text), forwarding the first occurrence
    /// and a "(repeated N times)" summary for what it swallowed. The summary is written when the next event arrives
    /// after the window has elapsed or with a different message, or on <see cref="Dispose"/>; there is no timer, so a
    /// flood that stops with nothing logged after it is summarized only when the sink is disposed. A
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

        private bool _hasCurrent;
        private LogLevel _currentLevel;
        private string _currentCategory = string.Empty;
        private string _currentMessage = string.Empty;
        private DateTimeOffset _windowStart;
        private long _suppressed;   // an int wrapped negative after 2^31 repeats and the summary vanished
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
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));   // every sink does the same
            DateTimeOffset now = _clock();
            LogEvent? summary;
            // The inner sink is called outside the lock: a blocking AsyncLogSink inside, whose consumer logged back
            // through this sink, deadlocked (the consumer waited for the lock, the lock's holder for queue space).
            lock (_gate)
            {
                bool same = _hasCurrent && logEvent.Level == _currentLevel
                    && string.Equals(logEvent.Category, _currentCategory, StringComparison.Ordinal)
                    && string.Equals(logEvent.Message, _currentMessage, StringComparison.Ordinal);
                // now >= _windowStart: a wall clock stepped back (NTP, a VM resume) muted the message until it passed the old time.
                if (same && now >= _windowStart && now - _windowStart < _window)
                {
                    _suppressed++;
                    _lastSuppressed = logEvent;
                    return;
                }
                summary = TakeSummary();
                // The window starts before the write: an inner sink that threw left it unstarted, so a flood of a message
                // that failed to write was never suppressed.
                _hasCurrent = true;
                _currentLevel = logEvent.Level;
                _currentCategory = logEvent.Category;
                _currentMessage = logEvent.Message;
                _windowStart = now;
                _suppressed = 0;
                _lastSuppressed = null;
            }
            Exception? summaryError = null;
            if (summary != null)
            {
                try { _inner.Emit(summary); }
                catch (Exception ex) { summaryError = ex; }                         // still write the new event
            }
            try { _inner.Emit(logEvent); }
            catch (Exception ex) when (summaryError != null) { throw new AggregateException(summaryError, ex); }   // both, not just one
            if (summaryError != null) ExceptionDispatchInfo.Capture(summaryError).Throw();
        }

        private LogEvent? TakeSummary()
        {
            long suppressed = _suppressed;
            LogEvent? last = _lastSuppressed;
            _suppressed = 0;
            _lastSuppressed = null;
            if (suppressed > 0 && last != null)
            {
                // Stamped with the last repeat, not the flush, which can come hours later.
                return new LogEvent(last.Timestamp, last.Level, last.Category,
                    last.Message + $" (repeated {suppressed} times)", last.Exception);
            }
            return null;
        }

        public void Dispose()
        {
            LogEvent? summary;
            lock (_gate) summary = TakeSummary();
            try { if (summary != null) _inner.Emit(summary); }
            finally { if (_ownsInner) _inner.Dispose(); } // a throwing summary left the owned sink open
        }
    }
}
