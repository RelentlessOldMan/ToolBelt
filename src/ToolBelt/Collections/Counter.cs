// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A multiset / frequency counter: tracks how many times each item has been added. Missing items
    /// report a count of zero; a count that drops to zero removes the item. Not thread-safe.
    /// </summary>
    public sealed class Counter<T>
        where T : notnull
    {
        private readonly Dictionary<T, int> _counts = new Dictionary<T, int>();

        /// <summary>Number of distinct items with a positive count.</summary>
        public int Count => _counts.Count;

        /// <summary>Sum of all counts across every item.</summary>
        public long Total { get; private set; }

        public IReadOnlyCollection<T> Keys => _counts.Keys;

        /// <summary>The count for an item (zero if absent).</summary>
        public int this[T item]
        {
            get
            {
                if (item is null) throw new ArgumentNullException(nameof(item));
                return _counts.TryGetValue(item, out int c) ? c : 0;
            }
        }

        /// <summary>Increments an item's count by <paramref name="amount"/> (default 1).</summary>
        public void Add(T item, int amount = 1)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");

            _counts.TryGetValue(item, out int current);
            // checked so a per-item count exceeding int.MaxValue throws rather than wrapping negative
            // and silently desynchronizing from the long Total.
            _counts[item] = checked(current + amount);
            Total += amount;
        }

        /// <summary>
        /// Decrements an item's count by <paramref name="amount"/>, clamping at zero and removing the item
        /// if it reaches zero. Returns how many were actually removed.
        /// </summary>
        public int Remove(T item, int amount = 1)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");

            if (!_counts.TryGetValue(item, out int current))
                return 0;

            int removed = Math.Min(current, amount);
            int remaining = current - removed;
            if (remaining == 0)
                _counts.Remove(item);
            else
                _counts[item] = remaining;
            Total -= removed;
            return removed;
        }

        public bool Contains(T item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            return _counts.ContainsKey(item);
        }

        /// <summary>The <paramref name="k"/> most common items, highest count first.</summary>
        public IReadOnlyList<KeyValuePair<T, int>> MostCommon(int k)
        {
            if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), k, "Count must not be negative.");
            return _counts
                .OrderByDescending(pair => pair.Value)
                .Take(k)
                .ToList();
        }

        public void Clear()
        {
            _counts.Clear();
            Total = 0;
        }
    }
}
