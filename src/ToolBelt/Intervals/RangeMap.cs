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
    /// the affected segments by binary search and touch only those and their neighbors; the segments are kept in blocks,
    /// so a Set or Remove anywhere costs a binary search and a shift within one block, not a shift of every later segment (assigning
    /// ranges in descending order was quadratic). If the key comparison or the value comparer throws, the map is left as
    /// it was. Changing the map while enumerating <see cref="Segments"/> throws InvalidOperationException.
    /// </summary>
    public sealed class RangeMap<TKey, TValue> where TKey : IComparable<TKey>
    {
        private readonly BlockList _entries = new BlockList(); // sorted by Start, disjoint, coalesced
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
                throw new ArgumentException($"Range start '{start}' must not be greater than end '{end}'.", nameof(start));
            if (Cmp(start, end) == 0) return; // empty

            var entry = new Entry { Start = start, End = end, Value = value };
            int at = Replace(start, end, entry, out int first, out List<Entry> removed, out int added);
            // Only the new segment's neighbors can have become mergeable with it. Both are decided before either merge, and
            // a comparer that throws undoes the replace: the map was left changed but not coalesced.
            bool mergeNext, mergePrev;
            try
            {
                mergeNext = at + 1 < _entries.Count && Mergeable(entry, _entries[at + 1]);
                mergePrev = at > 0 && Mergeable(_entries[at - 1], entry);
            }
            catch
            {
                _entries.RemoveRange(first, added);
                _entries.InsertRange(first, removed);
                throw;
            }
            if (mergeNext)
            {
                entry.End = _entries[at + 1].End;
                _entries[at] = entry;
                _entries.RemoveAt(at + 1);
            }
            if (mergePrev)
            {
                var prev = _entries[at - 1];
                prev.End = entry.End;
                _entries[at - 1] = prev;
                _entries.RemoveAt(at);
            }
        }

        /// <summary>Clears any assignment across [start, end), splitting partially covered ranges.</summary>
        public void Remove(TKey start, TKey end)
        {
            CheckNotNaN(start, end);
            if (Cmp(start, end) > 0)
                throw new ArgumentException($"Range start '{start}' must not be greater than end '{end}'.", nameof(start));
            if (Cmp(start, end) == 0) return;

            Replace(start, end, null, out _, out _, out _);
        }

        // Replaces the segments overlapping [start, end) with the parts of them outside it, plus inserted (if any)
        // between those parts. Returns the index of inserted; first, removed and added describe the change, to undo it.
        // Every comparison is made before the list changes.
        private int Replace(TKey start, TKey end, Entry? inserted, out int first, out List<Entry> removed, out int added)
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

            removed = _entries.GetRange(i, j - i);
            _entries.RemoveRange(i, j - i);
            _entries.InsertRange(i, pieces);
            first = i;
            added = pieces.Count;
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

        // An indexed list stored as blocks of at most 2 * Block entries, with each block's first index: an insert or
        // remove shifts one block and renumbers the blocks, rather than shifting every later entry.
        private sealed class BlockList : IEnumerable<Entry>
        {
            private const int Block = 256;
            private readonly List<List<Entry>> _blocks = new List<List<Entry>>();
            private readonly List<int> _starts = new List<int>();
            private int _version;

            public int Count { get; private set; }

            public Entry this[int index]
            {
                get { int b = Locate(index); return _blocks[b][index - _starts[b]]; }
                set { int b = Locate(index); _blocks[b][index - _starts[b]] = value; _version++; }
            }

            // The block holding index (index == Count: the last block).
            private int Locate(int index)
            {
                if ((uint)index > (uint)Count || (index == Count && Count == 0)) throw new ArgumentOutOfRangeException(nameof(index));
                int lo = 0, hi = _blocks.Count - 1;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo + 1) / 2;
                    if (_starts[mid] <= index) lo = mid; else hi = mid - 1;
                }
                return lo;
            }

            public List<Entry> GetRange(int index, int count)
            {
                var result = new List<Entry>(count);
                for (int k = 0; k < count; k++) result.Add(this[index + k]);
                return result;
            }

            public void InsertRange(int index, List<Entry> items)
            {
                if (items.Count == 0) return;
                if (_blocks.Count == 0) _blocks.Add(new List<Entry>());
                int b = index == Count ? _blocks.Count - 1 : Locate(index);
                int start = index == Count ? Count - _blocks[b].Count : _starts[b];
                List<Entry> block = _blocks[b];
                block.InsertRange(index - start, items);
                if (block.Count > 2 * Block)
                {
                    // Split into blocks of Block entries.
                    var parts = new List<List<Entry>>();
                    for (int k = 0; k < block.Count; k += Block) parts.Add(block.GetRange(k, Math.Min(Block, block.Count - k)));
                    _blocks.RemoveAt(b);
                    _blocks.InsertRange(b, parts);
                }
                Count += items.Count;
                Renumber();
            }

            public void RemoveAt(int index) => RemoveRange(index, 1);

            public void RemoveRange(int index, int count)
            {
                if (count == 0) return;
                if (index < 0 || count < 0 || index + count > Count) throw new ArgumentOutOfRangeException(nameof(index));
                while (count > 0)
                {
                    int b = Locate(index);
                    int off = index - _starts[b];
                    int n = Math.Min(count, _blocks[b].Count - off);
                    _blocks[b].RemoveRange(off, n);
                    count -= n;
                    Count -= n;
                    if (_blocks[b].Count == 0) _blocks.RemoveAt(b);
                    Renumber();
                }
                // Neighbours that fit in one block are joined, so many removes cannot leave a block per entry.
                for (int b = 0; b + 1 < _blocks.Count; b++)
                    if (_blocks[b].Count + _blocks[b + 1].Count <= Block)
                    {
                        _blocks[b].AddRange(_blocks[b + 1]);
                        _blocks.RemoveAt(b + 1);
                        b--;
                    }
                Renumber();
            }

            private void Renumber()
            {
                _version++;
                _starts.Clear();
                int at = 0;
                foreach (List<Entry> block in _blocks)
                {
                    _starts.Add(at);
                    at += block.Count;
                }
            }

            public IEnumerator<Entry> GetEnumerator()
            {
                int version = _version;
                foreach (List<Entry> block in _blocks)
                    foreach (Entry e in block)
                    {
                        yield return e;
                        if (version != _version) throw new InvalidOperationException("The map was changed while its segments were enumerated.");
                    }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        // True when segment a ends where b starts and they hold equal values.
        private bool Mergeable(Entry a, Entry b) => Cmp(a.End, b.Start) == 0 && _valueComparer.Equals(a.Value, b.Value);

        private static int Cmp(TKey a, TKey b) => Comparer<TKey>.Default.Compare(a, b);

        // NaN sorts below every number: a range from NaN covered everything below its end.
        private static void CheckNotNaN(TKey start, TKey end)
        {
            if (Interval<TKey>.IsNaN(start)) throw new ArgumentException("Range start must not be NaN.", nameof(start));
            if (Interval<TKey>.IsNaN(end)) throw new ArgumentException("Range end must not be NaN.", nameof(end));
        }
    }
}
