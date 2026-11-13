using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>
    /// Regressions from review round 71 (Collections, fourth pass): each test failed before its fix. The version-counter
    /// and Fenwick tests fail only against the overflow-checked build (scripts/checked-build.sh), where the old code
    /// threw OverflowException. TopN's growth cap needs a 2 GB heap, so it was confirmed by a probe instead.
    /// </summary>
    public sealed class CollectionsReview4Tests
    {
        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
            field.SetValue(target, Convert.ChangeType(value, field.FieldType));
        }

        // ---------------- counters past int ----------------

        public void CircularBuffer_VersionWrapsInsteadOfThrowing()
        {
            var buffer = new CircularBuffer<int>(4);
            SetField(buffer, "_version", int.MaxValue);
            buffer.Add(1);
            Check.True(buffer.TryRemoveOldest(out int item));
            Check.Equal(1, item);
            buffer.Add(2);
            buffer.Clear();
            Check.Equal(0, buffer.Count);
        }

        public void Deque_VersionWrapsInsteadOfThrowing()
        {
            var deque = new Deque<int>();
            SetField(deque, "_version", int.MaxValue);
            deque.PushBack(1);
            deque.PushFront(0);
            Check.True(deque.TryPopFront(out int front) && front == 0);
            Check.True(deque.TryPopBack(out int back) && back == 1);
            deque.Clear();
        }

        public void FenwickTree_PrefixOverflowStillGivesExactRanges()
        {
            var tree = new FenwickTree(4);
            tree.Add(0, long.MaxValue);
            tree.Add(1, 5);
            tree.Add(2, 7);
            Check.Equal(5L, tree.RangeSum(1, 1));
            Check.Equal(12L, tree.RangeSum(1, 2));
            Check.Equal(7L, tree.ValueAt(2));
        }

        public void BitSet_CountOfAllIntIndicesIsTwoToThe31()
        {
            var set = new BitSet(64);
            var words = new ulong[1 << 25];   // 2^31 bits, 256 MB
            for (int i = 0; i < words.Length; i++) words[i] = ulong.MaxValue;
            SetField(set, "_words", words);
            Check.Equal(2147483648L, set.Count);
        }

        public void MultiDictionary_TotalPassesIntMaxValue()
        {
            var map = new MultiDictionary<int, int>();
            SetField(map, "_count", int.MaxValue);
            map.Add(1, 1);
            Check.Equal(2147483648L, map.Total);
        }

        // ---------------- sizes past Array.MaxLength ----------------

        public void Constructors_RefuseSizesPastArrayMaxLength()
        {
            var e1 = Check.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<byte>(0x7FFFFFC8));
            Check.Equal("capacity", e1.ParamName);
            var e2 = Check.Throws<ArgumentOutOfRangeException>(() => new Deque<byte>(int.MaxValue));
            Check.Equal("initialCapacity", e2.ParamName);
            var e3 = Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(int.MaxValue, 1));
            Check.Equal("width", e3.ParamName);
        }

        public void CountingBloomFilter_RefusesSlotsPastArrayMaxLength()
        {
            double ln2 = Math.Log(2);
            int n = (int)(0x7FFFFFC8 * ln2 * ln2 / -Math.Log(0.01)) - 2;
            while (Math.Ceiling(-n * Math.Log(0.01) / (ln2 * ln2)) <= 0x7FFFFFC7) n++;
            Check.True(Math.Ceiling(-n * Math.Log(0.01) / (ln2 * ln2)) <= int.MaxValue);
            var e = Check.Throws<ArgumentOutOfRangeException>(() => new CountingBloomFilter(n, 0.01));
            Check.Equal("expectedItems", e.ParamName);
        }

        // ---------------- Bloom filters ----------------

        private static double FalsePositiveRate(Action<byte[]> add, Func<byte[], bool> mightContain, int n, int seed)
        {
            var random = new Random(seed);
            byte[] Key() { var b = new byte[16]; random.NextBytes(b); return b; }
            for (int i = 0; i < n; i++) add(Key());
            int hits = 0;
            const int probes = 100_000;
            for (int i = 0; i < probes; i++) if (mightContain(Key())) hits++;
            return (double)hits / probes;
        }

        // Averaged over several filters: one item's probes cycled through a handful of slots in a 10-bit filter.
        private static double MeanRate(Func<(Action<byte[]>, Func<byte[], bool>)> make, int n)
        {
            double sum = 0;
            const int filters = 40;
            for (int f = 0; f < filters; f++)
            {
                var (add, contains) = make();
                sum += FalsePositiveRate(add, contains, n, 1000 + f);
            }
            return sum / filters;
        }

        public void BloomFilter_SmallFiltersMeetTheirTarget()
        {
            foreach (var (n, p) in new[] { (1, 0.01), (5, 0.01), (1, 0.001), (3, 0.05) })
            {
                double rate = MeanRate(() => { var b = new BloomFilter(n, p); return (b.Add, b.MightContain); }, n);
                Check.True(rate < p * 2.5, $"n={n} p={p}: {rate}");
            }
        }

        public void CountingBloomFilter_SmallFiltersMeetTheirTarget()
        {
            foreach (var (n, p) in new[] { (1, 0.01), (5, 0.01), (1, 0.001), (3, 0.05) })
            {
                double rate = MeanRate(() => { var b = new CountingBloomFilter(n, p); return (b.Add, b.MightContain); }, n);
                Check.True(rate < p * 2.5, $"n={n} p={p}: {rate}");
            }
        }

        public void BloomFilter_LoneSurrogatesAreDistinctItems()
        {
            var filter = new BloomFilter(1000, 1e-9);
            filter.Add("key\uD800");
            Check.True(filter.MightContain("key\uD800"));
            Check.False(filter.MightContain("key\uDBFF"));
            Check.False(filter.MightContain("key�"));
            Check.False(filter.MightContain("key\uDC00"));
            // Well-formed text keeps its UTF-8 bytes.
            var bytes = new BloomFilter(1000, 1e-9);
            bytes.Add(System.Text.Encoding.UTF8.GetBytes("café 😀"));
            Check.True(bytes.MightContain("café 😀"));
        }

        public void CountingBloomFilter_RemovingOneLoneSurrogateKeepsAnother()
        {
            var filter = new CountingBloomFilter(1000, 1e-9);
            filter.Add("a\uD800");
            Check.False(filter.Remove("a\uDFFF"));
            Check.True(filter.MightContain("a\uD800"));
            Check.False(filter.MightContain("a�"));
            Check.True(filter.Remove("a\uD800"));
            Check.False(filter.MightContain("a\uD800"));
        }

        // ---------------- Aggregation ----------------

        public void Summarize_RescaledSumIsTheTrueSum()
        {
            var g = Aggregation.Summarize(new[] { 1e308, 1e308, -1e308 }, _ => 0, v => v).Single();
            Check.Equal(1e308, g.Sum);
            Check.Close(1e308 / 3, g.Mean, 1e293);
        }

        // Differential: tiny values against the same statistics on values scaled up by 2^700 (exact), then scaled back.
        public void Summarize_TinyValuesKeepTheirSpread()
        {
            var random = new Random(71);
            double up = Math.Pow(2, 700);
            for (int trial = 0; trial < 300; trial++)
            {
                int exponent = -1014 + random.Next(764);   // 2^-1014 … 2^-251: squares stay finite once scaled by 2^700
                int count = 2 + random.Next(6);
                var values = new double[count];
                for (int i = 0; i < count; i++) values[i] = (random.NextDouble() * 2 - 1) * Math.Pow(2, exponent);
                var g = Aggregation.Summarize(values, _ => 0, v => v).Single();
                double[] scaled = values.Select(v => v * up).ToArray();
                double mean = scaled.Average();
                double sd = Math.Sqrt(scaled.Sum(v => (v - mean) * (v - mean)) / (count - 1));
                double expectSd = sd / up, expectMean = mean / up;
                Check.True(Math.Abs(g.StandardDeviation - expectSd) <= 1e-9 * expectSd + 1e-320, $"sd {g.StandardDeviation} vs {expectSd} at 2^{exponent}");
                Check.True(Math.Abs(g.Mean - expectMean) <= 1e-9 * Math.Abs(expectMean) + 1e-320, $"mean {g.Mean} vs {expectMean} at 2^{exponent}");
            }
            var pair = Aggregation.Summarize(new[] { 1e-200, 2e-200 }, _ => 0, v => v).Single();
            Check.Close(1e-200 / Math.Sqrt(2), pair.StandardDeviation, 1e-212);
        }

        // ---------------- DataTableLite ----------------

        private static IEnumerable<object?> ReadsAtMost(int limit)
        {
            for (int i = 0; ; i++)
            {
                if (i >= limit) throw new InvalidOperationException("The sequence was read past the column count.");
                yield return i;
            }
        }

        public void AddRow_StopsReadingOneCellPastTheColumns()
        {
            var table = new DataTableLite(new[] { "a", "b" });
            var e = Check.Throws<ArgumentException>(() => table.AddRow(ReadsAtMost(3)));
            Check.Equal("values", e.ParamName);
            table.AddRow((IEnumerable<object?>)new object?[] { 1, 2 });
            Check.Equal(1, table.RowCount);
        }

        public void InnerJoin_ManyColumnsIsNotQuadratic()
        {
            const int columns = 40_000;
            var left = new DataTableLite(Enumerable.Range(0, columns).Select(i => "L" + i));
            var right = new DataTableLite(Enumerable.Range(0, columns).Select(i => "R" + i));
            left.AddRow(Enumerable.Range(0, columns).Select(i => (object?)i));
            right.AddRow(Enumerable.Range(0, columns).Select(i => (object?)i));
            var watch = Stopwatch.StartNew();
            var joined = left.InnerJoin(right, "L0", "R0");
            watch.Stop();
            Check.Equal(2 * columns, joined.ColumnCount);
            Check.True(watch.Elapsed.TotalSeconds < 2, $"{watch.Elapsed.TotalSeconds} s");
            var dup = new DataTableLite(new[] { "k", "x" });
            var other = new DataTableLite(new[] { "k2", "k2x" });
            Check.Throws<ArgumentException>(() => dup.InnerJoin(new DataTableLite(new[] { "y", "x" }), "k", "y"));
            Check.Equal(4, dup.InnerJoin(other, "k", "k2").ColumnCount);
        }

        public void OrderBy_StringsCompareOrdinally()
        {
            var table = new DataTableLite(new[] { "s" });
            foreach (string s in new[] { "b", "B", "a", "A", "é", "e" }) table.AddRow(s);
            var expected = new[] { "b", "B", "a", "A", "é", "e" }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var sorted = table.OrderBy("s");
            Check.Equal(string.Join(",", expected), string.Join(",", Enumerable.Range(0, 6).Select(i => (string)sorted[i, 0]!)));
        }

        private sealed class NoNulls : IComparer<object?>
        {
            public int Compare(object? x, object? y)
            {
                if (x is null || y is null) throw new InvalidOperationException("The comparer was handed a null cell.");
                return string.CompareOrdinal((string)x, (string)y);
            }
        }

        public void OrderBy_CallerComparerNeverSeesNulls()
        {
            var table = new DataTableLite(new[] { "s" });
            foreach (string? s in new[] { "b", null, "a", null, "c" }) table.AddRow(s);
            var up = table.OrderBy("s", new NoNulls());
            Check.Equal("<null>,<null>,a,b,c", string.Join(",", Enumerable.Range(0, 5).Select(i => (string?)up[i, 0] ?? "<null>")));
            var down = table.OrderByDescending("s", new NoNulls());
            Check.Equal("c,b,a,<null>,<null>", string.Join(",", Enumerable.Range(0, 5).Select(i => (string?)down[i, 0] ?? "<null>")));
        }

        // ---------------- Trie ----------------

        public void Trie_RemovingAnAbsentLongKeyAllocatesNothingLarge()
        {
            var trie = new Trie();
            trie.Add("ab");
            string absent = new string('a', 10_000_000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Check.False(trie.Remove(absent));
            Check.False(trie.Remove("a"));     // a prefix, not a word
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 1_000_000, $"{allocated} bytes");
            Check.True(trie.Remove("ab"));
            Check.Equal(0, trie.Count);
        }

        // ---------------- BitSet enumeration ----------------

        public void BitSet_ModifiedDuringEnumerationThrows()
        {
            foreach (Action<BitSet> mutate in new Action<BitSet>[]
            {
                s => s.Set(500), s => s.Clear(3), s => s.UnionWith(new BitSet()), s => s.IntersectWith(new BitSet()),
                s => s.ExceptWith(new BitSet()), s => s.SymmetricExceptWith(new BitSet()),
            })
            {
                var set = new BitSet();
                set.Set(1); set.Set(3); set.Set(70);
                Check.Throws<InvalidOperationException>(() => { foreach (int bit in set.EnumerateSetBits()) mutate(set); });
            }
            var plain = new BitSet();
            plain.Set(1); plain.Set(64);
            Check.Equal("1,64", string.Join(",", plain.EnumerateSetBits()));
        }

        // ---------------- NearestBy ----------------

        private static int NaiveNearest(double[] keys, double key)
        {
            int best = -1;
            for (int i = 0; i < keys.Length; i++)
                if (best < 0 || Math.Abs(keys[i] - key) < Math.Abs(keys[best] - key)) best = i;
            return best;
        }

        public void NearestBy_DuplicateKeysGiveTheFirstOfTheRun()
        {
            Check.Equal(0, new[] { 1.0, 1, 1, 3 }.NearestBy(2.0, x => x));
            Check.Equal(0, new[] { 1.0, 1, 1, 3 }.NearestBy(1.5, x => x));
            Check.Equal(1, new[] { 0.0, 5, 5 }.NearestBy(6.0, x => x));
            var random = new Random(7);
            for (int trial = 0; trial < 2000; trial++)
            {
                var keys = Enumerable.Range(0, 1 + random.Next(8)).Select(_ => (double)random.Next(6)).OrderBy(x => x).ToArray();
                double key = random.Next(-2, 16) / 2.0;
                int got = keys.NearestBy(key, x => x);
                int naive = NaiveNearest(keys, key);
                // Ties between a lower and an upper neighbour go to the lower one, which the naive scan also picks first.
                Check.Equal(naive, got, $"[{string.Join(",", keys)}] near {key}");
            }
        }

        public void NearestBy_NullListIsReportedBeforeTheKey()
        {
            var e = Check.Throws<ArgumentNullException>(() => ((IReadOnlyList<double>)null!).NearestBy(double.NaN, x => x));
            Check.Equal("list", e.ParamName);
        }

        // ---------------- weighted sampling ----------------

        public void WeightedWithoutReplacement_HugeAndTinyWeightsKeepTheirRatio()
        {
            var random = new Random(71);
            var weights = new[] { 1e308, 4e-300, 1e-300 };
            var items = new[] { 0, 1, 2 };
            int ones = 0, twos = 0;
            const int trials = 20_000;
            for (int t = 0; t < trials; t++)
            {
                var pick = SamplingPlans.WeightedWithoutReplacement(items, weights, 2, random);
                Check.True(pick.Contains(0));
                if (pick.Contains(1)) ones++;
                if (pick.Contains(2)) twos++;
            }
            Check.Equal(trials, ones + twos);
            double share = (double)ones / trials;
            Check.True(share > 0.78 && share < 0.82, $"{share}");
        }

        // ---------------- sequence comparers ----------------

        private sealed class NullIsEmpty : IEqualityComparer<string?>
        {
            public bool Equals(string? x, string? y) => (x ?? "") == (y ?? "");
            public int GetHashCode(string? s) => (s ?? "").GetHashCode();
        }

        public void SequenceEqualityComparer_NullElementEqualsOnlyNull()
        {
            var comparer = new SequenceEqualityComparer<string?>(new NullIsEmpty());
            Check.False(comparer.Equals(new[] { null, "x" }, new[] { "", "x" }));
            Check.True(comparer.Equals(new[] { null, "x" }, new string?[] { null, "x" }));
            var random = new Random(3);
            var pool = new[] { null, "", "x", "y" };
            for (int trial = 0; trial < 2000; trial++)
            {
                var a = Enumerable.Range(0, random.Next(4)).Select(_ => pool[random.Next(4)]).ToArray();
                var b = Enumerable.Range(0, random.Next(4)).Select(_ => pool[random.Next(4)]).ToArray();
                if (comparer.Equals(a, b)) Check.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
            }
        }
    }
}
