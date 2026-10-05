// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>
    /// Binary-search helpers for lists kept in sorted order — the C++ <c>lower_bound</c>/<c>upper_bound</c>/<c>equal_range</c>
    /// family that <see cref="List{T}.BinarySearch(T)"/> only half provides (it returns <em>an</em> index of a duplicate, not
    /// the first, and encodes "not found" as a bitwise complement). Plus sorted insert/remove and a key-projected search
    /// (<c>LowerBoundBy(samples, t, s =&gt; s.Time)</c>) for finding a time in a sorted record list without a separate key array.
    /// The list must already be sorted by the same comparer; that is not checked.
    /// </summary>
    public static class SortedListExtensions
    {
        /// <summary>Index of the first element not less than <paramref name="value"/> (Count if none).</summary>
        public static int LowerBound<T>(this IReadOnlyList<T> list, T value, IComparer<T>? comparer = null)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            comparer ??= Comparer<T>.Default;
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (comparer.Compare(list[mid], value) < 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>Index of the first element greater than <paramref name="value"/> (Count if none).</summary>
        public static int UpperBound<T>(this IReadOnlyList<T> list, T value, IComparer<T>? comparer = null)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            comparer ??= Comparer<T>.Default;
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (comparer.Compare(list[mid], value) <= 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>The half-open index range [Start, End) of elements equal to <paramref name="value"/>.</summary>
        public static (int Start, int End) EqualRange<T>(this IReadOnlyList<T> list, T value, IComparer<T>? comparer = null)
            => (list.LowerBound(value, comparer), list.UpperBound(value, comparer));

        /// <summary>Whether the sorted list contains <paramref name="value"/>.</summary>
        public static bool ContainsSorted<T>(this IReadOnlyList<T> list, T value, IComparer<T>? comparer = null)
        {
            int i = list.LowerBound(value, comparer);
            return i < list.Count && (comparer ?? Comparer<T>.Default).Compare(list[i], value) == 0;
        }

        /// <summary>Index of the first element whose projected key is not less than <paramref name="key"/>.</summary>
        public static int LowerBoundBy<T, TKey>(this IReadOnlyList<T> list, TKey key, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            comparer ??= Comparer<TKey>.Default;
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (comparer.Compare(keySelector(list[mid]), key) < 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>Index of the first element whose projected key is greater than <paramref name="key"/>.</summary>
        public static int UpperBoundBy<T, TKey>(this IReadOnlyList<T> list, TKey key, Func<T, TKey> keySelector, IComparer<TKey>? comparer = null)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            comparer ??= Comparer<TKey>.Default;
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (comparer.Compare(keySelector(list[mid]), key) <= 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Index of the element whose key is nearest <paramref name="key"/> (ties go to the earlier element), or −1 for an
        /// empty list — "the sample closest to t".
        /// </summary>
        public static int NearestBy<T>(this IReadOnlyList<T> list, double key, Func<T, double> keySelector)
        {
            int i = list.LowerBoundBy(key, keySelector);
            if (list.Count == 0) return -1;
            if (i == 0) return 0;
            if (i == list.Count) return list.Count - 1;
            return key - keySelector(list[i - 1]) <= keySelector(list[i]) - key ? i - 1 : i;
        }

        /// <summary>Inserts keeping the list sorted (after any equal elements, so insertion order is stable); returns the index.</summary>
        public static int InsertSorted<T>(this List<T> list, T value, IComparer<T>? comparer = null)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            int i = ((IReadOnlyList<T>)list).UpperBound(value, comparer);
            list.Insert(i, value);
            return i;
        }

        /// <summary>Removes one element equal to <paramref name="value"/> (the first); false if absent.</summary>
        public static bool RemoveSorted<T>(this List<T> list, T value, IComparer<T>? comparer = null)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            int i = ((IReadOnlyList<T>)list).LowerBound(value, comparer);
            if (i == list.Count || (comparer ?? Comparer<T>.Default).Compare(list[i], value) != 0) return false;
            list.RemoveAt(i);
            return true;
        }

        /// <summary>The elements in [<paramref name="from"/>, <paramref name="to"/>) — a range query on a sorted list.</summary>
        public static IEnumerable<T> RangeSorted<T>(this IReadOnlyList<T> list, T from, T to, IComparer<T>? comparer = null)
        {
            int start = list.LowerBound(from, comparer), end = list.LowerBound(to, comparer);
            for (int i = start; i < end; i++) yield return list[i];
        }

        /// <summary>True if the list is in non-decreasing order under the comparer.</summary>
        public static bool IsSorted<T>(this IReadOnlyList<T> list, IComparer<T>? comparer = null)
        {
            if (list is null) throw new ArgumentNullException(nameof(list));
            comparer ??= Comparer<T>.Default;
            for (int i = 1; i < list.Count; i++) if (comparer.Compare(list[i - 1], list[i]) > 0) return false;
            return true;
        }
    }
}
