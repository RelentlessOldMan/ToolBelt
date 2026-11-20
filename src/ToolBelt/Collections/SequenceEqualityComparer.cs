// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// An <see cref="IEqualityComparer{T}"/> over sequences that compares them element by element, in order:
    /// two sequences are equal when they have the same length and equal elements at every position. Pair it
    /// with a <c>Dictionary</c>/<c>HashSet</c> to key on a list or array by its contents rather than its
    /// reference, or to compare sequences without materializing them through LINQ. A null sequence equals only
    /// another null sequence (and never an empty one). Element equality uses the supplied comparer, or
    /// <see cref="EqualityComparer{T}.Default"/>; a null element equals only a null element and is never passed to
    /// it, as in <see cref="MultisetEqualityComparer{T}"/> (a comparer that equated null with "" made equal sequences
    /// hash differently). Immutable and therefore thread-safe; the hash code is stable
    /// only while the sequence's contents do not change (do not mutate a sequence while it is in use as a key).
    /// </summary>
    public sealed class SequenceEqualityComparer<T> : IEqualityComparer<IEnumerable<T>>
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

        private readonly IEqualityComparer<T> _elementComparer;

        /// <summary>Creates a comparer using the given element comparer (defaults to <see cref="EqualityComparer{T}.Default"/>).</summary>
        public SequenceEqualityComparer(IEqualityComparer<T>? elementComparer = null)
            => _elementComparer = elementComparer ?? ValueKeys.For<T>();

        /// <summary>A shared instance using the default element comparer.</summary>
        public static SequenceEqualityComparer<T> Default { get; } = new SequenceEqualityComparer<T>();

        public bool Equals(IEnumerable<T>? x, IEnumerable<T>? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            // Fast path when both expose a count: unequal lengths can't be equal sequences.
            if (TryGetCount(x, out int cx) && TryGetCount(y, out int cy) && cx != cy)
                return false;

            using IEnumerator<T> ex = x.GetEnumerator();
            using IEnumerator<T> ey = y.GetEnumerator();
            while (true)
            {
                bool hasX = ex.MoveNext();
                bool hasY = ey.MoveNext();
                if (hasX != hasY) return false;   // different lengths
                if (!hasX) return true;           // both ended together
                T a = ex.Current, b = ey.Current;
                if (a is null || b is null) { if ((a is null) != (b is null)) return false; }
                else if (!_elementComparer.Equals(a, b)) return false;
            }
        }

        public int GetHashCode(IEnumerable<T> obj)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            uint hash = 0x9747B28C;
            uint count = 0;
            foreach (T item in obj)
            {
                hash = Step(hash, item is null ? 0 : _elementComparer.GetHashCode(item));
                unchecked { count++; }
            }
            return unchecked((int)Finish(hash ^ count));
        }

        // Murmur3's element step and finalizer. Plain hash*31 + h let crafted sequences collide by construction:
        // every [i, -31i] had one hash (40,000 keys in a HashSet took 30 s).
        internal static uint Step(uint hash, int h)
        {
            unchecked
            {
                uint k = (uint)h * 0xCC9E2D51;
                k = (k << 15) | (k >> 17);
                hash ^= k * 0x1B873593;
                hash = (hash << 13) | (hash >> 19);
                return hash * 5 + 0xE6546B64;
            }
        }

        internal static uint Finish(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x85EBCA6B;
                h ^= h >> 13; h *= 0xC2B2AE35;
                return h ^ (h >> 16);
            }
        }

        private static bool TryGetCount(IEnumerable<T> seq, out int count)
        {
            if (seq is IReadOnlyCollection<T> roc) { count = roc.Count; return true; }
            if (seq is ICollection<T> c) { count = c.Count; return true; }
            count = 0;
            return false;
        }
    }

    /// <summary>
    /// An <see cref="IEqualityComparer{T}"/> over sequences that compares them as multisets (bags): two
    /// sequences are equal when they contain the same elements with the same multiplicities, regardless of
    /// order. <c>[1,2,2]</c> equals <c>[2,1,2]</c> but not <c>[1,2]</c>. A null sequence equals only another
    /// null sequence. Element identity/hashing uses the supplied comparer, or
    /// <see cref="EqualityComparer{T}.Default"/>; null elements are counted separately (a null equals only a null and is
    /// never passed to the comparer), even for reference types.
    /// Immutable and thread-safe.
    /// </summary>
    public sealed class MultisetEqualityComparer<T> : IEqualityComparer<IEnumerable<T>>
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

        private readonly IEqualityComparer<T> _elementComparer;

        /// <summary>Creates a comparer using the given element comparer (defaults to <see cref="EqualityComparer{T}.Default"/>).</summary>
        public MultisetEqualityComparer(IEqualityComparer<T>? elementComparer = null)
            => _elementComparer = elementComparer ?? ValueKeys.For<T>();

        /// <summary>A shared instance using the default element comparer.</summary>
        public static MultisetEqualityComparer<T> Default { get; } = new MultisetEqualityComparer<T>();

        public bool Equals(IEnumerable<T>? x, IEnumerable<T>? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (TryGetCount(x, out int xCount) && TryGetCount(y, out int yCount) && xCount != yCount) return false;

            // Null elements are tallied in nullCount, never inserted, so the notnull-key constraint is honored
            // at run time even when T is a nullable reference type — hence the local suppression.
#pragma warning disable CS8714
            var counts = new Dictionary<T, long>(_elementComparer);
#pragma warning restore CS8714
            // Counted in longs: int counts wrapped, so 2^32 nulls (or copies of one item) matched an empty sequence.
            long nullCount = 0;
            foreach (T item in x)
            {
                if (item is null) nullCount++;
                else counts[item] = (counts.TryGetValue(item, out long n) ? n : 0) + 1;
            }

            foreach (T item in y)
            {
                if (item is null)
                {
                    if (--nullCount < 0) return false;
                    continue;
                }
                if (!counts.TryGetValue(item, out long n) || n == 0) return false;
                counts[item] = n - 1;
            }

            if (nullCount != 0) return false;
            foreach (long remaining in counts.Values)
                if (remaining != 0) return false;
            return true;
        }

        public int GetHashCode(IEnumerable<T> obj)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            unchecked
            {
                // Order-insensitive: a commutative combine (sum) of mixed element hashes, so permutations hash alike.
                // The raw hashes were summed, so every int multiset with one sum and size collided (the 280,840
                // 3-subsets of 0..119 had 352 hashes, and a HashSet of them took 29 s).
                uint sum = 0, count = 0;
                foreach (T item in obj)
                {
                    sum += SequenceEqualityComparer<T>.Finish((uint)(item is null ? 0 : _elementComparer.GetHashCode(item)) + 0x9E3779B9);
                    count++;
                }
                return (int)SequenceEqualityComparer<T>.Finish(sum ^ (count * 0x85EBCA6B));
            }
        }

        private static bool TryGetCount(IEnumerable<T> seq, out int count)
        {
            if (seq is IReadOnlyCollection<T> roc) { count = roc.Count; return true; }
            if (seq is ICollection<T> c) { count = c.Count; return true; }
            count = 0;
            return false;
        }
    }

    /// <summary>
    /// Factory helpers for the sequence comparers, letting the element type be inferred from an element
    /// comparer where possible: <c>SequenceComparer.Ordered&lt;int&gt;()</c>, <c>SequenceComparer.Unordered(myComparer)</c>.
    /// </summary>
    public static class SequenceComparer
    {
        /// <summary>An order-sensitive sequence comparer (see <see cref="SequenceEqualityComparer{T}"/>).</summary>
        public static SequenceEqualityComparer<T> Ordered<T>(IEqualityComparer<T>? elementComparer = null)
            => elementComparer is null ? SequenceEqualityComparer<T>.Default : new SequenceEqualityComparer<T>(elementComparer);

        /// <summary>An order-insensitive multiset comparer (see <see cref="MultisetEqualityComparer{T}"/>).</summary>
        public static MultisetEqualityComparer<T> Unordered<T>(IEqualityComparer<T>? elementComparer = null)
            => elementComparer is null ? MultisetEqualityComparer<T>.Default : new MultisetEqualityComparer<T>(elementComparer);
    }
}
