// ToolBelt drop-in — also copy Diagnostics/MetricsRegistry.cs (for ToMetrics).
using System;

namespace ToolBelt.Diagnostics
{
    /// <summary>
    /// Times a named block and reports the elapsed duration to a callback when disposed — pair it with a
    /// <c>using</c>. Nest instances to time a call tree. The clock is injectable, so tests are deterministic
    /// (no sleeping); by default it uses <see cref="System.Diagnostics.Stopwatch"/>, which a clock change doesn't move.
    /// </summary>
    public sealed class ScopedTimer : IDisposable
    {
        private readonly string _name;
        private readonly Action<string, TimeSpan> _onComplete;
        private readonly Func<DateTimeOffset>? _clock;
        private readonly DateTimeOffset _start;
        private readonly long _startTicks;
        private bool _disposed;

        public ScopedTimer(string name, Action<string, TimeSpan> onComplete, Func<DateTimeOffset>? clock = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            // The wall clock by default gave a negative or inflated time across a clock step, and timed to 15.6 ms on
            // .NET Framework.
            _clock = clock;
            if (clock != null) _start = clock();
            else _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
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
            TimeSpan elapsed = _clock != null
                ? _clock() - _start
                : TimeSpan.FromTicks((long)((System.Diagnostics.Stopwatch.GetTimestamp() - _startTicks) * (10_000_000.0 / System.Diagnostics.Stopwatch.Frequency)));
            _onComplete(_name, elapsed);
        }
    }
}
