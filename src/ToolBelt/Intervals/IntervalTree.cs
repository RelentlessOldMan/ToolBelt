// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Intervals
{
    /// <summary>
    /// An immutable augmented interval tree: an index over a fixed set of intervals answering "which
    /// intervals contain this point" and "which overlap this range" in O(log n + matches). Built once from
    /// a balanced split of the intervals sorted by start, each node augmented with its subtree's maximum
    /// end for pruning. For the one-dimensional linear case with merge/gap operations, see the date-range
    /// type; this is the indexed version for many intervals.
    /// </summary>
    public sealed class IntervalTree<TKey, TValue> where TKey : IComparable<TKey>
    {
        private sealed class Node
        {
            public Interval<TKey> Interval = default!;
            public TValue Value = default!;
            public TKey MaxEnd = default!;
            public Node? Left;
            public Node? Right;
        }

        private readonly Node? _root;

        public int Count { get; }

        public IntervalTree(IEnumerable<KeyValuePair<Interval<TKey>, TValue>> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));

            var list = new List<KeyValuePair<Interval<TKey>, TValue>>(items);
            // Sort by start, then end, so the median split yields a balanced tree keyed by start.
            list.Sort((a, b) =>
            {
                int c = Interval<TKey>.Compare(a.Key.Start, b.Key.Start);
                return c != 0 ? c : Interval<TKey>.Compare(a.Key.End, b.Key.End);
            });
            Count = list.Count;
            _root = Build(list, 0, list.Count - 1);
        }

        private static Node? Build(List<KeyValuePair<Interval<TKey>, TValue>> sorted, int lo, int hi)
        {
            if (lo > hi) return null;
            int mid = lo + (hi - lo) / 2;
            var node = new Node
            {
                Interval = sorted[mid].Key,
                Value = sorted[mid].Value,
                Left = Build(sorted, lo, mid - 1),
                Right = Build(sorted, mid + 1, hi),
            };

            TKey maxEnd = node.Interval.End;
            if (node.Left != null && Interval<TKey>.Compare(node.Left.MaxEnd, maxEnd) > 0) maxEnd = node.Left.MaxEnd;
            if (node.Right != null && Interval<TKey>.Compare(node.Right.MaxEnd, maxEnd) > 0) maxEnd = node.Right.MaxEnd;
            node.MaxEnd = maxEnd;
            return node;
        }

        /// <summary>All intervals whose half-open range contains <paramref name="point"/>.</summary>
        public IReadOnlyList<KeyValuePair<Interval<TKey>, TValue>> Query(TKey point)
        {
            var results = new List<KeyValuePair<Interval<TKey>, TValue>>();
            SearchPoint(_root, point, results);
            return results;
        }

        /// <summary>All intervals that overlap <paramref name="range"/>.</summary>
        public IReadOnlyList<KeyValuePair<Interval<TKey>, TValue>> Query(Interval<TKey> range)
        {
            var results = new List<KeyValuePair<Interval<TKey>, TValue>>();
            SearchRange(_root, range, results);
            return results;
        }

        private static void SearchPoint(Node? node, TKey point, List<KeyValuePair<Interval<TKey>, TValue>> results)
        {
            if (node is null) return;
            // Prune left only if some interval there could still reach the point.
            if (node.Left != null && Interval<TKey>.Compare(point, node.Left.MaxEnd) < 0)
                SearchPoint(node.Left, point, results);
            if (node.Interval.Contains(point))
                results.Add(new KeyValuePair<Interval<TKey>, TValue>(node.Interval, node.Value));
            // Right subtree holds starts >= node.Start; only worth visiting if node.Start <= point.
            if (node.Right != null && Interval<TKey>.Compare(node.Interval.Start, point) <= 0)
                SearchPoint(node.Right, point, results);
        }

        private static void SearchRange(Node? node, Interval<TKey> range, List<KeyValuePair<Interval<TKey>, TValue>> results)
        {
            if (node is null) return;
            if (node.Left != null && Interval<TKey>.Compare(range.Start, node.Left.MaxEnd) < 0)
                SearchRange(node.Left, range, results);
            if (node.Interval.Overlaps(range))
                results.Add(new KeyValuePair<Interval<TKey>, TValue>(node.Interval, node.Value));
            if (node.Right != null && Interval<TKey>.Compare(node.Interval.Start, range.End) < 0)
                SearchRange(node.Right, range, results);
        }
    }
}
