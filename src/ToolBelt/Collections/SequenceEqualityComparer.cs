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
    /// <see cref="EqualityComparer{T}.Default"/>. Immutable and therefore thread-safe; the hash code is stable
    /// only while the sequence's contents do not change (do not mutate a sequence while it is in use as a key).
    /// </summary>
    public sealed class SequenceEqualityComparer<T> : IEqualityComparer<IEnumerable<T>>
    {
        private readonly IEqualityComparer<T> _elementComparer;

        /// <summary>Creates a comparer using the given element comparer (defaults to <see cref="EqualityComparer{T}.Default"/>).</summary>
        public SequenceEqualityComparer(IEqualityComparer<T>? elementComparer = null)
            => _elementComparer = elementComparer ?? EqualityComparer<T>.Default;

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
                if (!_elementComparer.Equals(ex.Current, ey.Current)) return false;
            }
        }

        public int GetHashCode(IEnumerable<T> obj)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            unchecked
            {
                int hash = 17;
                foreach (T item in obj)
                    hash = hash * 31 + (item is null ? 0 : _elementComparer.GetHashCode(item));
                return hash;
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
    /// <see cref="EqualityComparer{T}.Default"/>; null elements are counted correctly even for reference types.
    /// Immutable and thread-safe.
    /// </summary>
    public sealed class MultisetEqualityComparer<T> : IEqualityComparer<IEnumerable<T>>
    {
        private readonly IEqualityComparer<T> _elementComparer;

        /// <summary>Creates a comparer using the given element comparer (defaults to <see cref="EqualityComparer{T}.Default"/>).</summary>
        public MultisetEqualityComparer(IEqualityComparer<T>? elementComparer = null)
            => _elementComparer = elementComparer ?? EqualityComparer<T>.Default;

        /// <summary>A shared instance using the default element comparer.</summary>
        public static MultisetEqualityComparer<T> Default { get; } = new MultisetEqualityComparer<T>();

        public bool Equals(IEnumerable<T>? x, IEnumerable<T>? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            // Null elements are tallied in nullCount, never inserted, so the notnull-key constraint is honored
            // at run time even when T is a nullable reference type — hence the local suppression.
#pragma warning disable CS8714
            var counts = new Dictionary<T, int>(_elementComparer);
#pragma warning restore CS8714
            int nullCount = 0;
            foreach (T item in x)
            {
                if (item is null) nullCount++;
                else counts[item] = (counts.TryGetValue(item, out int n) ? n : 0) + 1;
            }

            foreach (T item in y)
            {
                if (item is null)
                {
                    if (--nullCount < 0) return false;
                    continue;
                }
                if (!counts.TryGetValue(item, out int n) || n == 0) return false;
                counts[item] = n - 1;
            }

            if (nullCount != 0) return false;
            foreach (int remaining in counts.Values)
                if (remaining != 0) return false;
            return true;
        }

        public int GetHashCode(IEnumerable<T> obj)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            unchecked
            {
                // Order-insensitive: a commutative combine (sum) so permutations hash alike.
                int hash = 0;
                int count = 0;
                foreach (T item in obj)
                {
                    hash += item is null ? 0 : _elementComparer.GetHashCode(item);
                    count++;
                }
                return hash * 31 + count;
            }
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
