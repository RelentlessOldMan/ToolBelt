// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Intervals
{
    /// <summary>
    /// A set of half-open intervals kept as a minimal, sorted, non-overlapping, coalesced cover, with
    /// union, intersection, difference and complement over any comparable key. Generalizes the date-range
    /// merge to numeric and other ordered domains. Instances are immutable; the set operations return new
    /// sets.
    /// </summary>
    public sealed class RangeSet<TKey> where TKey : IComparable<TKey>
    {
        private readonly List<Interval<TKey>> _intervals;

        public RangeSet() => _intervals = new List<Interval<TKey>>();

        public RangeSet(IEnumerable<Interval<TKey>> intervals)
        {
            if (intervals is null) throw new ArgumentNullException(nameof(intervals));
            _intervals = Normalize(intervals);
        }

        /// <summary>The disjoint intervals in ascending order.</summary>
        public IReadOnlyList<Interval<TKey>> Intervals => _intervals;
        public int Count => _intervals.Count;
        public bool IsEmpty => _intervals.Count == 0;

        /// <summary>True if <paramref name="point"/> lies in any interval.</summary>
        public bool Contains(TKey point)
        {
            int lo = 0, hi = _intervals.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (Cmp(_intervals[mid].Start, point) <= 0) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found >= 0 && _intervals[found].Contains(point);
        }

        public RangeSet<TKey> Union(RangeSet<TKey> other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            var all = new List<Interval<TKey>>(_intervals);
            all.AddRange(other._intervals);
            return new RangeSet<TKey>(all);
        }

        public RangeSet<TKey> Intersect(RangeSet<TKey> other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            var result = new List<Interval<TKey>>();
            int i = 0, j = 0;
            while (i < _intervals.Count && j < other._intervals.Count)
            {
                var a = _intervals[i];
                var b = other._intervals[j];
                TKey s = Max(a.Start, b.Start);
                TKey e = Min(a.End, b.End);
                if (Cmp(s, e) < 0) result.Add(new Interval<TKey>(s, e));
                if (Cmp(a.End, b.End) < 0) i++; else j++;
            }
            return new RangeSet<TKey>(result);
        }

        public RangeSet<TKey> Except(RangeSet<TKey> other)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            var result = new List<Interval<TKey>>();
            foreach (var x in _intervals)
            {
                TKey cursor = x.Start;
                foreach (var y in other._intervals)
                {
                    if (Cmp(y.End, cursor) <= 0) continue;      // y ends before the cursor
                    if (Cmp(y.Start, x.End) >= 0) break;        // y starts past x (lists sorted)
                    if (Cmp(y.Start, cursor) > 0)
                        result.Add(new Interval<TKey>(cursor, y.Start));
                    if (Cmp(y.End, cursor) > 0) cursor = y.End;
                    if (Cmp(cursor, x.End) >= 0) break;
                }
                if (Cmp(cursor, x.End) < 0)
                    result.Add(new Interval<TKey>(cursor, x.End));
            }
            return new RangeSet<TKey>(result);
        }

        /// <summary>The parts of <paramref name="universe"/> not covered by this set.</summary>
        public RangeSet<TKey> Complement(Interval<TKey> universe)
            => new RangeSet<TKey>(new[] { universe }).Except(this);

        private static List<Interval<TKey>> Normalize(IEnumerable<Interval<TKey>> items)
        {
            var list = new List<Interval<TKey>>();
            foreach (var i in items)
                if (!i.IsEmpty) list.Add(i);
            list.Sort((a, b) =>
            {
                int c = Cmp(a.Start, b.Start);
                return c != 0 ? c : Cmp(a.End, b.End);
            });

            var merged = new List<Interval<TKey>>();
            foreach (var i in list)
            {
                if (merged.Count == 0)
                {
                    merged.Add(i);
                    continue;
                }
                var last = merged[merged.Count - 1];
                if (Cmp(i.Start, last.End) <= 0) // overlaps or touches -> merge
                {
                    TKey end = Cmp(i.End, last.End) > 0 ? i.End : last.End;
                    merged[merged.Count - 1] = new Interval<TKey>(last.Start, end);
                }
                else
                {
                    merged.Add(i);
                }
            }
            return merged;
        }

        private static int Cmp(TKey a, TKey b) => Comparer<TKey>.Default.Compare(a, b);
        private static TKey Max(TKey a, TKey b) => Cmp(a, b) >= 0 ? a : b;
        private static TKey Min(TKey a, TKey b) => Cmp(a, b) <= 0 ? a : b;
    }
}
