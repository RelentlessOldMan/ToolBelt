// ToolBelt drop-in — also copy Intervals/Interval.cs.
using System;
using System.Collections.Generic;

namespace ToolBelt.Intervals
{
    /// <summary>
    /// A map from non-overlapping half-open key ranges to values. Assigning a range splits any ranges it
    /// partially covers, replaces any it fully covers, and coalesces neighbors that end up adjacent with an
    /// equal value. After any sequence of assignments the map is always a minimal, non-overlapping,
    /// coalesced cover — the invariant that hand-rolled "sorted list of bands" code usually gets wrong at
    /// the seams. Ideal for calibration bands, tier tables and piecewise-constant settings. Set and Remove find
    /// the affected segments by binary search and touch only those and their neighbors.
    /// </summary>
    public sealed class RangeMap<TKey, TValue> where TKey : IComparable<TKey>
    {
        private readonly List<Entry> _entries = new List<Entry>(); // sorted by Start, disjoint, coalesced
        private readonly IEqualityComparer<TValue> _valueComparer;

        private struct Entry
        {
            public TKey Start;
            public TKey End;
            public TValue Value;
        }

        public RangeMap(IEqualityComparer<TValue>? valueComparer = null)
        {
            _valueComparer = valueComparer ?? EqualityComparer<TValue>.Default;
        }

        /// <summary>The current segments in ascending order.</summary>
        public IEnumerable<KeyValuePair<Interval<TKey>, TValue>> Segments
        {
            get
            {
                foreach (var e in _entries)
                    yield return new KeyValuePair<Interval<TKey>, TValue>(new Interval<TKey>(e.Start, e.End), e.Value);
            }
        }

        public int SegmentCount => _entries.Count;

        /// <summary>Assigns <paramref name="value"/> across [start, end). An empty range is a no-op.</summary>
        public void Set(TKey start, TKey end, TValue value)
        {
            CheckNotNaN(start, end);
            if (Cmp(start, end) > 0)
                throw new ArgumentException($"Range start '{start}' must not be greater than end '{end}'.");
            if (Cmp(start, end) == 0) return; // empty

            int at = Replace(start, end, new Entry { Start = start, End = end, Value = value });
            // Only the new segment's neighbors can have become mergeable with it.
            if (at + 1 < _entries.Count) MergeWithNext(at);
            if (at > 0) MergeWithNext(at - 1);
        }

        /// <summary>Clears any assignment across [start, end), splitting partially covered ranges.</summary>
        public void Remove(TKey start, TKey end)
        {
            CheckNotNaN(start, end);
            if (Cmp(start, end) > 0)
                throw new ArgumentException($"Range start '{start}' must not be greater than end '{end}'.");
            if (Cmp(start, end) == 0) return;

            Replace(start, end, null);
        }

        // Replaces the segments overlapping [start, end) with the parts of them outside it, plus inserted (if any)
        // between those parts. Returns the index of inserted.
        private int Replace(TKey start, TKey end, Entry? inserted)
        {
            // i: first segment ending after start; j: first segment starting at or after end. [i, j) overlap.
            int lo = 0, hi = _entries.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (Cmp(_entries[mid].End, start) <= 0) lo = mid + 1; else hi = mid;
            }
            int i = lo;
            hi = _entries.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (Cmp(_entries[mid].Start, end) < 0) lo = mid + 1; else hi = mid;
            }
            int j = lo;

            var pieces = new List<Entry>(3);
            if (i < j && Cmp(_entries[i].Start, start) < 0)
                pieces.Add(new Entry { Start = _entries[i].Start, End = start, Value = _entries[i].Value });
            int at = i + pieces.Count;
            if (inserted.HasValue) pieces.Add(inserted.Value);
            if (i < j && Cmp(_entries[j - 1].End, end) > 0)
                pieces.Add(new Entry { Start = end, End = _entries[j - 1].End, Value = _entries[j - 1].Value });

            _entries.RemoveRange(i, j - i);
            _entries.InsertRange(i, pieces);
            return at;
        }

        /// <summary>Gets the value assigned at <paramref name="key"/>, if any.</summary>
        public bool TryGet(TKey key, out TValue value)
        {
            // Binary search for the segment whose Start <= key.
            int lo = 0, hi = _entries.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (Cmp(_entries[mid].Start, key) <= 0) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            if (found >= 0 && Cmp(key, _entries[found].End) < 0)
            {
                value = _entries[found].Value;
                return true;
            }
            value = default!;
            return false;
        }

        public bool ContainsKey(TKey key) => TryGet(key, out _);

        // Merges segment i with segment i + 1 when they touch and hold equal values.
        private void MergeWithNext(int i)
        {
            var prev = _entries[i];
            var cur = _entries[i + 1];
            if (Cmp(prev.End, cur.Start) == 0 && _valueComparer.Equals(prev.Value, cur.Value))
            {
                prev.End = cur.End;
                _entries[i] = prev;
                _entries.RemoveAt(i + 1);
            }
        }

        private static int Cmp(TKey a, TKey b) => Comparer<TKey>.Default.Compare(a, b);

        // NaN sorts below every number: a range from NaN covered everything below its end.
        private static void CheckNotNaN(TKey start, TKey end)
        {
            if (Interval<TKey>.IsNaN(start)) throw new ArgumentException("Range start must not be NaN.", nameof(start));
            if (Interval<TKey>.IsNaN(end)) throw new ArgumentException("Range end must not be NaN.", nameof(end));
        }
    }
}
