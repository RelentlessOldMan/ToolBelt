// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Intervals
{
    /// <summary>
    /// A map from non-overlapping half-open key ranges to values. Assigning a range splits any ranges it
    /// partially covers, replaces any it fully covers, and coalesces neighbors that end up adjacent with an
    /// equal value. After any sequence of assignments the map is always a minimal, non-overlapping,
    /// coalesced cover — the invariant that hand-rolled "sorted list of bands" code usually gets wrong at
    /// the seams. Ideal for calibration bands, tier tables and piecewise-constant settings.
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
            if (Cmp(start, end) > 0)
                throw new ArgumentException($"Range start '{start}' must not be greater than end '{end}'.");
            if (Cmp(start, end) == 0) return; // empty

            var rebuilt = new List<Entry>(_entries.Count + 2);
            foreach (var e in _entries)
            {
                if (Cmp(e.End, start) <= 0 || Cmp(e.Start, end) >= 0)
                {
                    rebuilt.Add(e); // no overlap with the new range
                    continue;
                }
                // Overlap: keep the parts that stick out past the new range.
                if (Cmp(e.Start, start) < 0)
                    rebuilt.Add(new Entry { Start = e.Start, End = start, Value = e.Value });
                if (Cmp(e.End, end) > 0)
                    rebuilt.Add(new Entry { Start = end, End = e.End, Value = e.Value });
            }
            rebuilt.Add(new Entry { Start = start, End = end, Value = value });
            rebuilt.Sort((a, b) => Cmp(a.Start, b.Start));

            Coalesce(rebuilt);
            _entries.Clear();
            _entries.AddRange(rebuilt);
        }

        /// <summary>Clears any assignment across [start, end), splitting partially covered ranges.</summary>
        public void Remove(TKey start, TKey end)
        {
            if (Cmp(start, end) > 0)
                throw new ArgumentException($"Range start '{start}' must not be greater than end '{end}'.");
            if (Cmp(start, end) == 0) return;

            var rebuilt = new List<Entry>(_entries.Count + 2);
            foreach (var e in _entries)
            {
                if (Cmp(e.End, start) <= 0 || Cmp(e.Start, end) >= 0)
                {
                    rebuilt.Add(e);
                    continue;
                }
                if (Cmp(e.Start, start) < 0)
                    rebuilt.Add(new Entry { Start = e.Start, End = start, Value = e.Value });
                if (Cmp(e.End, end) > 0)
                    rebuilt.Add(new Entry { Start = end, End = e.End, Value = e.Value });
            }
            _entries.Clear();
            _entries.AddRange(rebuilt);
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

        private void Coalesce(List<Entry> entries)
        {
            for (int i = entries.Count - 1; i >= 1; i--)
            {
                var prev = entries[i - 1];
                var cur = entries[i];
                if (Cmp(prev.End, cur.Start) == 0 && _valueComparer.Equals(prev.Value, cur.Value))
                {
                    prev.End = cur.End;
                    entries[i - 1] = prev;
                    entries.RemoveAt(i);
                }
            }
        }

        private static int Cmp(TKey a, TKey b) => Comparer<TKey>.Default.Compare(a, b);
    }
}
