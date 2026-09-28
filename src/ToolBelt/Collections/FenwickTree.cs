// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A Fenwick tree (binary indexed tree) over <see cref="long"/> values: point updates and prefix-sum
    /// / range-sum queries, both in O(log n). Indices are 0-based. Ideal when values change and you need
    /// running totals repeatedly (a plain prefix-sum array is O(n) per update). Not thread-safe.
    /// </summary>
    public sealed class FenwickTree
    {
        private readonly long[] _tree; // 1-based; _tree[0] is unused

        public FenwickTree(int size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must not be negative.");
            _tree = new long[size + 1];
        }

        public int Size => _tree.Length - 1;

        /// <summary>Adds <paramref name="delta"/> to the value at <paramref name="index"/>.</summary>
        public void Add(int index, long delta)
        {
            if ((uint)index >= (uint)Size)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {Size}).");
            for (int x = index + 1; x <= Size; x += x & -x)
                _tree[x] += delta;
        }

        /// <summary>Sum of values in the inclusive range [0, <paramref name="index"/>].</summary>
        public long PrefixSum(int index)
        {
            if ((uint)index >= (uint)Size)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {Size}).");
            long sum = 0;
            for (int x = index + 1; x > 0; x -= x & -x)
                sum += _tree[x];
            return sum;
        }

        /// <summary>Sum of values in the inclusive range [<paramref name="from"/>, <paramref name="to"/>].</summary>
        public long RangeSum(int from, int to)
        {
            if ((uint)from >= (uint)Size)
                throw new ArgumentOutOfRangeException(nameof(from), from, $"Index must be in [0, {Size}).");
            if ((uint)to >= (uint)Size)
                throw new ArgumentOutOfRangeException(nameof(to), to, $"Index must be in [0, {Size}).");
            if (from > to)
                throw new ArgumentException("from must not exceed to.", nameof(from));
            long upper = PrefixSum(to);
            long lower = from == 0 ? 0 : PrefixSum(from - 1);
            return upper - lower;
        }

        /// <summary>The current value at a single index.</summary>
        public long ValueAt(int index) => RangeSum(index, index);
    }
}
