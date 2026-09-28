// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A disjoint-set (union-find) structure over the integers <c>0 .. size-1</c>, with path compression
    /// and union by rank for near-constant-time operations. Tracks how many disjoint sets remain. Not
    /// thread-safe.
    /// </summary>
    public sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public DisjointSet(int size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must not be negative.");
            _parent = new int[size];
            _rank = new int[size];
            for (int i = 0; i < size; i++)
                _parent[i] = i;
            SetCount = size;
        }

        public int Size => _parent.Length;

        /// <summary>The number of disjoint sets remaining (starts equal to <see cref="Size"/>).</summary>
        public int SetCount { get; private set; }

        /// <summary>Returns the canonical representative of the set containing <paramref name="x"/>.</summary>
        public int Find(int x)
        {
            if ((uint)x >= (uint)_parent.Length)
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Element must be in [0, {_parent.Length}).");

            // Iterative find with full path compression (point every node on the path at the root).
            int root = x;
            while (_parent[root] != root)
                root = _parent[root];
            while (_parent[x] != root)
            {
                int next = _parent[x];
                _parent[x] = root;
                x = next;
            }
            return root;
        }

        /// <summary>Merges the sets containing <paramref name="a"/> and <paramref name="b"/>. Returns false if already merged.</summary>
        public bool Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra == rb)
                return false;

            // Attach the shorter tree under the taller to keep depth low.
            if (_rank[ra] < _rank[rb])
                (ra, rb) = (rb, ra);
            _parent[rb] = ra;
            if (_rank[ra] == _rank[rb])
                _rank[ra]++;

            SetCount--;
            return true;
        }

        /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are in the same set.</summary>
        public bool Connected(int a, int b) => Find(a) == Find(b);
    }
}
