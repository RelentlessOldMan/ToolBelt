using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>Regressions from review round 25 (Collections, second pass): each test failed before its fix.</summary>
    public sealed class CollectionsReview2Tests
    {
        // ---------------- ObjectPool ----------------

        public void ObjectPool_RacingDoubleReturnDoesNotHandOneInstanceToTwoRenters()
        {
            var inReset = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            int resets = 0;
            var pool = new ObjectPool<object>(() => new object(), _ =>
            {
                // The first reset (thread A) waits; the second (main thread) runs straight through.
                if (Interlocked.Increment(ref resets) == 1) { inReset.Set(); release.Wait(5000); }
            });
            var x = new object();
            var a = new Thread(() => pool.Return(x));
            a.Start();
            Check.True(inReset.Wait(5000));
            pool.Return(x);
            object r1 = pool.Rent();
            release.Set();
            a.Join();
            object r2 = pool.Rent();
            Check.False(ReferenceEquals(r1, r2), "one instance handed to two renters");
        }

        // ---------------- TopN ----------------

        public void TopN_ComparerThrowingMidAddLosesNoRetainedItem()
        {
            int calls = 0;
            bool armed = false;
            var top = new TopN<int>(10, Comparer<int>.Create((p, q) =>
            {
                if (armed && ++calls == 2) throw new InvalidOperationException("comparer");
                return p.CompareTo(q);
            }));
            foreach (int v in new[] { 1, 5, 3, 9, 7, 8 }) top.Add(v);
            armed = true;
            Check.Throws<InvalidOperationException>(() => top.Add(0));
            armed = false;
            int[] kept = top.ToArray();
            foreach (int v in new[] { 1, 5, 3, 9, 7, 8 })
                Check.True(kept.Contains(v), $"lost {v}: [{string.Join(",", kept)}]");
        }

        // ---------------- Bloom filters ----------------

        public void BloomFilters_RejectANaNFalsePositiveRate()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new BloomFilter(1000, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new CountingBloomFilter(1000, double.NaN));
        }

        // ---------------- CountMinSketch ----------------

        public void CountMinSketch_CountsDoNotWrapNegative()
        {
            var s = new CountMinSketch<string>();
            s.Add("a", long.MaxValue);
            try { s.Add("a", 1); } catch (OverflowException) { }
            Check.True(s.Estimate("a") >= 0, $"estimate {s.Estimate("a")}");
            Check.True(s.TotalCount >= 0, $"total {s.TotalCount}");
        }

        // ---------------- Aggregation ----------------

        public void Aggregation_SummarizeHandlesFiniteValuesWhoseSumOverflows()
        {
            var big = Aggregation.Summarize(new[] { 1e308, 1e308 }, _ => "k", v => v)[0];
            Check.Equal(1e308, big.Mean);
            Check.Equal(0.0, big.StandardDeviation);
            Check.True(double.IsPositiveInfinity(big.Sum), "the sum itself is genuinely infinite");

            var alt = Aggregation.Summarize(new[] { 1e308, -1e308, 1e308, -1e308 }, _ => "k", v => v)[0];
            Check.Equal(0.0, alt.Mean);
            double expected = 1e308 * Math.Sqrt(4.0 / 3.0);
            Check.True(Math.Abs(alt.StandardDeviation - expected) <= expected * 1e-12, $"stddev {alt.StandardDeviation}");
        }

        public void Aggregation_SummarizeKeepsOrdinaryResultsExact()
        {
            var s = Aggregation.Summarize(new[] { 1.0, 2.0, 3.0, 4.0 }, _ => 0, v => v)[0];
            Check.Equal(2.5, s.Mean);
            Check.Equal(10.0, s.Sum);
            Check.Equal(Math.Sqrt(5.0 / 3.0), s.StandardDeviation);
        }

        // ---------------- TtlCache ----------------

        public void TtlCache_SaturatesForAClockWithAPositiveOffset()
        {
            var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(2));
            var ttl = TimeSpan.FromTicks(DateTimeOffset.MaxValue.UtcTicks - now.UtcTicks - TimeSpan.FromHours(1).Ticks);
            var cache = new TtlCache<int, int>(ttl, () => now);
            cache.Set(1, 1);
            Check.True(cache.TryGet(1, out int v) && v == 1);
        }

        // ---------------- enumerator versions ----------------

        public void Deque_ChangeBetweenGetEnumeratorAndMoveNextIsDetected()
        {
            var d = new Deque<int>();
            d.PushBack(1); d.PushBack(2);
            using var e = d.GetEnumerator();
            d.PushBack(3);
            Check.Throws<InvalidOperationException>(() => { while (e.MoveNext()) { } });
        }

        public void CircularBuffer_ChangeBetweenGetEnumeratorAndMoveNextIsDetected()
        {
            var b = new CircularBuffer<int>(4);
            b.Add(1); b.Add(2);
            using var e = b.GetEnumerator();
            b.Add(3);
            Check.Throws<InvalidOperationException>(() => { while (e.MoveNext()) { } });
        }
    }
}
