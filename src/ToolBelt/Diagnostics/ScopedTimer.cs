// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Diagnostics
{
    /// <summary>
    /// Times a named block and reports the elapsed duration to a callback when disposed — pair it with a
    /// <c>using</c>. Nest instances to time a call tree. The clock is injectable, so tests are deterministic
    /// (no sleeping); by default it uses wall-clock time.
    /// </summary>
    public sealed class ScopedTimer : IDisposable
    {
        private readonly string _name;
        private readonly Action<string, TimeSpan> _onComplete;
        private readonly Func<DateTimeOffset> _clock;
        private readonly DateTimeOffset _start;
        private bool _disposed;

        public ScopedTimer(string name, Action<string, TimeSpan> onComplete, Func<DateTimeOffset>? clock = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _start = _clock();
        }

        /// <summary>Convenience: time a block into a <see cref="MetricsRegistry"/> timer (milliseconds).</summary>
        public static ScopedTimer ToMetrics(string name, MetricsRegistry registry, Func<DateTimeOffset>? clock = null)
        {
            if (registry is null) throw new ArgumentNullException(nameof(registry));
            return new ScopedTimer(name, (n, elapsed) => registry.Record(n, elapsed.TotalMilliseconds), clock);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _onComplete(_name, _clock() - _start);
        }
    }
}
