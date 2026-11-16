using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    // Round 96 (Collections, sixth pass): each regression failed on the code before its fix (the zero-Random ones by
    // hanging past the 60 s timeout).
    public sealed class CollectionsReview96Tests
    {
        // ---- Aggregation.Summarize: the mean ----

        public void Summarize_CopiesOfOneValueAverageToIt()
        {
            foreach (int n in new[] { 3, 1000, 1_000_000 })
            {
                GroupSummary<int> g = Aggregation.Summarize(Enumerable.Repeat(0.1, n), _ => 0, v => v).Single();
                Check.Equal(0.1, g.Mean);
            }
        }

        public void Summarize_TheMeanStaysWithinMinAndMaxAndNearTheExactMean()
        {
            var rng = new Random(961);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = rng.Next(1, 200);
                double centre = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-5, 6));
                double spread = Math.Abs(centre) * Math.Pow(10, -rng.Next(0, 16));
                double[] v = Enumerable.Range(0, n).Select(_ => centre + (rng.NextDouble() - 0.5) * spread).ToArray();
                GroupSummary<int> g = Aggregation.Summarize(v, _ => 0, x => x).Single();
                Check.True(g.Mean >= v.Min() && g.Mean <= v.Max(), $"trial {trial}: mean {g.Mean:R} outside [{v.Min():R}, {v.Max():R}]");
                decimal exact = v.Select(x => (decimal)x).Sum() / n;
                double tol = 1e-13 * Math.Max(Math.Abs(centre), 1e-300) + 4 * spread * 1e-16 * n;
                Check.True(Math.Abs(g.Mean - (double)exact) <= tol, $"trial {trial}: mean {g.Mean:R}, exact {exact}");
            }
        }

        // ---- ReservoirSampler and WeightedWithoutReplacement: a Random that only returns 0 ----

        public void Reservoir_ARandomOfZerosThrowsInsteadOfHanging()
        {
            var sampler = new ReservoirSampler<int>(2, new Zeros(int.MaxValue));
            Check.Throws<InvalidOperationException>(() => { for (int i = 0; i < 100; i++) sampler.Add(i); });
        }

        public void Weighted_ARandomOfZerosThrowsInsteadOfHanging()
        {
            Check.Throws<InvalidOperationException>(() =>
                SamplingPlans.WeightedWithoutReplacement(new[] { 1, 2, 3 }, new[] { 1.0, 2.0, 3.0 }, 2, new Zeros(int.MaxValue)));
        }

        public void Samplers_AFewZeroDrawsAreRedrawn()
        {
            IReadOnlyList<int> picked = SamplingPlans.WeightedWithoutReplacement(new[] { 1, 2, 3 }, new[] { 1.0, 2.0, 3.0 }, 2, new Zeros(63));
            Check.Equal(2, picked.Count);
            var sampler = new ReservoirSampler<int>(2, new Zeros(63));
            for (int i = 0; i < 1000; i++) sampler.Add(i);
            Check.Equal(2, sampler.Sample.Count);
        }

        // Returns 0 for the first `zeros` draws, then a fixed spread of values.
        private sealed class Zeros : Random
        {
            private int _left;
            private int _i;
            public Zeros(int zeros) { _left = zeros; }
            protected override double Sample() => _left > 0 && _left-- > 0 ? 0 : (++_i % 97 + 0.5) / 97.0;
            public override int Next(int maxValue) => (int)(Sample() * maxValue);
            public override int Next(int minValue, int maxValue) => minValue + (int)(Sample() * ((long)maxValue - minValue));
        }

        // ---- MultisetEqualityComparer: counts past 2^32 ----

        public void Multiset_TwoToThe32NullsAreNotEmpty()
        {
            var cmp = new MultisetEqualityComparer<string?>();
            Check.False(cmp.Equals(new Nulls(1L << 32), new string?[0]), "2^32 nulls equalled an empty sequence");
        }

        // A sequence of `count` nulls with no Count, so the comparer must enumerate it.
        private sealed class Nulls : IEnumerable<string?>
        {
            private readonly long _count;
            public Nulls(long count) { _count = count; }
            public IEnumerator<string?> GetEnumerator() => new E(_count);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class E : IEnumerator<string?>
            {
                private long _left;
                public E(long left) { _left = left; }
                public string? Current => null;
                object? IEnumerator.Current => null;
                public bool MoveNext() => _left-- > 0;
                public void Reset() => throw new NotSupportedException();
                public void Dispose() { }
            }
        }

        // ---- CountMinSketch: a null item and a comparer that rejects null ----

        public void CountMinSketch_ANullItemIsCountedWhateverTheComparer()
        {
            var sketch = new CountMinSketch<string?>(64, 4, StringComparer.Ordinal!);
            sketch.Add(null, 3);
            sketch.Add("a");
            Check.True(sketch.Estimate(null) >= 3);
            Check.True(sketch.Estimate("a") >= 1);
            Check.Equal(4L, sketch.TotalCount);
        }

        // ---- Deque and CircularBuffer: Clear costs the items held, not the capacity ----

        public void Deque_ClearingAFewItemsIsCheapAfterManyWereHeld()
        {
            var d = new Deque<int>();
            for (int i = 0; i < 1 << 24; i++) d.PushBack(i);
            d.Clear();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) { d.PushBack(i); d.Clear(); }
            Check.True(sw.ElapsedMilliseconds < 2000, $"2000 clears of one item took {sw.ElapsedMilliseconds} ms");
        }

        public void CircularBuffer_ClearingAFewItemsIsCheap()
        {
            var b = new CircularBuffer<int>(1 << 24);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) { b.Add(i); b.Clear(); }
            Check.True(sw.ElapsedMilliseconds < 2000, $"2000 clears of one item took {sw.ElapsedMilliseconds} ms");
        }

        // Guards: a wrapped Clear still releases every item it held, and the containers work afterwards.
        public void Deque_AWrappedClearReleasesEveryItem()
        {
            var d = new Deque<object>(8);
            WeakReference[] refs = FillWrapped(o => d.PushFront(o), o => d.PushBack(o));
            d.Clear();
            Collect();
            Check.True(refs.All(r => !r.IsAlive), "an item outlived Clear");
            d.PushBack(1); d.PushFront(0);
            Check.Equal("0,1", string.Join(",", d));
        }

        public void CircularBuffer_AWrappedClearReleasesEveryItem()
        {
            var b = new CircularBuffer<object>(8);
            WeakReference[] refs = FillWrapped(o => b.Add(o), o => b.Add(o));
            Check.Equal(8, b.Count);
            b.Clear();
            Collect();
            Check.True(refs.All(r => !r.IsAlive), "an item outlived Clear");
            b.Add(1); b.Add(2);
            Check.Equal("1,2", string.Join(",", b));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] FillWrapped(Action<object> front, Action<object> back)
        {
            var refs = new List<WeakReference>();
            for (int i = 0; i < 12; i++)
            {
                var o = new object();
                refs.Add(new WeakReference(o));
                if (i % 2 == 0) front(o); else back(o);
            }
            return refs.Skip(4).ToArray(); // a CircularBuffer of 8 already dropped the first 4
        }

        private static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // ---- DataTableLite.Row: the default value ----

        public void DataTableRow_TheDefaultHasNoCells()
        {
            DataTableLite.Row row = default;
            Check.Equal(0, row.Count);
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => _ = row[0]);
            Check.Equal("column", ex.ParamName);
            var ex2 = Check.Throws<ArgumentException>(() => _ = row["a"]);
            Check.Equal("column", ex2.ParamName);
        }
    }
}
