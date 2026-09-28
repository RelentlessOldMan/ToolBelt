using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class TopNTests
    {
        public void KeepsLargest()
        {
            var top = new TopN<int>(3);
            foreach (var x in new[] { 5, 1, 9, 3, 7, 2, 8 })
                top.Add(x);
            Check.True(top.ToArray().SequenceEqual(new[] { 9, 8, 7 }));
        }

        public void FewerThanCapacity()
        {
            var top = new TopN<int>(5);
            top.Add(2); top.Add(1);
            Check.True(top.ToArray().SequenceEqual(new[] { 2, 1 }));
            Check.Equal(2, top.Count);
        }

        public void CustomComparer_SmallestN()
        {
            // Reverse comparer -> "largest" becomes smallest, so we retain the N smallest.
            var top = new TopN<int>(2, Comparer<int>.Create((a, b) => b.CompareTo(a)));
            foreach (var x in new[] { 5, 1, 9, 3 })
                top.Add(x);
            Check.True(top.ToArray().SequenceEqual(new[] { 1, 3 }));
        }

        public void InvalidCapacity_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new TopN<int>(0));
        }

        // Differential: the retained items must equal the top-N of all items, sorted descending.
        public void Differential_MatchesSortedTopN()
        {
            var rng = new Random(4242);
            for (int trial = 0; trial < 500; trial++)
            {
                int capacity = rng.Next(1, 12);
                var top = new TopN<int>(capacity);
                var all = new List<int>();

                int n = rng.Next(0, 100);
                for (int i = 0; i < n; i++)
                {
                    int v = rng.Next(-50, 50); // small range → ties
                    top.Add(v);
                    all.Add(v);
                }

                var expected = all.OrderByDescending(x => x).Take(capacity).ToArray();
                Check.True(expected.SequenceEqual(top.ToArray()),
                    $"trial {trial} (n={n}, cap={capacity})");
            }
        }
    }
}
