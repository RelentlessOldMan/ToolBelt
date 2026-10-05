// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A uniform random sample of <c>k</c> items from a stream of unknown length, in O(k) memory — every item seen so far
    /// has the same k/n chance of being in <see cref="Sample"/>. Uses Li's Algorithm L, which draws O(k·log(n/k)) random
    /// numbers instead of one per item, so it is cheap on very long streams. Feed with <see cref="Add"/> or
    /// <see cref="AddRange"/>; the sample is valid at any point. Not thread-safe.
    /// </summary>
    public sealed class ReservoirSampler<T>
    {
        private readonly List<T> _reservoir;
        private readonly Random _random;
        private double _w;
        private long _nextAccept;

        public ReservoirSampler(int capacity, Random random)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            Capacity = capacity;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _reservoir = new List<T>(Math.Min(capacity, 1024));
        }

        public int Capacity { get; }

        /// <summary>Items offered so far.</summary>
        public long Count { get; private set; }

        /// <summary>The current sample (fewer than <see cref="Capacity"/> items until that many have been seen). Order is arbitrary.</summary>
        public IReadOnlyList<T> Sample => _reservoir;

        public void Add(T item)
        {
            long index = Count++;
            if (index < Capacity)
            {
                _reservoir.Add(item);
                if (index == Capacity - 1)
                {
                    _w = Math.Exp(Math.Log(Uniform()) / Capacity);
                    ScheduleNext(index);
                }
                return;
            }
            if (index < _nextAccept) return;
            _reservoir[_random.Next(Capacity)] = item;
            _w *= Math.Exp(Math.Log(Uniform()) / Capacity);
            ScheduleNext(index);
        }

        public void AddRange(IEnumerable<T> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            foreach (T item in items) Add(item);
        }

        // Algorithm L: skip a geometrically distributed number of items before the next replacement.
        private void ScheduleNext(long index)
        {
            double skip = Math.Floor(Math.Log(Uniform()) / Math.Log(1 - _w));
            _nextAccept = skip >= long.MaxValue - index - 1 || double.IsNaN(skip) ? long.MaxValue : index + 1 + (long)skip;
        }

        // Uniform on (0, 1): never exactly 0, so logarithms stay finite.
        private double Uniform()
        {
            double u;
            do u = _random.NextDouble(); while (u == 0);
            return u;
        }
    }

    /// <summary>Weighted and stratified sampling without replacement.</summary>
    public static class SamplingPlans
    {
        /// <summary>
        /// <paramref name="count"/> distinct items, each draw proportional to weight among the items not yet drawn
        /// (Efraimidis–Spirakis A-ES: key = ln(u)/w, keep the largest keys) — the "weighted lottery without putting tickets
        /// back". O(n log k). Zero-weight items are never chosen; asking for more items than have positive weight throws.
        /// Results are in draw order (most-favoured first).
        /// </summary>
        public static IReadOnlyList<T> WeightedWithoutReplacement<T>(IReadOnlyList<T> items, IReadOnlyList<double> weights, int count, Random random)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (weights is null) throw new ArgumentNullException(nameof(weights));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (items.Count != weights.Count) throw new ArgumentException("Items and weights must have the same length.");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");
            int positive = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                double w = weights[i];
                if (!(w >= 0) || double.IsInfinity(w)) throw new ArgumentException($"Weight {i} is {w}; weights must be finite and non-negative.", nameof(weights));
                if (w > 0) positive++;
            }
            if (count > positive) throw new ArgumentException($"Asked for {count} items but only {positive} have positive weight.", nameof(count));

            // Min-heap of the best `count` keys seen.
            var heap = new SortedSet<(double Key, int Index)>();
            for (int i = 0; i < items.Count; i++)
            {
                if (weights[i] == 0) continue;
                double u;
                do u = random.NextDouble(); while (u == 0);
                double key = Math.Log(u) / weights[i];
                if (heap.Count < count) heap.Add((key, i));
                else if (count > 0 && key > heap.Min.Key)
                {
                    heap.Remove(heap.Min);
                    heap.Add((key, i));
                }
            }
            return heap.Reverse().Select(e => items[e.Index]).ToArray();
        }

        /// <summary>
        /// Up to <paramref name="perStratum"/> items chosen uniformly from each group (all of a smaller group). Groups are
        /// returned in first-seen order so the result is reproducible for a seeded <paramref name="random"/>.
        /// </summary>
        public static IReadOnlyList<T> Stratified<T, TKey>(IEnumerable<T> items, Func<T, TKey> stratum, int perStratum, Random random)
            where TKey : notnull
            => StratifiedCore(items, stratum, random, n => Math.Min(n, perStratum), perStratum < 0 ? nameof(perStratum) : null);

        /// <summary>
        /// A proportional stratified sample: <paramref name="fraction"/> of each group, rounded, at least one item from every
        /// non-empty group (so rare groups aren't lost — the point of stratifying).
        /// </summary>
        public static IReadOnlyList<T> StratifiedFraction<T, TKey>(IEnumerable<T> items, Func<T, TKey> stratum, double fraction, Random random)
            where TKey : notnull
            => StratifiedCore(items, stratum, random, n => Math.Min(n, Math.Max(1, (int)Math.Round(n * fraction, MidpointRounding.AwayFromZero))),
                fraction > 0 && fraction <= 1 ? null : nameof(fraction));

        private static IReadOnlyList<T> StratifiedCore<T, TKey>(IEnumerable<T> items, Func<T, TKey> stratum, Random random, Func<int, int> take, string? badArg)
            where TKey : notnull
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (stratum is null) throw new ArgumentNullException(nameof(stratum));
            if (random is null) throw new ArgumentNullException(nameof(random));
            if (badArg != null) throw new ArgumentOutOfRangeException(badArg);
            var groups = new Dictionary<TKey, List<T>>();
            var order = new List<TKey>();
            foreach (T item in items)
            {
                TKey key = stratum(item);
                if (!groups.TryGetValue(key, out var list)) { groups[key] = list = new List<T>(); order.Add(key); }
                list.Add(item);
            }
            var result = new List<T>();
            foreach (TKey key in order)
            {
                var g = groups[key];
                int k = take(g.Count);
                // Partial Fisher–Yates: the first k slots become a uniform sample.
                for (int i = 0; i < k; i++)
                {
                    int j = i + random.Next(g.Count - i);
                    (g[i], g[j]) = (g[j], g[i]);
                    result.Add(g[i]);
                }
            }
            return result;
        }
    }
}
