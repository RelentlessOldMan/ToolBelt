using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    // Round 90 review regressions (Collections, fifth pass): each test failed before its fix (some only on .NET
    // Framework 4.8, where the runtime's hashing or formatting differs).
    public sealed class CollectionsReview90Tests
    {
        public void MultisetHash_IntSubsetsWithOneSumSpread()
        {
            // The raw element hashes were summed: the 3-subsets of 0..59 (34,220) shared about 170 hashes.
            var cmp = MultisetEqualityComparer<int>.Default;
            var hashes = new HashSet<int>();
            int total = 0;
            for (int a = 0; a < 60; a++)
                for (int b = a + 1; b < 60; b++)
                    for (int c = b + 1; c < 60; c++) { hashes.Add(cmp.GetHashCode(new[] { a, b, c })); total++; }
            Check.True(hashes.Count > total * 99 / 100, $"{hashes.Count} hashes for {total} multisets");
            Check.Equal(cmp.GetHashCode(new[] { 3, 1, 2, 1 }), cmp.GetHashCode(new[] { 1, 1, 2, 3 }));
            Check.True(cmp.Equals(new[] { 3, 1, 2, 1 }, new List<int> { 1, 1, 2, 3 }));
            Check.False(cmp.Equals(new[] { 1, 2 }, new[] { 1, 2, 2 }));
        }

        public void SequenceHash_LinearlyRelatedElementsSpread()
        {
            // hash*31 + h: every [i, -31i] had one hash.
            var cmp = SequenceEqualityComparer<int>.Default;
            var hashes = new HashSet<int>();
            for (int i = 0; i < 20000; i++) hashes.Add(cmp.GetHashCode(new[] { i, -31 * i }));
            Check.True(hashes.Count > 19900, $"{hashes.Count} hashes");
            Check.Equal(cmp.GetHashCode(new[] { 1, 2 }), cmp.GetHashCode(new List<int> { 1, 2 }));
        }

        public void DataTableLite_DistinctOnCraftedRowsIsFast()
        {
            // The row hash was hash*31 + h too: 40,000 rows of (i, -31i) took 8 s.
            var t = new DataTableLite(new[] { "a", "b" });
            for (int i = 0; i < 40000; i++) t.AddRow(i, -31 * i);
            var sw = Stopwatch.StartNew();
            Check.Equal(40000, t.Distinct().RowCount);
            Check.True(sw.Elapsed.TotalSeconds < 1.5, $"took {sw.Elapsed.TotalSeconds:F1} s");
        }

        public void EqualNaNsAndDecimalsAreOneKeyOnEveryRuntime()
        {
            // .NET Framework hashed a NaN by its bits and a decimal by its scale.
            double nan1 = double.NaN, nan2 = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000123UL));
            decimal d1 = 16664282.42484238642m, d2 = 16664282.42484238642000m;
            Check.True(nan1.Equals(nan2) && d1 == d2);
            // Through double these two round apart, so a hash by (double)d splits them too.
            decimal d3 = 1.1000000000000000000000000000m, d4 = 1.1m;

            var t = new DataTableLite(new[] { "k" });
            t.AddRow(nan1).AddRow(nan2).AddRow(d1).AddRow(d2);
            Check.Equal(2, t.GroupBy("k").Count);
            Check.Equal(2, t.Distinct().RowCount);
            var right = new DataTableLite(new[] { "r" }).AddRow(nan2).AddRow(d2);
            Check.Equal(4, t.InnerJoin(right, "k", "r").RowCount);   // every left row finds its one match

            var counter = new Counter<double>();
            counter.Add(nan1); counter.Add(nan2);
            Check.Equal(1, counter.Count);
            var set = new OrderedSet<decimal> { d1, d2, d3, d4, 0m, -0m };
            Check.Equal(3, set.Count);
            var cells = new DataTableLite(new[] { "k" }).AddRow(d3).AddRow(d4).AddRow(0m).AddRow(-0m);
            Check.Equal(2, cells.GroupBy("k").Count);
            var od = new OrderedDictionary<double?, int>();
            od[nan1] = 1; od[nan2] = 2;
            Check.Equal(1, od.Count);
            Check.Equal(1, Aggregation.Summarize(new[] { d1, d2 }, v => v, _ => 1.0).Count);
            Check.Equal(1, Aggregation.Pivot(new[] { nan1, nan2 }, v => v, v => v, _ => 1.0, xs => xs.Count).Count);
            Check.Equal(1, SequenceEqualityComparer<double>.Default.GetHashCode(new[] { nan1 }) == SequenceEqualityComparer<double>.Default.GetHashCode(new[] { nan2 }) ? 1 : 0);
        }

        public void DataTableLite_ColumnsCannotBeCastBackAndRenamed()
        {
            var t = new DataTableLite(new[] { "a", "b" });
            Check.False(t.Columns is string[]);
            Check.Throws<NotSupportedException>(() => ((IList<string>)t.Columns)[0] = "zzz");
            Check.Equal("a", t.Columns[0]);
        }

        public void CountMinSketch_DepthPastArrayMaxLengthIsAnArgumentError()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(1, int.MaxValue));
            Check.Equal("depth", ex.ParamName);
        }

        public void BitSet_ChangeBeforeTheFirstMoveNextThrows()
        {
            var bits = new BitSet();
            bits.Set(1);
            using var e = bits.EnumerateSetBits().GetEnumerator();
            bits.Set(5);
            Check.Throws<InvalidOperationException>(() => e.MoveNext());
            Check.Equal("1,5", string.Join(",", bits.EnumerateSetBits()));
        }

        public void TtlCache_RemoveOfAnExpiredEntryReturnsFalse()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var cache = new TtlCache<string, int>(TimeSpan.FromMinutes(1), () => now);
            cache.Set("a", 1);
            cache.Set("b", 2);
            now = now.AddMinutes(2);
            Check.False(cache.Remove("a"));
            cache.Set("b", 3);
            Check.True(cache.Remove("b"));
            Check.False(cache.Remove("b"));
        }

        public void TtlCache_ExpiryNearTheEndOfTheCalendarUsesUtc()
        {
            // Local 9999-12-31 22:59:59 +14:00 is UTC 08:59:59: a 2 h TTL fits, but the local time made it "never".
            var now = new DateTimeOffset(9999, 12, 31, 22, 59, 59, TimeSpan.FromHours(14));
            var cache = new TtlCache<string, int>(TimeSpan.FromHours(2), () => now);
            cache.Set("k", 1);
            now = new DateTimeOffset(now.UtcDateTime.AddHours(3), TimeSpan.Zero);
            Check.False(cache.TryGet("k", out _));
            // Past the end in UTC still saturates.
            now = new DateTimeOffset(9999, 12, 31, 23, 0, 0, TimeSpan.Zero);
            cache.Set("m", 1);
            now = new DateTimeOffset(9999, 12, 31, 23, 59, 0, TimeSpan.Zero);
            Check.True(cache.TryGet("m", out _));
        }

        public void TtlCache_ClearLowersThePruneThreshold()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var cache = new TtlCache<int, int>(TimeSpan.FromSeconds(1), () => now);
            for (int i = 0; i < 100000; i++) cache.Set(i, i);
            cache.Clear();
            // Write-once keys, each expired before the next: a fresh cache holds at most its minimum threshold.
            for (int i = 0; i < 100000; i++) { cache.Set(-1 - i, i); now = now.AddSeconds(2); }
            var entries = (System.Collections.ICollection)typeof(TtlCache<int, int>)
                .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
            Check.True(entries.Count <= 128, $"{entries.Count} entries held");
        }

        public void SelectorsReturningNullAreArgumentErrors()
        {
            var items = new[] { "a", null };
            var ex = Check.Throws<ArgumentException>(() => Aggregation.Summarize(items, s => s, _ => 1.0));
            Check.Equal("keySelector", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => Aggregation.Pivot(items, s => s, _ => "c", _ => 1.0, xs => xs.Count));
            Check.Equal("rowSelector", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => Aggregation.Pivot(items, _ => "r", s => s, _ => 1.0, xs => xs.Count));
            Check.Equal("colSelector", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => SamplingPlans.Stratified(items, s => s, 1, new Random(1)));
            Check.Equal("stratum", ex.ParamName);
        }

        private sealed class Key
        {
            public readonly int Id;
            public readonly string Tag;
            public Key(int id, string tag) { Id = id; Tag = tag; }
            public override bool Equals(object? obj) => obj is Key k && k.Id == Id;
            public override int GetHashCode() => Id;
        }

        public void UpdatesKeepTheFirstKeyAsDictionaryDoes()
        {
            Key first = new Key(1, "first"), second = new Key(1, "second");
            var od = new OrderedDictionary<Key, int>();
            od[first] = 1;
            od[second] = 2;
            Check.Equal("first", od.Keys[0].Tag);
            Check.Equal("first", od.First().Key.Tag);
            var lru = new LruCache<Key, int>(4);
            lru.Set(first, 1);
            lru.Set(second, 2);
            Check.Equal("first", lru.Keys[0].Tag);
        }

        public void DataTableLite_ToStringRoundTripsDoubles()
        {
            // .NET Framework's default wrote 15 digits: 0.30000000000000004 and 0.3 looked alike.
            var t = new DataTableLite(new[] { "v" }).AddRow(0.1 + 0.2).AddRow(0.3).AddRow(0.1f + 0.2f);
            string text = t.ToString();
            Check.True(text.Contains("0.30000000000000004"), text);
            Check.True(text.Split('\n').Any(line => line.Trim() == "0.3"), text);
        }

        public void Summarize_MatchesANaiveReference()
        {
            var rng = new Random(9001);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = 1 + rng.Next(30);
                double magnitude = Math.Pow(10, rng.Next(-140, 300));   // below, the naive squares underflow
                var values = Enumerable.Range(0, n).Select(_ => (rng.NextDouble() * 2 - 1) * magnitude).ToArray();
                var g = Aggregation.Summarize(values, _ => 0, v => v).Single();
                double mean = values.Average();
                double sd = n > 1 ? Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (n - 1)) : 0;
                Check.Equal(n, g.Count);
                Check.Equal(values.Min(), g.Min);
                Check.Equal(values.Max(), g.Max);
                Check.True(Math.Abs(g.Mean - mean) <= 1e-12 * values.Max(Math.Abs), $"mean {g.Mean} vs {mean}");
                Check.True(Math.Abs(g.StandardDeviation - sd) <= 1e-10 * sd + 1e-300 * magnitude, $"sd {g.StandardDeviation} vs {sd}");
            }
            var inf = Aggregation.Summarize(new[] { 1.0, double.PositiveInfinity }, _ => 0, v => v).Single();
            Check.True(double.IsNaN(inf.StandardDeviation) && double.IsPositiveInfinity(inf.Mean));
            Check.Equal(0.0, Aggregation.Summarize(new[] { double.PositiveInfinity }, _ => 0, v => v).Single().StandardDeviation);
        }

