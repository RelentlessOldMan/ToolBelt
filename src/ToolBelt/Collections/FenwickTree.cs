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

#if NETSTANDARD2_0
        // .NET Framework caps an array of elements wider than a byte at 0x7FEFFFFF; Array.MaxLength (0x7FFFFFC7) is .NET's.
        // A length between passed the check there and threw OutOfMemoryException.
        private static readonly int ArrayMaxLength = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
            .StartsWith(".NET Framework", StringComparison.Ordinal) ? 0x7FEFFFFF : 0x7FFFFFC7;
#else
        private const int ArrayMaxLength = 0x7FFFFFC7;   // Array.MaxLength
#endif

        public FenwickTree(int size)
        {
            // size + 1 past Array.MaxLength threw OutOfMemoryException ("array dimensions exceeded").
            if (size < 0 || size >= ArrayMaxLength)
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be in [0, Array.MaxLength − 1] (0x7FFFFFC6; 0x7FEFFFFE on .NET Framework).");
            _tree = new long[size + 1];
        }

        public int Size => _tree.Length - 1;

        /// <summary>Adds <paramref name="delta"/> to the value at <paramref name="index"/>.</summary>
        public void Add(int index, long delta)
        {
            if (unchecked((uint)index >= (uint)Size))
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {Size}).");
            int size = Size;
            // Sums wrap modulo 2^64, so a range whose true sum fits is exact even if a prefix overflows. In a checked build
            // the addition threw partway up the path and left some nodes updated.
            for (int x = index + 1; ; )
            {
                _tree[x] = unchecked(_tree[x] + delta);
                int step = x & -x;
                if (x > size - step) break; // x + step would pass the end (or overflow int)
                x += step;
            }
        }

        /// <summary>Sum of values in the inclusive range [0, <paramref name="index"/>].</summary>
        public long PrefixSum(int index)
        {
            if (unchecked((uint)index >= (uint)Size))
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {Size}).");
            long sum = 0;
            for (int x = index + 1; x > 0; x -= x & -x)
                sum = unchecked(sum + _tree[x]);
            return sum;
        }

        /// <summary>Sum of values in the inclusive range [<paramref name="from"/>, <paramref name="to"/>].</summary>
        public long RangeSum(int from, int to)
        {
            if (unchecked((uint)from >= (uint)Size))
                throw new ArgumentOutOfRangeException(nameof(from), from, $"Index must be in [0, {Size}).");
            if (unchecked((uint)to >= (uint)Size))
                throw new ArgumentOutOfRangeException(nameof(to), to, $"Index must be in [0, {Size}).");
            if (from > to)
                throw new ArgumentException("from must not exceed to.", nameof(from));
            long upper = PrefixSum(to);
            long lower = from == 0 ? 0 : PrefixSum(from - 1);
            return unchecked(upper - lower);
        }

        /// <summary>The current value at a single index.</summary>
        public long ValueAt(int index) => RangeSum(index, index);
    }
}
