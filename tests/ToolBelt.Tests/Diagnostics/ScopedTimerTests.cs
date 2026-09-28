using System;
using System.Collections.Generic;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class ScopedTimerTests
    {
        private static Func<DateTimeOffset> ClockOf(params DateTimeOffset[] times)
        {
            var q = new Queue<DateTimeOffset>(times);
            return () => q.Dequeue();
        }

        public void ReportsElapsedOnDispose()
        {
            var t0 = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            var clock = ClockOf(t0, t0.AddMilliseconds(250));
            string? name = null; TimeSpan elapsed = default;
            using (new ScopedTimer("op", (n, e) => { name = n; elapsed = e; }, clock)) { }
            Check.Equal("op", name);
            Check.Close(250, elapsed.TotalMilliseconds, 1e-6);
        }

        public void ToMetricsRecordsDuration()
        {
            var t0 = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            var registry = new MetricsRegistry();
            using (ScopedTimer.ToMetrics("load", registry, ClockOf(t0, t0.AddMilliseconds(100)))) { }
            var stats = registry.Snapshot().Timers["load"];
            Check.Equal(1L, stats.Count);
            Check.Close(100, stats.Mean, 1e-6);
        }

        public void DisposeIsIdempotent()
        {
            int calls = 0;
            var t0 = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
            var timer = new ScopedTimer("x", (_, __) => calls++, ClockOf(t0, t0, t0));
            timer.Dispose();
            timer.Dispose();
            Check.Equal(1, calls);
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new ScopedTimer(null!, (_, __) => { }));
            Check.Throws<ArgumentNullException>(() => new ScopedTimer("x", null!));
        }
    }
}
