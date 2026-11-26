using System;
using System.Collections.Generic;
using System.Reflection;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    // Round 220 (collections, thirteenth pass): each regression failed on the code before its fix.
    public sealed class CollectionsReview220Tests
    {
        // ---- LruCache ----

        // A key whose hash sets another key in the cache (once per outer call).
        private sealed class ReKey
        {
            public static LruCache<ReKey, int>? Cache;
            [ThreadStatic] private static int t_depth;
            public readonly int V;
            public ReKey(int v) { V = v; }
            public override bool Equals(object? o) => o is ReKey k && k.V == V;
            public override int GetHashCode()
            {
                if (Cache != null && t_depth == 0)
                {
                    t_depth++;
                    try { Cache.Set(new ReKey(100 + V), 0); }
                    finally { t_depth--; }
                }
                return V;
            }
        }

        // The eviction's hash of the least-recently-used key re-entered Set, which evicted that node itself; RemoveLast
        // then took a different node off the list, leaving its key in the map for good: Count 3 with Capacity 2, the
        // list and the map disagreeing.
        public void LruCache_AKeyWhoseHashReentersTheCache_KeepsItConsistent()
        {
            var cache = new LruCache<ReKey, int>(2);
            cache.Set(new ReKey(1), 1);
            cache.Set(new ReKey(2), 2);
            ReKey.Cache = cache;
            try
            {
                for (int i = 3; i < 12; i++) cache.Set(new ReKey(i), i);
            }
            finally { ReKey.Cache = null; }
            Check.True(cache.Count <= cache.Capacity, "count " + cache.Count + " over capacity " + cache.Capacity);
            var keys = new List<ReKey>(cache.Keys);
            Check.Equal(cache.Count, keys.Count);
            foreach (ReKey k in keys)
            {
                Check.True(cache.ContainsKey(k), "listed key " + k.V + " is not in the map");
                Check.True(cache.Remove(k));
            }
            Check.Equal(0, cache.Count);
            // Still a working cache.
            for (int i = 0; i < 5; i++) cache.Set(new ReKey(i), i);
            Check.Equal(2, cache.Count);
            Check.Equal(2, new List<ReKey>(cache.Keys).Count);
        }

        // ---- ReservoirSampler, WeightedWithoutReplacement ----

        // A seeded Random's NextDouble (and any on .NET Framework) is a multiple of 1/int.MaxValue. Past about 2^31 items
        // a slot, Algorithm L's skip lengths only landed on a grid (1 position in 93 at n/k = 1e11), so most items could
        // never be the next replacement (R96-2). The draw now has 53 random bits.
        public void Sampler_UniformDrawHasFiftyThreeBits()
        {
            var sampler = new ReservoirSampler<int>(1, new Random(220));
            MethodInfo uniform = typeof(ReservoirSampler<int>).GetMethod("Uniform", BindingFlags.NonPublic | BindingFlags.Instance)!;
            const int N = 100000;
            int onGrid = 0, odd = 0;
            double sum = 0;
            for (int i = 0; i < N; i++)
            {
                double u = (double)uniform.Invoke(sampler, null)!;
                Check.True(u > 0 && u < 1, "u " + u);
                double g = u * int.MaxValue;
                if (Math.Abs(g - Math.Round(g)) < 1e-6) onGrid++;
                if (((long)(u * 9007199254740992.0) & 1) == 1) odd++; // the 53rd bit
                sum += u;
            }
            Check.True(onGrid < N / 100, onGrid + " of " + N + " draws were on the 1/int.MaxValue grid");
            Check.True(Math.Abs(odd - N / 2) < 5 * Math.Sqrt(N / 4.0), "53rd bit set " + odd + " times in " + N);
            Check.True(Math.Abs(sum / N - 0.5) < 0.005, "mean " + sum / N);
        }

        // The skips themselves at n/k = 1e11 (w = 1e-11 at capacity 1): every skip a 31-bit draw u = m / int.MaxValue can
        // give is floor(ln u / ln(1 - w)) for an integer m, about 1 position in 47 near u = 1. All the old code's skips were
        // on that grid; from a 53-bit draw about 1 in 47 is.
        public void Sampler_SkipsAtHugeCountsAreNotOnAGrid()
        {
            var sampler = new ReservoirSampler<int>(1, new Random(7));
            Type t = typeof(ReservoirSampler<int>);
            FieldInfo w = t.GetField("_w", BindingFlags.NonPublic | BindingFlags.Instance)!;
            FieldInfo next = t.GetField("_nextAccept", BindingFlags.NonPublic | BindingFlags.Instance)!;
            MethodInfo schedule = t.GetMethod("ScheduleNext", BindingFlags.NonPublic | BindingFlags.Instance)!;
            const double W = 1e-11;
            double log1mw = -W - W * W / 2 - W * W * W / 3; // as the sampler computes it for small w
            const double M = int.MaxValue;
            int small = 0, onGrid = 0;
            for (int i = 0; i < 200000; i++)
            {
                w.SetValue(sampler, W);
                schedule.Invoke(sampler, new object[] { 0L });
                long skip = (long)next.GetValue(sampler)! - 1;
                if (skip >= 2000000000L) continue; // u near 1, where the grid's skips are about 47 apart
                small++;
                double m0 = Math.Round(M * Math.Exp((skip + 0.5) * log1mw));
                for (double m = m0 - 3; m <= m0 + 3; m++)
                    if (Math.Floor(Math.Log(m * (1.0 / M)) / log1mw) == skip) { onGrid++; break; }
            }
            Check.True(small > 1000, "only " + small + " skips under 2e9");
            Check.True(onGrid < small / 10, onGrid + " of " + small + " skips were on the 31-bit grid");
        }

        // ---- TtlCache ----

        // TryGet read the entry, then the clock; a clock that set the key meanwhile had its fresh entry removed as the
        // expired one, so the next TryGet missed a value that had not expired. Remove did the same and reported the old
        // entry's liveness.
        public void TtlCache_AClockThatSetsTheKey_KeepsTheFreshEntry()
        {
            foreach (bool viaRemove in new[] { false, true })
            {
                long t = 100;
                TtlCache<int, int>? c = null;
                bool reenter = false;
                c = new TtlCache<int, int>(TimeSpan.FromTicks(10), () =>
                {
                    if (reenter) { reenter = false; c!.Set(1, 2); }
                    return new DateTimeOffset(t, TimeSpan.Zero);
                });
                c.Set(1, 1);
                t = 200;
                reenter = true;
                if (viaRemove)
                {
                    Check.True(c.Remove(1), "Remove of the live entry the clock set");
                    Check.False(c.TryGet(1, out _));
                }
                else
                {
                    Check.True(c.TryGet(1, out int v), "lost the entry the clock set");
                    Check.Equal(2, v);
                    Check.True(c.TryGet(1, out v));
                    Check.Equal(2, v);
                }
            }
        }

        // ---- SlidingWindow, Combinations, CartesianProduct ----

        // A list that grew after the last item was read ended the enumeration quietly instead of throwing, as each one
        // does for a change mid-way and as List<T>'s enumerator does.
        public void Enumerators_AListThatGrewAfterTheLastItem_Throws()
        {
            var a = new List<int> { 1, 2, 3 };
            Check.Throws<InvalidOperationException>(() => { foreach (int[] w in SlidingWindow.Over(a, 3)) a.Add(9); });
            var b = new List<int> { 1, 2, 3 };
            Check.Throws<InvalidOperationException>(() => { foreach (int[] w in Combinatorics.Combinations(b, 3)) b.Add(9); });
            var c0 = new List<int> { 1 };
            var c1 = new List<int> { 2 };
            Check.Throws<InvalidOperationException>(() =>
            {
                foreach (int[] w in CartesianProduct.Of(new IReadOnlyList<int>[] { c0, c1 })) c1.Add(9);
            });
            // Guard: unchanged lists still end normally, with every item.
            int n = 0;
            foreach (int[] w in SlidingWindow.Over(new List<int> { 1, 2, 3, 4 }, 2)) n++;
            foreach (int[] w in Combinatorics.Combinations(new List<int> { 1, 2, 3, 4 }, 2)) n++;
            foreach (int[] w in CartesianProduct.Of(new IReadOnlyList<int>[] { new List<int> { 1, 2 }, new List<int> { 3, 4 } })) n++;
            Check.Equal(3 + 6 + 4, n);
        }
    }
}
