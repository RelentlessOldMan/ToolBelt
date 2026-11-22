// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Numerics;

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
            // The exact sum as non-overlapping partials (Shewchuk), in two tiers: values of at least 2⁻⁹⁵⁸ scaled by 2⁻⁶⁴
            // (exact, and no count overflows them) and smaller ones unscaled. A sum of v·2⁻¹⁰²⁴ made every sample below 4
            // subnormal: beside Max, Max, −Max, −Max, a 1e-5 averaged 1.4e-10 relative off and a 1e-20 averaged 0.
            public double[] Big = new double[4], Small = new double[4];
            public int NBig, NSmall;
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
                // −0 below +0: < and > never replaced one zero with the other, so the signs of Min and Max depended on
                // which zero came first ([−0, +0, +0] gave a Max of −0).
                if (value < s.Min || (value == 0 && s.Min == 0 && IsNegativeZero(value))) s.Min = value;
                if (value > s.Max || (value == 0 && s.Max == 0 && IsNegativeZero(s.Max))) s.Max = value;
                if (Math.Abs(value) >= BigFrom) Accumulate(ref s.Big, ref s.NBig, value * TwoToMinus64);
                else Accumulate(ref s.Small, ref s.NSmall, value);
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
            var copies = new List<KeyValuePair<string, (double[] Kept, long Count, double Min, double Max, double[] Big, double[] Small)>>();
            lock (_gate)
            {
                counters = new Dictionary<string, long>(_counters);
                gauges = new Dictionary<string, double>(_gauges);
                foreach (var kv in _timers)
                    if (kv.Value.Count > 0)
                    {
                        Samples s = kv.Value;
                        var big = new double[s.NBig];
                        var small = new double[s.NSmall];
                        Array.Copy(s.Big, big, s.NBig);
                        Array.Copy(s.Small, small, s.NSmall);
                        copies.Add(new KeyValuePair<string, (double[], long, double, double, double[], double[])>(
                            kv.Key, (s.Kept.ToArray(), s.Count, s.Min, s.Max, big, small)));
                    }
                if (reset) { _counters.Clear(); _gauges.Clear(); _timers.Clear(); }
            }
            var timers = new Dictionary<string, TimerStats>();
            foreach (var kv in copies)
            {
                var c = kv.Value;
                // Exact count, min, max and mean; percentiles from the samples (all of them, or the reservoir past the cap).
                // The mean is the same under the cap and past it: under it, an overflowing sum fell back to adding v/n in
                // sorted order, where large opposite terms swallowed the small ones (Max, Max, -Max, -Max, 6 gave 0, and
                // 1.2 past the cap).
                // The exact sum over the count, rounded once, so it needs no clamp to [min, max]: Math.Min and Math.Max order
                // −0 and +0 differently on .NET Framework, so one −0 sample averaged −0 on .NET 8 and +0 there (now +0).
                double mean = ExactMean(c.Big, c.Small, c.Count);
                var kept = new List<double>(c.Kept);
                kept.Sort();
                NegativeZerosFirst(kept);
                timers[kv.Key] = new TimerStats(c.Count, c.Min, c.Max, mean, Quantile(kept, 0.50), Quantile(kept, 0.95), Quantile(kept, 0.99));
            }
            return new MetricsSnapshot(counters, gauges, timers);
        }

        private const double TwoToMinus64 = 1.0 / 18446744073709551616.0;
        private const double BigFrom = 4.1045368012983762e-289; // 2⁻⁹⁵⁸: scaled by 2⁻⁶⁴ it is still normal

        // Adds v to the exact sum held as non-overlapping partials, smallest first (Shewchuk; Python's math.fsum).
        private static void Accumulate(ref double[] partials, ref int count, double v)
        {
            int k = 0;
            for (int i = 0; i < count; i++)
            {
                double y = partials[i];
                if (Math.Abs(v) < Math.Abs(y)) { double t = v; v = y; y = t; }
                double hi = v + y;
                double lo = y - (hi - v);
                if (lo != 0) partials[k++] = lo;
                v = hi;
            }
            if (k == partials.Length) Array.Resize(ref partials, 2 * k);
            partials[k++] = v;
            count = k;
        }

        // (Σ big·2⁶⁴ + Σ small) / count rounded once to the nearest double, ties to even; an exact zero sum averages +0,
        // as the plain sum (from +0) gives it.
        private static double ExactMean(double[] big, double[] small, long count)
        {
            const int Base = -1074 - 64;                    // every partial is a multiple of 2^Base
            BigInteger total = BigInteger.Zero;
            foreach (double p in big) total += Scaled(p, 64 - Base);
            foreach (double p in small) total += Scaled(p, -Base);
            if (total.IsZero) return 0;
            bool negative = total.Sign < 0;
            total = BigInteger.Abs(total);
            // total·2^Base / count: a quotient of at least 64 bits beyond the count, its remainder kept as a sticky bit.
            BigInteger q = BigInteger.DivRem(total << 128, count, out BigInteger rem);
            q = (q << 1) | (rem.IsZero ? BigInteger.Zero : BigInteger.One);
            double r = RoundToDouble(q, Base - 129);
            return negative ? -r : r;
        }

        // x·2^shift as an integer (x a multiple of 2^-shift).
        private static BigInteger Scaled(double x, int shift)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int be = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else m |= 1L << 52;
            BigInteger v = new BigInteger(m) << (be - 1075 + shift);
            return bits < 0 ? -v : v;
        }

        // m·2^e (m > 0) rounded once to the nearest double, ties to even, subnormals included.
        private static double RoundToDouble(BigInteger m, long e)
        {
            long top = BitLength(m) - 1 + e;               // floor(log2 (m·2^e))
            if (top > 1023) return double.PositiveInfinity;
            long q = Math.Max(top - 52, -1074);            // the result's ulp
            long shift = q - e;
            BigInteger kept;
            if (shift <= 0) kept = m << (int)-shift;
            else
            {
                kept = m >> (int)shift;
                BigInteger rest = m - (kept << (int)shift), half = BigInteger.One << (int)(shift - 1);
                if (rest > half || (rest == half && !kept.IsEven)) kept += 1;
            }
            double r = (long)kept;                         // at most 2^53, so exact
            return q >= -1022 ? r * Pow2((int)q) : r * Pow2(-1022) * Pow2((int)(q + 1022));
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52); // −1022 ≤ k ≤ 1023

        private static long BitLength(BigInteger m)
        {
            byte[] b = m.ToByteArray();
            int i = b.Length - 1;
            while (i > 0 && b[i] == 0) i--;
            long n = 8L * i;
            for (int t = b[i]; t != 0; t >>= 1) n++;
            return n;
        }

        private static bool IsNegativeZero(double v) => v == 0 && BitConverter.DoubleToInt64Bits(v) < 0;

        // The default sort leaves −0 and +0 in whatever order it meets them, so P50 of [+0, −0, +0] was −0 beside a Min
        // and Max of +0. The run of zeros is rewritten with its −0s first.
        private static void NegativeZerosFirst(List<double> sorted)
        {
            int lo = sorted.BinarySearch(0.0);
            if (lo < 0) return;
            while (lo > 0 && sorted[lo - 1] == 0) lo--;
            int hi = lo, negative = 0;
            for (; hi < sorted.Count && sorted[hi] == 0; hi++) if (IsNegativeZero(sorted[hi])) negative++;
            for (int i = lo; i < hi; i++) sorted[i] = i - lo < negative ? -0.0 : 0.0;
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
