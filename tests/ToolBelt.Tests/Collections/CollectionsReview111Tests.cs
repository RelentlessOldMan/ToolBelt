using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>
    /// Regressions from review round 111 (Collections, seventh pass): each test failed before its fix, except the
    /// .NET Framework array-limit tests, which only run there (on .NET they return at once).
    /// </summary>
    public sealed class CollectionsReview111Tests
    {
        // A key equal by Id whose hash can be made to throw once, after a given number of good calls (a disposed object).
        private sealed class FlakyKey
        {
            private int _ok = int.MaxValue;
            public FlakyKey(int id) => Id = id;
            public int Id { get; }
            public void FailAfter(int goodCalls) => _ok = goodCalls;
            public void Heal() => _ok = int.MaxValue;
            public override bool Equals(object? obj) => obj is FlakyKey k && k.Id == Id;
            public override int GetHashCode()
            {
                if (_ok == 0) { _ok = int.MaxValue; throw new ObjectDisposedException("key"); }
                if (_ok != int.MaxValue) _ok--;
                return Id;
            }
        }

        // ---------------- LruCache ----------------

        public void LruCache_EvictingAKeyWhoseHashThrows_LeavesTheCacheAsItWas()
        {
            var a = new FlakyKey(1);
            var cache = new LruCache<FlakyKey, string>(2);
            cache.Set(a, "a");
            cache.Set(new FlakyKey(2), "b");
            a.FailAfter(0);
            Check.Throws<ObjectDisposedException>(() => cache.Set(new FlakyKey(3), "c"));
            a.Heal();
            Check.Equal(2, cache.Count);
            Check.Equal(2, cache.Keys.Count);
            Check.True(cache.TryGet(new FlakyKey(1), out _));
            Check.True(cache.TryGet(new FlakyKey(2), out _));
            Check.False(cache.TryGet(new FlakyKey(3), out _));
        }

        public void LruCache_RemoveWhoseHashThrows_KeepsTheEntry()
        {
            var a = new FlakyKey(1);
            var cache = new LruCache<FlakyKey, string>(4);
            cache.Set(a, "a");
            a.FailAfter(1);   // the lookup succeeds, the removal throws
            Check.Throws<ObjectDisposedException>(() => cache.Remove(a));
            a.Heal();
            Check.Equal(1, cache.Count);
            Check.Equal(1, cache.Keys.Count);
            Check.True(cache.TryGet(a, out var v) && v == "a");
        }

        // ---------------- IndexedPriorityQueue ----------------

        public void IndexedPriorityQueue_SiftingPastAKeyWhoseHashThrows_DoesNotHashIt()
        {
            var keys = Enumerable.Range(0, 31).Select(i => new FlakyKey(i)).ToArray();
            var q = new IndexedPriorityQueue<FlakyKey, int>();
            foreach (var k in keys) q.Enqueue(k, k.Id);
            keys[0].FailAfter(0);   // the root: moving the last key to the top displaces it
            q.UpdatePriority(keys[30], -1);
            keys[0].Heal();
            var drained = new List<int>();
            while (q.TryDequeueMin(out var k, out var p)) { Check.Equal(k.Id == 30 ? -1 : k.Id, p); drained.Add(k.Id); }
            Check.Equal(31, drained.Count);
            Check.Equal(30, drained[0]);
            Check.True(drained.Skip(1).SequenceEqual(Enumerable.Range(0, 30)));
        }

        public void IndexedPriorityQueue_DifferentialAgainstASortedReference()
        {
            var random = new Random(111);
            for (int trial = 0; trial < 200; trial++)
            {
                var q = new IndexedPriorityQueue<int, double>();
                var reference = new Dictionary<int, double>();
                for (int op = 0; op < 200; op++)
                {
                    int key = random.Next(40);
                    double priority = random.Next(20);
                    int kind = random.Next(3);
                    if (kind == 0 && !reference.ContainsKey(key)) { q.Enqueue(key, priority); reference[key] = priority; }
                    else if (kind == 1 && reference.ContainsKey(key)) { q.UpdatePriority(key, priority); reference[key] = priority; }
                    else if (kind == 2 && q.TryDequeueMin(out int k, out double p))
                    {
                        Check.Equal(reference.Values.Min(), p);
                        Check.Equal(reference[k], p);
                        reference.Remove(k);
                    }
                    Check.Equal(reference.Count, q.Count);
                }
                foreach (var pair in reference) Check.True(q.TryGetPriority(pair.Key, out double got) && got == pair.Value);
            }
        }

        public void IndexedPriorityQueue_UpdatePriority_KeepsTheFirstKey()
        {
            var first = new FlakyKey(7);
            var q = new IndexedPriorityQueue<FlakyKey, int>();
            q.Enqueue(first, 5);
            q.UpdatePriority(new FlakyKey(7), 1);
            Check.True(q.TryPeekMin(out var k, out int p));
            Check.True(ReferenceEquals(first, k));
            Check.Equal(1, p);
        }

        // ---------------- BiMap ----------------

        public void BiMap_RemoveByKey_ValueHashThrows_KeepsBothSides()
        {
            var v = new FlakyKey(9);
            var map = new BiMap<int, FlakyKey>();
            map.Add(1, v);
            v.FailAfter(0);
            Check.Throws<ObjectDisposedException>(() => map.RemoveByKey(1));
            v.Heal();
            Check.True(map.ContainsKey(1));
            Check.True(map.ContainsValue(v));
            Check.Equal(1, map.Count);
        }

        public void BiMap_RemoveByValue_KeyHashThrows_KeepsBothSides()
        {
            var k = new FlakyKey(9);
            var map = new BiMap<FlakyKey, int>();
            map.Add(k, 1);
            k.FailAfter(0);
            Check.Throws<ObjectDisposedException>(() => map.RemoveByValue(1));
            k.Heal();
            Check.True(map.ContainsKey(k));
            Check.True(map.ContainsValue(1));
            Check.Equal(1, map.Count);
        }

        // ---------------- MultiDictionary ----------------

        public void MultiDictionary_Remove_KeyHashThrows_KeepsTheValueAndTheTotal()
        {
            var k = new FlakyKey(3);
            var d = new MultiDictionary<FlakyKey, string>();
            d.Add(k, "a");
            k.FailAfter(1);
            Check.Throws<ObjectDisposedException>(() => d.Remove(k, "a"));
            k.Heal();
            Check.Equal(1L, d.Total);
            Check.True(d.GetValues(k).SequenceEqual(new[] { "a" }));
        }

        public void MultiDictionary_RemoveAll_KeyHashThrows_KeepsTheTotal()
        {
            var k = new FlakyKey(3);
            var d = new MultiDictionary<FlakyKey, string>();
            d.Add(k, "a");
            d.Add(k, "b");
            k.FailAfter(1);
            Check.Throws<ObjectDisposedException>(() => d.RemoveAll(k));
            k.Heal();
            Check.Equal(2L, d.Total);
            Check.Equal(2, d.GetValues(k).Count);
        }

        // ---------------- TtlCache ----------------

        public void TtlCache_MaxValueTtl_NeverExpires_EvenAtTheEndOfTime()
        {
            var cache = new TtlCache<string, int>(TimeSpan.MaxValue, () => DateTimeOffset.MaxValue);
            cache.Set("k", 1);
            Check.True(cache.TryGet("k", out int v) && v == 1);
            Check.Equal(1, cache.Count);
            Check.True(cache.Remove("k"));

            var now = DateTimeOffset.MaxValue.AddTicks(-1);
            var late = new TtlCache<string, int>(TimeSpan.MaxValue, () => now);
            late.Set("k", 1);
            now = DateTimeOffset.MaxValue;
            Check.True(late.TryGet("k", out _));
        }

        public void TtlCache_Remove_ClockThrows_KeepsTheEntry()
        {
            bool broken = false;
            var cache = new TtlCache<string, int>(TimeSpan.FromMinutes(1),
                () => broken ? throw new InvalidOperationException("clock") : new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            cache.Set("k", 1);
            broken = true;
            Check.Throws<InvalidOperationException>(() => cache.Remove("k"));
            broken = false;
            Check.True(cache.TryGet("k", out _));
        }

        // ---------------- CartesianProduct, Combinations, SlidingWindow ----------------

        public void CartesianProduct_ListChangedAfterTheCall_UsesTheCheckedSequences()
        {
            var seqs = new List<IReadOnlyList<int>> { new[] { 1, 2 }, new[] { 3 } };
            var product = CartesianProduct.Of<int>(seqs);
            seqs[1] = null!;
            var all = product.ToList();
            Check.Equal(2, all.Count);
            Check.True(all[0].SequenceEqual(new[] { 1, 3 }) && all[1].SequenceEqual(new[] { 2, 3 }));
        }

        public void Combinations_ListShrinksMidEnumeration_IsAnInvalidOperation()
        {
            var items = new List<int> { 1, 2, 3, 4 };
            using var e = Combinatorics.Combinations(items, 2).GetEnumerator();
            Check.True(e.MoveNext());
            items.RemoveAt(3);
            items.RemoveAt(2);
            Check.Throws<InvalidOperationException>(() => { while (e.MoveNext()) { } });
        }

        public void SlidingWindow_ListShrinksMidEnumeration_IsAnInvalidOperation()
        {
            var items = new List<int> { 1, 2, 3, 4, 5 };
            using var e = SlidingWindow.Over(items, 3).GetEnumerator();
            Check.True(e.MoveNext());
            items.RemoveAt(4);
            items.RemoveAt(3);
            Check.Throws<InvalidOperationException>(() => { while (e.MoveNext()) { } });
        }

        // ---------------- CountMinSketch ----------------

        private static void CheckOverCount<T>(IReadOnlyList<T> keys, double maxMean)
        {
            var sketch = new CountMinSketch<T>();
            foreach (var k in keys) sketch.Add(k);
            double over = keys.Sum(k => (double)(sketch.Estimate(k) - 1));
            Check.True(over / keys.Count <= maxMean, $"{typeof(T).Name}: mean over-count {over / keys.Count}");
        }

        public void CountMinSketch_LongKeysSharingA32BitHash_AreCountedApart()
        {
            // i * (2^32 + 1) has equal halves, so every one hashed (lo ^ hi) to 0: each over-counted by 1999.
            CheckOverCount(Enumerable.Range(0, 2000).Select(i => i * 0x1_0000_0001L).ToArray(), 0.5);
            // A packed (x << 32) | y grid: a mean over-count of 32 at the default width.
            CheckOverCount((from x in Enumerable.Range(0, 45) from y in Enumerable.Range(0, 45) select ((long)x << 32) | (uint)y).ToArray(), 0.5);
            CheckOverCount(Enumerable.Range(0, 2000).Select(i => (ulong)i * 0x1_0000_0001UL).ToArray(), 0.5);
            CheckOverCount(Enumerable.Range(0, 2000).Select(i => (long?)(i * 0x1_0000_0001L)).ToArray(), 0.5);
            CheckOverCount(Enumerable.Range(1, 2000).Select(i => new DateTime(i * 0x1_0000_0001L)).ToArray(), 0.5);
            CheckOverCount(Enumerable.Range(1, 2000).Select(i => TimeSpan.FromTicks(i * 0x1_0000_0001L)).ToArray(), 0.5);
        }

        public void CountMinSketch_EqualWideKeys_ShareACount()
        {
            var d = new CountMinSketch<double>();
            d.Add(0.0); d.Add(-0.0); d.Add(double.NaN); d.Add(-double.NaN);
            Check.Equal(2L, d.Estimate(0.0));
            Check.Equal(2L, d.Estimate(double.NaN));
            var t = new CountMinSketch<DateTime>();
            t.Add(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            t.Add(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local));
            Check.Equal(2L, t.Estimate(new DateTime(2026, 1, 1)));
            var o = new CountMinSketch<DateTimeOffset>();
            o.Add(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
            o.Add(new DateTimeOffset(2026, 1, 1, 14, 0, 0, TimeSpan.FromHours(2)));
            Check.Equal(2L, o.Estimate(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
            var n = new CountMinSketch<long?>();
            n.Add(null); n.Add(null);
            Check.Equal(2L, n.Estimate(null));
        }

        // ---------------- Aggregation ----------------

        public void Aggregation_Summarize_LargeMeanSmallSpread_KeepsTheDeviation()
        {
            var values = new[] { 10000000004.0, 10000000001, 9999999999, 10000000001, 10000000004, 10000000003 };
            var g = Aggregation.Summarize(values, _ => 0, v => v).Single();
            Check.Close(2.0, g.StandardDeviation, 1e-14);
            Check.Equal(10000000002.0, g.Mean);
        }

        public void Aggregation_Summarize_DifferentialAgainstExactIntegers()
        {
            var random = new Random(111);
            for (int trial = 0; trial < 2000; trial++)
            {
                long offset = trial % 3 == 0 ? 0 : trial % 3 == 1 ? 10_000_000_000L : -3_000_000_000_000L;
                int n = 2 + random.Next(30);
                var xs = Enumerable.Range(0, n).Select(_ => offset + random.Next(-50, 51)).ToArray();
                var g = Aggregation.Summarize(xs, _ => 0, x => (double)x).Single();
                BigInteger sum = xs.Aggregate(BigInteger.Zero, (s, x) => s + x);
                BigInteger sumSq = xs.Aggregate(BigInteger.Zero, (s, x) => s + (BigInteger)x * x);
                double variance = (double)(n * sumSq - sum * sum) / ((double)n * (n - 1));
                double sd = Math.Sqrt(variance);
                double mean = (double)offset + (double)(sum - (BigInteger)offset * n) / n;
                Check.True(Math.Abs(g.StandardDeviation - sd) <= 1e-12 * Math.Max(sd, 1e-300) || (sd == 0 && g.StandardDeviation == 0),
                    $"trial {trial}: sd {g.StandardDeviation:R}, exact {sd:R}");
                Check.True(Math.Abs(g.Mean - mean) <= 2e-16 * Math.Abs(mean) + 1e-12, $"trial {trial}: mean {g.Mean:R}, exact {mean:R}");
            }
        }

        // ---------------- Bloom filters ----------------

        private static double ExactRate(long m, int k, int n) => Math.Pow(1 - Math.Pow(1 - 1.0 / m, (double)k * n), k);

        public void BloomFilters_FewItems_MeetTheTargetRate()
        {
            foreach (int n in Enumerable.Range(1, 60).Concat(new[] { 100, 1000, 100_000 }))
                foreach (double p in new[] { 0.5, 0.3, 0.1, 0.01, 0.001, 1e-6 })
                {
                    var b = new BloomFilter(n, p);
                    Check.True(ExactRate(b.BitCount, b.HashCount, n) <= p * (1 + 1e-9), $"BloomFilter n={n} p={p}: m={b.BitCount} k={b.HashCount}");
                    var c = new CountingBloomFilter(n, p);
                    Check.True(ExactRate(c.SlotCount, c.HashCount, n) <= p * (1 + 1e-9), $"CountingBloomFilter n={n} p={p}: m={c.SlotCount} k={c.HashCount}");
                }
        }

        public void BloomFilter_ThreeItems_MeasuredRateIsOnTarget()
        {
            // 3 items at 0.01 got 29 bits and 7 hashes: a measured rate of 0.0128.
            var random = new Random(111);
            byte[] Key() { var b = new byte[16]; random.NextBytes(b); return b; }
            long hits = 0, probes = 0;
            for (int f = 0; f < 2000; f++)
            {
                var filter = new BloomFilter(3, 0.01);
                for (int i = 0; i < 3; i++) filter.Add(Key());
                for (int i = 0; i < 500; i++, probes++) if (filter.MightContain(Key())) hits++;
            }
            double rate = (double)hits / probes;
            Check.True(rate < 0.0114, $"measured rate {rate}");
        }

        // ---------------- NearestBy (doc: an infinite key is the limit) ----------------

        public void NearestBy_InfiniteKey_IsTheLimit()
        {
            var list = new[] { -5.0, 0, 1, 1, 5, 5 };
            Check.Equal(4, list.NearestBy(double.PositiveInfinity, x => x));
            Check.Equal(0, list.NearestBy(double.NegativeInfinity, x => x));
        }

        // ---------------- .NET Framework array limits ----------------

        private static bool OnFramework => RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal);

        public void FrameworkArrayLimit_IsAnArgumentError()
        {
            if (!OnFramework) return;   // .NET allows 0x7FFFFFC7; these sizes would allocate there
            const int past = 0x7FF00000;   // above .NET Framework's 0x7FEFFFFF, below Array.MaxLength
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(past, 1)).ParamName);
            Check.Equal("depth", Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(1, past)).ParamName);
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new DisjointSet(past)).ParamName);
            Check.Equal("size", Check.Throws<ArgumentOutOfRangeException>(() => new FenwickTree(past)).ParamName);
            Check.Equal("capacity", Check.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<long>(past)).ParamName);
            Check.Equal("initialCapacity", Check.Throws<ArgumentOutOfRangeException>(() => new Deque<long>(past)).ParamName);
        }
    }
}
