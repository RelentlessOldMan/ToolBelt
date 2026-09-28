using System;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class DisjointSetTests
    {
        public void StartsFullyDisjoint()
        {
            var ds = new DisjointSet(5);
            Check.Equal(5, ds.SetCount);
            Check.False(ds.Connected(0, 1));
        }

        public void UnionAndConnected()
        {
            var ds = new DisjointSet(5);
            Check.True(ds.Union(0, 1));
            Check.True(ds.Union(1, 2));
            Check.True(ds.Connected(0, 2)); // transitive
            Check.False(ds.Connected(0, 3));
            Check.Equal(3, ds.SetCount); // {0,1,2},{3},{4}
        }

        public void RedundantUnion_ReturnsFalse()
        {
            var ds = new DisjointSet(3);
            Check.True(ds.Union(0, 1));
            Check.False(ds.Union(1, 0)); // already connected
            Check.Equal(2, ds.SetCount);
        }

        public void OutOfRange_Throws()
        {
            var ds = new DisjointSet(3);
            Check.Throws<ArgumentOutOfRangeException>(() => ds.Find(3));
        }

        // Differential: mirror random unions against a naive label-array model and compare Connected for
        // all pairs plus the set count after every union.
        public void Differential_MatchesNaiveModel()
        {
            var rng = new Random(555);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = rng.Next(1, 25);
                var ds = new DisjointSet(n);
                var label = new int[n];
                for (int i = 0; i < n; i++) label[i] = i;
                int naiveSetCount = n;

                for (int op = 0; op < 40; op++)
                {
                    int a = rng.Next(n), b = rng.Next(n);

                    bool actual = ds.Union(a, b);
                    // Naive union: relabel every member of b's label to a's label.
                    bool expected = label[a] != label[b];
                    if (expected)
                    {
                        int from = label[b], to = label[a];
                        for (int i = 0; i < n; i++)
                            if (label[i] == from) label[i] = to;
                        naiveSetCount--;
                    }
                    Check.Equal(expected, actual, $"trial {trial} op {op}: union({a},{b})");
                    Check.Equal(naiveSetCount, ds.SetCount, $"trial {trial} op {op}: set count");

                    // Verify connectivity for every pair.
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                            Check.Equal(label[i] == label[j], ds.Connected(i, j),
                                $"trial {trial} op {op}: connected({i},{j})");
                }
            }
        }
    }
}
