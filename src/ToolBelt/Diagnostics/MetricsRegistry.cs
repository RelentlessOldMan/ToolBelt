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
    /// you can report per interval. Timer samples accumulate until reset, up to a cap per timer (see the constructor).
    /// </summary>
    public sealed class MetricsRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _counters = new Dictionary<string, long>();
        private readonly Dictionary<string, double> _gauges = new Dictionary<string, double>();
        private readonly Dictionary<string, Samples> _timers = new Dictionary<string, Samples>();
        private readonly int _maxSamples;

        /// <summary>Default for <c>maxSamplesPerTimer</c>: about a million samples (8 MB) per timer.</summary>
        public const int DefaultMaxSamplesPerTimer = 1 << 20;

        public MetricsRegistry() : this(DefaultMaxSamplesPerTimer) { }

        /// <param name="maxSamplesPerTimer">
        /// Samples kept per timer. Count, min, max and mean stay exact past it; the percentiles come from a uniform random
        /// sample of that size (reservoir sampling with a fixed seed). Samples were kept without limit: 20 million took
        /// 400 MB.
        /// </param>
        public MetricsRegistry(int maxSamplesPerTimer)
        {
            if (maxSamplesPerTimer < 1)
                throw new ArgumentOutOfRangeException(nameof(maxSamplesPerTimer), maxSamplesPerTimer, "At least one sample must be kept.");
            _maxSamples = maxSamplesPerTimer;
        }

        // A timer's samples: all of them up to the cap, then a reservoir, with exact running count, min, max and sum.
        private sealed class Samples
        {
            public readonly List<double> Kept = new List<double>();
            public long Count;
            public double Min = double.PositiveInfinity, Max = double.NegativeInfinity;
            public double Sum, ScaledSum;                                     // ScaledSum is Σ v·2⁻¹⁰²⁴, for when Sum overflows
            public Random? Random;
        }

        /// <summary>Adds <paramref name="amount"/> (default 1) to a counter.</summary>
        public void Increment(string name, long amount = 1)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_gate)
            {
                long v = _counters.TryGetValue(name, out long current) ? current : 0;
                // Saturates: long.MaxValue + 1 wrapped to long.MinValue.
                long sum = unchecked(v + amount);
                if (amount > 0 && sum < v) sum = long.MaxValue;
                else if (amount < 0 && sum > v) sum = long.MinValue;
                _counters[name] = sum;
            }
        }

        /// <summary>Sets a gauge to its latest value.</summary>
        public void SetGauge(string name, double value)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_gate) _gauges[name] = value;
        }

        /// <summary>Records a timer/distribution sample (e.g. a duration in milliseconds). NaN and infinity are rejected.</summary>
        public void Record(string name, double value)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            // An infinite sample made the mean and percentiles NaN.
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "A sample must be finite.");
            lock (_gate)
            {
                if (!_timers.TryGetValue(name, out var s)) { s = new Samples(); _timers[name] = s; }
                s.Count++;
                if (value < s.Min) s.Min = value;
                if (value > s.Max) s.Max = value;
                s.Sum += value;
                s.ScaledSum += value * TwoToMinus1024;
                if (s.Kept.Count < _maxSamples) s.Kept.Add(value);
                else
                {
                    // Algorithm R: each of the Count samples so far is kept with equal probability.
                    s.Random ??= new Random(0x5EED);
                    long slot = (long)(s.Random.NextDouble() * s.Count);
                    if (slot < _maxSamples) s.Kept[(int)slot] = value;
                }
            }
        }

        /// <summary>Captures the current metrics; if <paramref name="reset"/>, clears them atomically.</summary>
        public MetricsSnapshot Snapshot(bool reset = false)
        {
            // Sorting happens after the lock is released: it was held for the sort, so a snapshot of 20 million samples
            // blocked every Increment for about 3 s.
            Dictionary<string, long> counters;
            Dictionary<string, double> gauges;
            var copies = new List<KeyValuePair<string, (double[] Kept, long Count, double Min, double Max, double Sum, double ScaledSum)>>();
            lock (_gate)
            {
                counters = new Dictionary<string, long>(_counters);
                gauges = new Dictionary<string, double>(_gauges);
                foreach (var kv in _timers)
                    if (kv.Value.Count > 0)
                    {
                        Samples s = kv.Value;
                        copies.Add(new KeyValuePair<string, (double[], long, double, double, double, double)>(
                            kv.Key, (s.Kept.ToArray(), s.Count, s.Min, s.Max, s.Sum, s.ScaledSum)));
                    }
                if (reset) { _counters.Clear(); _gauges.Clear(); _timers.Clear(); }
            }
            var timers = new Dictionary<string, TimerStats>();
            foreach (var kv in copies)
            {
                var c = kv.Value;
                if (c.Count == c.Kept.Length) timers[kv.Key] = Summarize(new List<double>(c.Kept));
                else
                {
                    // Past the cap: exact count, min, max and mean, percentiles from the reservoir.
                    double mean = c.Sum / c.Count;
                    if (double.IsInfinity(mean)) mean = c.ScaledSum / c.Count * TwoTo512 * TwoTo512;
                    mean = Math.Max(c.Min, Math.Min(c.Max, mean));
                    var kept = new List<double>(c.Kept);
                    kept.Sort();
                    timers[kv.Key] = new TimerStats(c.Count, c.Min, c.Max, mean, Quantile(kept, 0.50), Quantile(kept, 0.95), Quantile(kept, 0.99));
                }
            }
            return new MetricsSnapshot(counters, gauges, timers);
        }

        private const double TwoTo512 = 1.3407807929942597e154;
        private const double TwoToMinus1024 = 5.562684646268003e-309;

        private static TimerStats Summarize(List<double> sorted)
        {
            sorted.Sort();
            double sum = 0;
            for (int i = 0; i < sorted.Count; i++) sum += sorted[i];
            double mean = sum / sorted.Count;
            if (double.IsInfinity(mean))
            {
                mean = 0;                                                     // the sum overflowed: average scaled values
                for (int i = 0; i < sorted.Count; i++) mean += sorted[i] / sorted.Count;
            }
            double min = sorted[0], max = sorted[sorted.Count - 1];
            // Rounding put the mean of three 0.1s above the largest sample.
            mean = Math.Max(min, Math.Min(max, mean));
            return new TimerStats(sorted.Count, min, max, mean,
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
            // Clamped: rounding put P95 of three equal samples below them.
            return Math.Max(sorted[lo], Math.Min(sorted[hi], sorted[lo] * (1 - frac) + sorted[hi] * frac));
        }
    }
}