#if NETCOREAPP
        public void Summarize_DoesNotKeepEveryValue()
        {
            // Every value of every group was kept: 20 million items held 384 MB.
            long before = GC.GetAllocatedBytesForCurrentThread();
            var g = Aggregation.Summarize(Ones(2000000), _ => 0, v => v).Single();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(2000000, g.Count);
            Check.Equal(2000000.0, g.Sum);
            Check.True(allocated < 1 << 20, $"allocated {allocated} bytes");
        }

        private static IEnumerable<double> Ones(int count)
        {
            for (int i = 0; i < count; i++) yield return 1.0;
        }

        public void Trie_ALongKeyCostsTensOfBytesACharacter()
        {
            // A dictionary per node retained about 250 bytes a character.
            string key = new string('x', 1000000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var trie = new Trie();
            trie.Add(key);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 80L * key.Length, $"allocated {allocated} bytes");
            Check.True(trie.Contains(key));
            trie.Add("xy");
            trie.Add("xz");
            Check.Equal("xy,xz", string.Join(",", trie.WithPrefix("x").Where(k => k.Length < 3)));
            Check.True(trie.Remove("xy"));
            Check.True(trie.StartsWith("xz") && !trie.StartsWith("xy"));
            Check.True(trie.Remove(key));
            Check.Equal("xz", string.Join(",", trie.WithPrefix("")));
        }
#endif
    }
}
