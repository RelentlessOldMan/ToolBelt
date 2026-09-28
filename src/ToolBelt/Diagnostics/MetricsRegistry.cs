// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Diagnostics
{
    /// <summary>Summary statistics for a recorded timer/distribution.</summary>
    public sealed class TimerStats
    {
        internal TimerStats(long count, double min, double max, double mean, double p50, double p95, double p99)
        {
            Count = count; Min = min; Max = max; Mean = mean; P50 = p50; P95 = p95; P99 = p99;
        }

        public long Count { get; }
        public double Min { get; }
        public double Max { get; }
        public double Mean { get; }
        public double P50 { get; }
        public double P95 { get; }
        public double P99 { get; }
    }

    /// <summary>An immutable point-in-time view of a <see cref="MetricsRegistry"/>.</summary>
    public sealed class MetricsSnapshot
    {
        internal MetricsSnapshot(IReadOnlyDictionary<string, long> counters, IReadOnlyDictionary<string, double> gauges, IReadOnlyDictionary<string, TimerStats> timers)
        {
            Counters = counters; Gauges = gauges; Timers = timers;
        }

        public IReadOnlyDictionary<string, long> Counters { get; }
        public IReadOnlyDictionary<string, double> Gauges { get; }
        public IReadOnlyDictionary<string, TimerStats> Timers { get; }
    }

    /// <summary>
    /// Named counters, gauges and timers with percentile summaries — the "how is the long run going" facility
    /// batch tools improvise with static fields. Thread-safe; <see cref="Snapshot"/> can atomically reset so
    /// you can report per interval. Timer samples accumulate until reset.
    /// </summary>
    public sealed class MetricsRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _counters = new Dictionary<string, long>();
        private readonly Dictionary<string, double> _gauges = new Dictionary<string, double>();
        private readonly Dictionary<string, List<double>> _timers = new Dictionary<string, List<double>>();

        /// <summary>Adds <paramref name="amount"/> (default 1) to a counter.</summary>
        public void Increment(string name, long amount = 1)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_gate) _counters[name] = (_counters.TryGetValue(name, out long v) ? v : 0) + amount;
        }

        /// <summary>Sets a gauge to its latest value.</summary>
        public void SetGauge(string name, double value)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_gate) _gauges[name] = value;
        }

        /// <summary>Records a timer/distribution sample (e.g. a duration in milliseconds).</summary>
        public void Record(string name, double value)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_gate)
            {
                if (!_timers.TryGetValue(name, out var list)) { list = new List<double>(); _timers[name] = list; }
                list.Add(value);
            }
        }

        /// <summary>Captures the current metrics; if <paramref name="reset"/>, clears them atomically.</summary>
        public MetricsSnapshot Snapshot(bool reset = false)
        {
            lock (_gate)
            {
                var counters = new Dictionary<string, long>(_counters);
                var gauges = new Dictionary<string, double>(_gauges);
                var timers = new Dictionary<string, TimerStats>();
                foreach (var kv in _timers)
                    if (kv.Value.Count > 0)
                        timers[kv.Key] = Summarize(kv.Value);

                if (reset) { _counters.Clear(); _gauges.Clear(); _timers.Clear(); }
                return new MetricsSnapshot(counters, gauges, timers);
            }
        }

        private static TimerStats Summarize(List<double> samples)
        {
            var sorted = new List<double>(samples);
            sorted.Sort();
            double sum = 0;
            for (int i = 0; i < sorted.Count; i++) sum += sorted[i];
            return new TimerStats(sorted.Count, sorted[0], sorted[sorted.Count - 1], sum / sorted.Count,
                Quantile(sorted, 0.50), Quantile(sorted, 0.95), Quantile(sorted, 0.99));
        }

        private static double Quantile(List<double> sorted, double p)
        {
            if (sorted.Count == 1) return sorted[0];
            double position = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(position);
            int hi = (int)Math.Ceiling(position);
            if (lo == hi) return sorted[lo];
            double frac = position - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }
    }
}
