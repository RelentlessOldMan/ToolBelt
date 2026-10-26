using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>
    /// Regressions from review round 44 (Collections, third pass): each test failed before its fix. The int-overflow
    /// fixes in Deque.PushFront and the heaps' SiftDown need 1-2 GB arrays, so they were confirmed by a probe instead.
    /// </summary>
    public sealed class CollectionsReview3Tests
    {
        // ---------------- Bloom filters ----------------

        private static double FalsePositiveRate(Action<byte[]> add, Func<byte[], bool> mightContain, int n)
        {
            var random = new Random(42);
            byte[] Key() { var b = new byte[16]; random.NextBytes(b); return b; }
            for (int i = 0; i < n; i++) add(Key());
            int hits = 0;
            const int probes = 200_000;
            for (int i = 0; i < probes; i++) if (mightContain(Key())) hits++;
            return (double)hits / probes;
        }

        public void BloomFilter_EvenBitCount_MeetsTheTargetRate()
        {
            var f = new BloomFilter(1000, 0.001);
            Check.Equal(0, f.BitCount % 2);
            double rate = FalsePositiveRate(f.Add, f.MightContain, 1000);
            Check.True(rate < 0.002, $"false-positive rate {rate}");
        }

        public void CountingBloomFilter_EvenSlotCount_MeetsTheTargetRate()
        {
            var f = new CountingBloomFilter(1000, 0.001);
            Check.Equal(0, f.SlotCount % 2);
            double rate = FalsePositiveRate(f.Add, f.MightContain, 1000);
            Check.True(rate < 0.002, $"false-positive rate {rate}");
        }

        // ---------------- Aggregation ----------------

        public void Summarize_ValuesAtDoubleMaxValue_StayFinite()
        {
            var s = Aggregation.Summarize(new[] { double.MaxValue, double.MaxValue }, _ => 1, v => v).Single();
            Check.Equal(double.MaxValue, s.Mean);
            Check.Equal(0.0, s.StandardDeviation);
            s = Aggregation.Summarize(new[] { -double.MaxValue, -double.MaxValue }, _ => 1, v => v).Single();
            Check.Equal(-double.MaxValue, s.Mean);
            s = Aggregation.Summarize(new[] { double.MaxValue, double.MaxValue, -double.MaxValue }, _ => 1, v => v).Single();
            Check.Close(double.MaxValue / 3, s.Mean, 1e-12 * double.MaxValue);
            // the deviation, √(4/3)·Max, really is past double.MaxValue, so only the mean is checked here
        }

        // ---------------- ReservoirSampler ----------------

        public void ReservoirSampler_TinyAcceptanceWeight_DoesNotReplaceEveryItem()
        {
            var sampler = new ReservoirSampler<int>(1, new Random(7));
            sampler.Add(-1);
            var t = typeof(ReservoirSampler<int>);
            t.GetField("_w", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(sampler, 1e-17); // ~1e16 items in
            t.GetField("_nextAccept", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(sampler, 1L); // accept the next
            int replacements = 0, last = -1;
            for (int i = 0; i < 1000; i++)
            {
                sampler.Add(i);
                if (sampler.Sample[0] != last) { replacements++; last = sampler.Sample[0]; }
            }
            Check.Equal(1, replacements);
        }

        // ---------------- A key whose hash throws ----------------

        // Hashes once, then throws: the presence check passes and the insert fails. Each collection holds an item
        // first, since an empty Dictionary answers ContainsKey without hashing.
        private sealed class Flaky
        {
            private int _calls;
            public override int GetHashCode() => ++_calls == 1 ? 1 : throw new InvalidOperationException("hash failed");
            public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        }

        public void OrderedSet_FailedAdd_LeavesNoOrphan()
        {
            var set = new OrderedSet<object>();
            set.Add("x");
            Check.Throws<InvalidOperationException>(() => set.Add(new Flaky()));
            Check.Equal(1, set.Count);
            Check.Equal(1, set.Count());
        }

        public void OrderedDictionary_FailedAdd_LeavesNoOrphan()
        {
            var d = new OrderedDictionary<object, int>();
            d.Add("x", 0);
            Check.Throws<InvalidOperationException>(() => d.Add(new Flaky(), 1));
            Check.Throws<InvalidOperationException>(() => d[new Flaky()] = 2);
            Check.Equal(1, d.Count);
            Check.Equal(1, d.Count());
        }

        public void LruCache_FailedSet_LeavesNoOrphan()
        {
            var cache = new LruCache<object, int>(2);
            cache.Set("x", 0);
            Check.Throws<InvalidOperationException>(() => cache.Set(new Flaky(), 0));
            Check.Equal(1, cache.Keys.Count);
            for (int i = 1; i <= 3; i++) cache.Set(i, i);
            Check.Equal(2, cache.Count);
            Check.Equal(2, cache.Keys.Count);
        }

        public void BiMap_FailedValueInsert_LeavesNoOneSidedPair()
        {
            var map = new BiMap<int, object>();
            map.Add(0, "x");
            Check.Throws<InvalidOperationException>(() => map.Add(1, new Flaky()));
            Check.Equal(1, map.Count);
            Check.Equal(1, map.Values.Count);
            Check.False(map.ContainsKey(1));
        }

        public void IndexedPriorityQueue_FailedEnqueue_LeavesNoOrphan()
        {
            var q = new IndexedPriorityQueue<object, int>();
            q.Enqueue("x", 5);
            Check.Throws<InvalidOperationException>(() => q.Enqueue(new Flaky(), 1));
            Check.Equal(1, q.Count);
            Check.True(q.TryDequeueMin(out object key, out _));
            Check.Equal((object)"x", key);
        }

        // ---------------- Enumeration ----------------

        public void Deque_EmptiedBeforeTheFirstMoveNext_Throws()
        {
            var d = new Deque<int>();
            d.PushBack(1);
            d.PushBack(2);
            using var e = d.GetEnumerator();
            d.Clear();
            Check.Throws<InvalidOperationException>(() => e.MoveNext());
        }

        public void CircularBuffer_EmptiedBeforeTheFirstMoveNext_Throws()
        {
            var b = new CircularBuffer<int>(4);
            b.Add(1);
            b.Add(2);
            using var e = b.GetEnumerator();
            b.Clear();
            Check.Throws<InvalidOperationException>(() => e.MoveNext());
        }

        // ---------------- Argument names ----------------

        public void CartesianProduct_NullArgument_IsNamed()
        {
            var a = new[] { 1 };
            Check.Equal("first", Check.Throws<ArgumentNullException>(() => CartesianProduct.Of(null!, a)).ParamName);
            Check.Equal("second", Check.Throws<ArgumentNullException>(() => CartesianProduct.Of(a, null!)).ParamName);
            Check.Equal("rest", Check.Throws<ArgumentException>(() => CartesianProduct.Of(a, a, null!, a)).ParamName);
        }

        public void WeightedWithoutReplacement_LengthMismatch_NamesWeights()
        {
            var e = Check.Throws<ArgumentException>(() =>
                SamplingPlans.WeightedWithoutReplacement(new[] { 1, 2 }, new[] { 1.0 }, 1, new Random(1)));
            Check.Equal("weights", e.ParamName);
        }

        public void DataTableLite_BadArguments_AreNamed()
        {
            var t = new DataTableLite(new[] { "a", "b" }).AddRow(1, 2);
            Check.Equal("columns", Check.Throws<ArgumentException>(() => t.Select("a", null!)).ParamName);
            var other = new DataTableLite(new[] { "a", "c" }).AddRow(1, 3);
            Check.Equal("rightPrefix", Check.Throws<ArgumentException>(() => t.InnerJoin(other, "a", "a")).ParamName);
            Check.Equal("column", Check.Throws<ArgumentOutOfRangeException>(() => _ = t[0, 99]).ParamName);
            Check.Equal("row", Check.Throws<ArgumentOutOfRangeException>(() => _ = t[5, 0]).ParamName);
            Check.Equal("row", Check.Throws<ArgumentOutOfRangeException>(() => _ = t[5, "a"]).ParamName);
            Check.Equal("row", Check.Throws<ArgumentOutOfRangeException>(() => t.GetRow(-1)).ParamName);
            Check.Equal("column", Check.Throws<ArgumentOutOfRangeException>(() => t.Where(r => r[99] is null)).ParamName);
        }
    }
}
