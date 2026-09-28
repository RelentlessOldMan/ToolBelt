using System;
using System.Threading.Tasks;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class MetricsRegistryTests
    {
        public void CountersAndGauges()
        {
            var m = new MetricsRegistry();
            m.Increment("hits");
            m.Increment("hits", 4);
            m.SetGauge("temp", 21.5);
            m.SetGauge("temp", 22.0); // last wins

            var snap = m.Snapshot();
            Check.Equal(5L, snap.Counters["hits"]);
            Check.Close(22.0, snap.Gauges["temp"], 1e-9);
        }

        public void TimerStats()
        {
            var m = new MetricsRegistry();
            for (int i = 1; i <= 100; i++) m.Record("latency", i); // 1..100
            var t = m.Snapshot().Timers["latency"];
            Check.Equal(100L, t.Count);
            Check.Close(1, t.Min, 1e-9);
            Check.Close(100, t.Max, 1e-9);
            Check.Close(50.5, t.Mean, 1e-9);
            Check.True(t.P95 >= t.P50 && t.P99 >= t.P95, "percentiles should be ordered");
            Check.True(t.P95 > 90, $"p95 was {t.P95}");
        }

        public void SnapshotResetClears()
        {
            var m = new MetricsRegistry();
            m.Increment("a");
            m.Record("t", 1);
            m.Snapshot(reset: true);
            var after = m.Snapshot();
            Check.Equal(0, after.Counters.Count);
            Check.Equal(0, after.Timers.Count);
        }

        public void ThreadSafeIncrement()
        {
            var m = new MetricsRegistry();
            Parallel.For(0, 100000, _ => m.Increment("c"));
            Check.Equal(100000L, m.Snapshot().Counters["c"]);
        }

        public void NullName_Throws()
        {
            var m = new MetricsRegistry();
            Check.Throws<ArgumentNullException>(() => m.Increment(null!));
        }
    }
}
