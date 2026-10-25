using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    /// <summary>Regressions from review round 9 (Collections): each test failed before its fix.</summary>
    public sealed class CollectionsReviewTests
    {
        // ---------------- CartesianProduct ----------------

        public void CartesianProduct_ListOfListsIsTheProductOfTheInnerLists()
        {
            var lists = new List<List<int>> { new List<int> { 1, 2 }, new List<int> { 3, 4 } };
            var product = CartesianProduct.Of(lists).ToList();
            Check.Equal(4, product.Count);
            Check.Equal("13 14 23 24", string.Join(" ", product.Select(t => Flat(t))));

            var arrays = new List<int[]> { new[] { 1, 2 }, new[] { 3 } };
            Check.Equal("13 23", string.Join(" ", CartesianProduct.Of(arrays).Select(t => Flat(t))));
        }

        // Non-generic, so this compiles whichever overload Of binds to.
        private static string Flat(System.Collections.IEnumerable tuple)
        {
            var sb = new System.Text.StringBuilder();
            foreach (object? o in tuple) sb.Append(o);
            return sb.ToString();
        }

        // ---------------- Sampling ----------------

        public void WeightedPick_RejectsInfiniteWeights()
        {
            var e = Check.Throws<ArgumentException>(() => Sampling.WeightedPickIndex(new[] { double.PositiveInfinity, 1, 0 }, new Random(1)));
            Check.Equal("weights", e.ParamName);
        }

        public void WeightedPick_HugeWeightsWhoseSumOverflowsStillPickInProportion()
        {
            var rng = new Random(7);
            var counts = new int[3];
            for (int i = 0; i < 2000; i++) counts[Sampling.WeightedPickIndex(new[] { double.MaxValue, double.MaxValue, 0 }, rng)]++;
            Check.Equal(0, counts[2], "a zero weight was picked");
            Check.True(counts[0] > 800 && counts[1] > 800, $"{counts[0]}/{counts[1]}");
        }

        public void WeightedWithoutReplacement_TinyWeightsKeepTheirProportions()
        {
            var rng = new Random(3);
            var items = new[] { 0, 1, 2 };
            var counts = new int[3];
            for (int i = 0; i < 20000; i++)
                counts[SamplingPlans.WeightedWithoutReplacement(items, new[] { 1e-310, 1e-310, 2e-310 }, 1, rng)[0]]++;
            Check.True(counts[2] > 9000 && counts[2] < 11000, $"first picks {counts[0]}/{counts[1]}/{counts[2]}");
            Check.True(counts[0] > 4000 && counts[1] > 4000, $"first picks {counts[0]}/{counts[1]}/{counts[2]}");
        }

        // ---------------- CountMinSketch ----------------

        public void CountMinSketch_KeysDifferingOnlyInHighBitsDoNotCollideInEveryRow()
        {
            var sketch = new CountMinSketch<int>();
            sketch.Add(0, 1000);
            for (int k = 1; k < 128; k++)
                Check.True(sketch.Estimate(k << 24) < 1000, $"key {k << 24} collides with 0 in every row");
        }

        // ---------------- Deque / CircularBuffer enumerators ----------------

        public void Deque_ModifyingDuringEnumerationThrows()
        {
            var d = new Deque<int>();
            d.PushBack(1);
            int seen = 0;
            Check.Throws<InvalidOperationException>(() =>
            {
                foreach (int x in d) { d.PushBack(x); if (++seen > 100) break; }
            });
        }

        public void CircularBuffer_ModifyingDuringEnumerationThrows()
        {
            var b = new CircularBuffer<int>(3);
            b.Add(1); b.Add(2); b.Add(3);
            int seen = 0;
            Check.Throws<InvalidOperationException>(() =>
            {
                foreach (int x in b) { b.Add(x + 10); if (++seen > 100) break; }
            });
        }

        // ---------------- Trie ----------------

        public void Trie_VeryLongKeysDoNotOverflowTheStack()
        {
            var trie = new Trie();
            string key = new string('a', 200_000);
            trie.Add(key);
            trie.Add("ab");
            var all = trie.WithPrefix("");
            Check.Equal(2, all.Count);
            Check.Equal(key, all[0]);
            Check.Equal("ab", all[1]);
            Check.True(trie.Remove(key));
            Check.False(trie.Contains(key));
            Check.True(trie.Contains("ab"));
            Check.False(trie.StartsWith("aa"));                        // the long branch was pruned
        }

        public void Trie_IterativeWalksMatchASortedSet()
        {
            var rng = new Random(11);
            var trie = new Trie();
            var model = new SortedSet<string>(StringComparer.Ordinal);
            for (int op = 0; op < 5000; op++)
            {
                var chars = new char[rng.Next(0, 6)];
                for (int i = 0; i < chars.Length; i++) chars[i] = (char)('a' + rng.Next(3));
                string k = new string(chars);
                if (rng.Next(3) == 0) Check.Equal(model.Remove(k), trie.Remove(k), $"remove {k}");
                else { trie.Add(k); model.Add(k); }
                if (op % 100 == 0)
                {
                    string prefix = new string('a', rng.Next(0, 3));
                    Check.Equal(string.Join(",", model.Where(s => s.StartsWith(prefix, StringComparison.Ordinal))),
                                string.Join(",", trie.WithPrefix(prefix)), $"prefix '{prefix}'");
                }
            }
            Check.Equal(model.Count, trie.Count);
        }

        // ---------------- Combinatorics ----------------

        public void Permutations_KeepTheSwapOrder()
        {
            Check.Equal("123 132 213 231 321 312", string.Join(" ", Combinatorics.Permutations(new[] { 1, 2, 3 }).Select(p => string.Concat(p))));
            Check.Equal("ab ba", string.Join(" ", Combinatorics.Permutations(new[] { 'a', 'b' }).Select(p => new string(p))));
        }

        public void Permutations_EnumerationsAreIndependent()
        {
            var p = Combinatorics.Permutations(new[] { 1, 2, 3 });
            var pairs = p.Zip(p, (a, b) => string.Concat(a) + "/" + string.Concat(b)).ToList();
            Check.True(pairs.All(s => s.Substring(0, 3) == s.Substring(4)), string.Join(" ", pairs));

            var q = Combinatorics.Permutations(new[] { 1, 2, 3, 4 });
            using (var e = q.GetEnumerator()) { e.MoveNext(); e.MoveNext(); e.MoveNext(); }   // abandoned part-way
            Check.Equal("1234", string.Concat(q.First()));
        }

        public void Permutations_FirstOfALargeListDoesNotOverflowTheStack()
        {
            int[] items = Enumerable.Range(0, 200_000).ToArray();
            int[] first = Combinatorics.Permutations(items).First();
            Check.True(first.SequenceEqual(items));
        }

        // ---------------- TtlCache ----------------

        public void TtlCache_NeverExpiringTtl()
        {
            var cache = new TtlCache<string, int>(TimeSpan.MaxValue);
            cache.Set("a", 1);
            Check.True(cache.TryGet("a", out int v));
            Check.Equal(1, v);
        }

        public void TtlCache_ExpiredEntriesDoNotAccumulate()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var cache = new TtlCache<int, int>(TimeSpan.FromSeconds(1), () => now);
            for (int i = 0; i < 100_000; i++)
            {
                cache.Set(i, i);
                now = now.AddSeconds(2);                               // everything written so far has expired
            }
            var field = typeof(TtlCache<int, int>).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!;
            int stored = ((System.Collections.ICollection)field.GetValue(cache)!).Count;
            Check.True(stored < 1000, $"{stored} expired entries retained");
        }

        // ---------------- Pre-sized storage ----------------

        public void HugeCapacitiesDoNotPreallocate()
        {
            var lru = new LruCache<int, int>(int.MaxValue);
            lru.Set(1, 1);
            Check.Equal(1, lru.Count);

            var batch = new[] { 1, 2, 3 }.Batch(int.MaxValue).First();
            Check.Equal(3, batch.Count);

            var top = new TopN<int>(int.MaxValue);
            top.Add(5); top.Add(9);
            Check.Equal(int.MaxValue, top.Capacity);
            Check.Equal(2, top.Count);
        }

        public void FenwickTree_SizeTooLargeIsAnArgumentError()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => new FenwickTree(int.MaxValue));
            Check.Equal("size", e.ParamName);
        }

        // ---------------- SortedListExtensions ----------------

        public void RangeSorted_NullListThrowsAtCallTime()
        {
            Check.Throws<ArgumentNullException>(() => ((IReadOnlyList<int>)null!).RangeSorted(1, 2));
        }

        public void NearestBy_NaNKeyThrows()
        {
            var list = new[] { 1.0, 2.0, 3.0 };
            Check.Throws<ArgumentException>(() => list.NearestBy(double.NaN, x => x));
        }

        // ---------------- Aggregation ----------------

        public void Summarize_NaNPropagatesToMinAndMax()
        {
            var s = Aggregation.Summarize(new[] { 1.0, double.NaN, 3.0 }, _ => "g", v => v)[0];
            Check.True(double.IsNaN(s.Mean) && double.IsNaN(s.Min) && double.IsNaN(s.Max), $"min {s.Min} max {s.Max} mean {s.Mean}");
        }

        // ---------------- ObjectPool ----------------

        public void ObjectPool_ReturningTheSameInstanceTwiceKeepsItOnce()
        {
            int resets = 0;
            var pool = new ObjectPool<object>(() => new object(), _ => resets++, maxRetained: 1);
            var o = pool.Rent();
            pool.Return(o);
            pool.Return(o);
            Check.Equal(1, pool.Count);
            Check.False(ReferenceEquals(pool.Rent(), pool.Rent()), "two renters share one instance");

            var other = new object();
            pool.Return(new object());
            resets = 0;
            pool.Return(other);                                        // pool full: dropped, so not reset
            Check.Equal(0, resets);
        }

        public void ObjectPool_NullFromTheFactoryThrows()
        {
            var pool = new ObjectPool<object>(() => null!);
            Check.Throws<InvalidOperationException>(() => pool.Rent());
        }

        // ---------------- ReservoirSampler ----------------

        public void ReservoirSampler_SampleCannotBeMutatedThroughACast()
        {
            var r = new ReservoirSampler<int>(3, new Random(1));
            r.Add(1);
            Check.False(r.Sample is List<int>, "Sample exposes the internal List<T>");
        }

        // ---------------- DataTableLite ----------------

        public void DataTableLite_ToStringUsesTheInvariantCulture()
        {
            var t = new DataTableLite(new[] { "x" }).AddRow(1.5);
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                string s = t.ToString();
                Check.True(s.Contains("1.5") && !s.Contains("1,5"), s);
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        // ---------------- Drop-in law ----------------

        public void SketchesDoNotReferenceBloomFilterInDocs()
        {
            foreach (string file in new[] { "CountingBloomFilter.cs", "CountMinSketch.cs" })
            {
                string src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ToolBelt", "Collections", file);
                if (!File.Exists(src)) continue;
                Check.False(File.ReadAllText(src).Contains("cref=\"BloomFilter\""), $"{file} has a cref to BloomFilter but claims to be self-contained");
            }
        }
    }
}
