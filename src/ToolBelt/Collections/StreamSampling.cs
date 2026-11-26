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
        private readonly IReadOnlyList<T> _view; // read-only wrapper, so Sample can't be cast back to List<T>
        private readonly Random _random;
        private double _w;
        private long _nextAccept;

        public ReservoirSampler(int capacity, Random random)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            Capacity = capacity;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _reservoir = new List<T>(Math.Min(capacity, 1024));
            _view = _reservoir.AsReadOnly();
        }

        public int Capacity { get; }

        /// <summary>Items offered so far.</summary>
        public long Count { get; private set; }

        /// <summary>
        /// The current sample (fewer than <see cref="Capacity"/> items until that many have been seen). Order is arbitrary.
        /// This is a live read-only view that changes as items are added; copy it to keep a snapshot.
        /// </summary>
        public IReadOnlyList<T> Sample => _view;

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
            // log(1 - w) is 0 once w < 2^-53 (after ~1e16 items at capacity 1), which made skip -∞ and every later
            // item a replacement; the series keeps it accurate for small w.
            double log1mw = _w < 1e-4 ? -_w - _w * _w / 2 - _w * _w * _w / 3 : Math.Log(1 - _w);
            double skip = Math.Floor(Math.Log(Uniform()) / log1mw);
            _nextAccept = skip >= long.MaxValue - index - 1 || double.IsNaN(skip) ? long.MaxValue : index + 1 + (long)skip;
        }

        private double Uniform() => SamplingPlans.NonZeroUniform(_random);
    }

    /// <summary>Weighted and stratified sampling without replacement.</summary>
    public static class SamplingPlans
    {
        // .NET Framework hashes a double or float NaN by its bits and a decimal by its scale, so equal keys (NaNs with
        // other payloads, 1.0m and 1.00m) fell into separate buckets: one key counted twice. Keys of those types hash
        // by value, and so do such values inside a tuple or an object key; everything else uses the default comparer.
        private static class ValueKeys
        {
            public static IEqualityComparer<TK> For<TK>()
            {
#if NETSTANDARD2_0
                object? c = typeof(TK) == typeof(double) ? DoubleKeys.Instance
                    : typeof(TK) == typeof(float) ? SingleKeys.Instance
                    : typeof(TK) == typeof(decimal) ? DecimalKeys.Instance
                    : typeof(TK) == typeof(double?) ? new NullableKeys<double>(DoubleKeys.Instance)
                    : typeof(TK) == typeof(float?) ? new NullableKeys<float>(SingleKeys.Instance)
                    : typeof(TK) == typeof(decimal?) ? new NullableKeys<decimal>(DecimalKeys.Instance)
                    : IsTuple(typeof(TK)) || (!typeof(TK).IsValueType && !typeof(TK).IsSealed) ? new DeepKeys<TK>()
                    : (object?)null;
                if (c is not null) return (IEqualityComparer<TK>)c;
#endif
                return EqualityComparer<TK>.Default;
            }
#if NETSTANDARD2_0

            private sealed class DoubleKeys : IEqualityComparer<double>
            {
                public static readonly DoubleKeys Instance = new DoubleKeys();
                public bool Equals(double x, double y) => x.Equals(y);
                public int GetHashCode(double v) => double.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class SingleKeys : IEqualityComparer<float>
            {
                public static readonly SingleKeys Instance = new SingleKeys();
                public bool Equals(float x, float y) => x.Equals(y);
                public int GetHashCode(float v) => float.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class DecimalKeys : IEqualityComparer<decimal>
            {
                public static readonly DecimalKeys Instance = new DecimalKeys();
                public bool Equals(decimal x, decimal y) => x == y;
                // Not through double: the conversion can round 1.0m and 1.00m-style pairs apart (the Framework's own bug).
                // Dividing by one drops trailing zeros, so equal nonzero decimals have one representation.
                public int GetHashCode(decimal v)
                {
                    if (v == 0) return 0;   // 0m and -0m
                    int[] b = decimal.GetBits(v / 1.000000000000000000000000000000000m);
                    return unchecked(b[0] ^ (b[1] * 31) ^ (b[2] * 961) ^ b[3]);
                }
            }

            private sealed class NullableKeys<TV> : IEqualityComparer<TV?> where TV : struct
            {
                private readonly IEqualityComparer<TV> _inner;
                public NullableKeys(IEqualityComparer<TV> inner) => _inner = inner;
                public bool Equals(TV? x, TV? y) => x.HasValue ? y.HasValue && _inner.Equals(x.Value, y.Value) : !y.HasValue;
                public int GetHashCode(TV? v) => v.HasValue ? _inner.GetHashCode(v.Value) : 0;
            }

            // Tuples (System.ValueTuple and System.Tuple, nested too) and keys of a type that can hold a boxed value
            // (object, an interface, an unsealed class) hashed a NaN inside them by its bits as well: (NaN, 1) and
            // (NaN with another payload, 1) are Equals but counted twice, and SequenceComparer gave them different hashes.
            // Equality stays the default; the hash walks tuples and hashes doubles, floats and decimals by value.
            private sealed class DeepKeys<TV> : IEqualityComparer<TV>
            {
                private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, object?>[]?> Members =
                    new System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, object?>[]?>();

                public bool Equals(TV x, TV y) => EqualityComparer<TV>.Default.Equals(x, y);
                public int GetHashCode(TV v) => Hash(v);

                private static int Hash(object? o)
                {
                    if (o is null) return 0;
                    if (o is double d) return DoubleKeys.Instance.GetHashCode(d);
                    if (o is float f) return SingleKeys.Instance.GetHashCode(f);
                    if (o is decimal m) return DecimalKeys.Instance.GetHashCode(m);
                    Func<object, object?>[]? items = Members.GetOrAdd(o.GetType(), TupleItems);
                    if (items is null) return o.GetHashCode();
                    int h = 17;
                    foreach (Func<object, object?> item in items)
                        h = unchecked(h * 31 + Hash(item(o)));
                    return h;
                }

                // The items of a tuple type, in a fixed order; null for any other type.
                private static Func<object, object?>[]? TupleItems(Type t)
                {
                    if (!IsTuple(t)) return null;
                    var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                    System.Reflection.MemberInfo[] members = t.IsValueType ? t.GetFields(flags) : (System.Reflection.MemberInfo[])t.GetProperties(flags);
                    Array.Sort(members, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                    var items = new Func<object, object?>[members.Length];
                    for (int i = 0; i < members.Length; i++)
                        items[i] = members[i] is System.Reflection.FieldInfo field
                            ? (Func<object, object?>)field.GetValue
                            : ((System.Reflection.PropertyInfo)members[i]).GetValue;
                    return items;
                }
            }

            private static bool IsTuple(Type t)
            {
                if (!t.IsGenericType) return false;
                string? name = t.GetGenericTypeDefinition().FullName;
                return name is not null && (name.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
                    || name.StartsWith("System.Tuple`", StringComparison.Ordinal));
            }
#endif
        }

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
            if (items.Count != weights.Count) throw new ArgumentException("Items and weights must have the same length.", nameof(weights));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");
            int positive = 0;
            double max = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                double w = weights[i];
                if (!(w >= 0) || double.IsInfinity(w)) throw new ArgumentException($"Weight {i} is {w}; weights must be finite and non-negative.", nameof(weights));
                if (w > 0) positive++;
                if (w > max) max = w;
            }
            if (count > positive) throw new ArgumentException($"Asked for {count} items but only {positive} have positive weight.", nameof(count));

            // Min-heap of the best `count` keys seen.
            var heap = new SortedSet<(double Key, int Index)>();
            for (int i = 0; i < items.Count; i++)
            {
                if (weights[i] == 0) continue;
                double u = NonZeroUniform(random);
                // ln(u)/w in log space: −(ln(−ln u) − ln w) orders the same and keeps every weight's own scale. ln(u)/w for a
                // subnormal w was −∞, and relative weights w/max underflowed to 0 for a 1e308 neighbour, which tied them and
                // left the draw to the index.
                double key = Math.Log(weights[i]) - Math.Log(-Math.Log(u));
                if (heap.Count < count) heap.Add((key, i));
                else if (count > 0 && key > heap.Min.Key)
                {
                    heap.Remove(heap.Min);
                    heap.Add((key, i));
                }
            }
            return heap.Reverse().Select(e => items[e.Index]).ToArray();
        }

        // Uniform on (0, 1): never exactly 0, so logarithms stay finite. Redrawing until nonzero hung on a Random that
        // only ever returns 0; a sound one returns 0 sixty-four times running with probability 2^-3392.
        // 53 random bits from two draws: a seeded Random's NextDouble (and any on .NET Framework) is a multiple of
        // 1/int.MaxValue, so past about 2^31 items a slot ReservoirSampler's skips only landed on a grid (1 position in 93
        // at n/k = 1e11) and most items could never be the next replacement.
        internal static double NonZeroUniform(Random random)
        {
            for (int i = 0; i < 64; i++)
            {
                double hi = Math.Floor(random.NextDouble() * 67108864.0);   // 26 bits
                double lo = Math.Floor(random.NextDouble() * 134217728.0);  // 27 bits
                double u = (hi * 134217728.0 + lo) * (1.0 / 9007199254740992.0);
                if (u != 0) return u;
            }
            throw new InvalidOperationException("The random source returned 0 sixty-four times in a row.");
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
            var groups = new Dictionary<TKey, List<T>>(ValueKeys.For<TKey>());
            var order = new List<TKey>();
            foreach (T item in items)
            {
                TKey key = stratum(item);
                if (key is null) throw new ArgumentException("stratum returned null for an item.", nameof(stratum));   // the dictionary threw ArgumentNullException("key")
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
