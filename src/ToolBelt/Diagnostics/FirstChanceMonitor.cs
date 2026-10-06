// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace ToolBelt.Diagnostics
{
    /// <summary>One exception seen by a <see cref="FirstChanceMonitor"/>.</summary>
    public sealed class FirstChanceRecord
    {
        internal FirstChanceRecord(Exception exception, DateTimeOffset time, int occurrence, int suppressedSinceLast)
        {
            Exception = exception;
            Time = time;
            Occurrence = occurrence;
            SuppressedSinceLast = suppressedSinceLast;
        }

        public Exception Exception { get; }
        public DateTimeOffset Time { get; }

        /// <summary>How many times this exception type + message has been thrown so far (1 for the first).</summary>
        public int Occurrence { get; }

        /// <summary>Repeats of this signature that were throttled since it was last reported.</summary>
        public int SuppressedSinceLast { get; }

        public override string ToString()
            => $"{Time:HH:mm:ss.fff} first-chance {Exception.GetType().FullName}: {Exception.Message}"
               + (SuppressedSinceLast > 0 ? $" (+{SuppressedSinceLast} similar suppressed)" : "");
    }

    /// <summary>
    /// Reports exceptions the moment they are <em>thrown</em> — including ones that are later caught and swallowed —
    /// via <see cref="AppDomain.FirstChanceException"/>. This is how you find the <c>catch { }</c> that hides the real
    /// failure, or the parser that throws ten thousand times a second. Safe to leave on: it ignores exceptions thrown
    /// from inside its own callback (no recursion), skips ignored types (e.g. <see cref="OperationCanceledException"/>),
    /// and throttles each distinct type+message to one report per <see cref="ThrottleWindow"/>, counting the rest.
    /// Dispose to detach.
    /// </summary>
    public sealed class FirstChanceMonitor : IDisposable
    {
        private readonly Action<FirstChanceRecord> _report;
        private readonly Func<DateTimeOffset> _clock;
        private readonly ConcurrentDictionary<string, Signature> _seen = new ConcurrentDictionary<string, Signature>();

        /// <summary>Distinct type+message signatures tracked before new messages are folded into one per type.</summary>
        public const int MaxSignatures = 1000;
        private readonly HashSet<Type> _ignored = new HashSet<Type>();
        private readonly object _ignoredGate = new object();
        private int _attached;

        [ThreadStatic] private static bool _inCallback;

        private sealed class Signature
        {
            public int Count;
            public int Suppressed;
            public DateTimeOffset LastReported = DateTimeOffset.MinValue;
        }

        /// <param name="report">Called synchronously on the throwing thread — keep it fast and non-blocking.</param>
        /// <param name="attach">Subscribe immediately (default) — pass false to test via <see cref="Observe"/>.</param>
        /// <param name="clock">Time source for throttling (default: UTC now; a clock that steps backwards never mutes reports).</param>
        public FirstChanceMonitor(Action<FirstChanceRecord> report, bool attach = true, Func<DateTimeOffset>? clock = null)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            Ignore<OperationCanceledException>();
            if (attach)
            {
                _attached = 1;
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            }
        }

        /// <summary>Minimum time between reports of the same type+message (default 10 s; zero reports everything).</summary>
        public TimeSpan ThrottleWindow { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Optional extra filter: return false to skip an exception entirely.</summary>
        public Func<Exception, bool>? Filter { get; set; }

        /// <summary>Ignore an exception type and its subclasses. <see cref="OperationCanceledException"/> is ignored by default.</summary>
        public FirstChanceMonitor Ignore<T>() where T : Exception => Ignore(typeof(T));

        public FirstChanceMonitor Ignore(Type exceptionType)
        {
            if (exceptionType is null) throw new ArgumentNullException(nameof(exceptionType));
            if (!typeof(Exception).IsAssignableFrom(exceptionType)) throw new ArgumentException("Not an exception type.", nameof(exceptionType));
            lock (_ignoredGate) _ignored.Add(exceptionType);
            return this;
        }

        /// <summary>Stop ignoring a type (e.g. to see cancellations too).</summary>
        public FirstChanceMonitor Unignore<T>() where T : Exception
        {
            lock (_ignoredGate) _ignored.Remove(typeof(T));
            return this;
        }

        /// <summary>Counts per "Type: message" signature since creation.</summary>
        public IReadOnlyDictionary<string, int> Counts()
        {
            var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in _seen) d[kv.Key] = Volatile.Read(ref kv.Value.Count);
            return d;
        }

        private void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e) => Observe(e.Exception);

        /// <summary>Processes one exception as if it had just been thrown (the event handler calls this).</summary>
        public void Observe(Exception exception)
        {
            if (exception is null || _inCallback) return;
            _inCallback = true;
            try
            {
                if (IsIgnored(exception.GetType())) return;
                if (Filter != null && !Filter(exception)) return;
                string key = exception.GetType().FullName + ": " + exception.Message;
                // Bounded: messages that embed values ("bad value 'x123' at line 4711") would otherwise add a signature per
                // throw for the life of the process. Past the cap, new messages share one per-type signature.
                if (_seen.Count >= MaxSignatures && !_seen.ContainsKey(key)) key = exception.GetType().FullName + ": (other messages)";
                Signature sig = _seen.GetOrAdd(key, _ => new Signature());
                DateTimeOffset now = _clock();
                int occurrence, suppressed;
                lock (sig)
                {
                    occurrence = ++sig.Count;
                    if (sig.LastReported != DateTimeOffset.MinValue && now >= sig.LastReported && now - sig.LastReported < ThrottleWindow)
                    {
                        sig.Suppressed++;
                        return;
                    }
                    suppressed = sig.Suppressed;
                    sig.Suppressed = 0;
                    sig.LastReported = now;
                }
                _report(new FirstChanceRecord(exception, now, occurrence, suppressed));
            }
            catch
            {
                // A failing reporter must never turn every throw in the process into a second failure.
            }
            finally
            {
                _inCallback = false;
            }
        }

        private bool IsIgnored(Type t)
        {
            lock (_ignoredGate)
            {
                foreach (Type ignored in _ignored)
                    if (ignored.IsAssignableFrom(t)) return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _attached, 0) == 1)
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        }
    }
}
