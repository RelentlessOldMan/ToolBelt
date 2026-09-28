using System;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class FenwickTreeTests
    {
        public void PrefixAndRangeSums()
        {
            var t = new FenwickTree(5);
            for (int i = 0; i < 5; i++) t.Add(i, i + 1); // values 1,2,3,4,5

            Check.Equal(1L, t.PrefixSum(0));
            Check.Equal(6L, t.PrefixSum(2));   // 1+2+3
            Check.Equal(15L, t.PrefixSum(4));  // 1..5
            Check.Equal(9L, t.RangeSum(1, 3)); // 2+3+4
            Check.Equal(3L, t.ValueAt(2));
        }

        public void UpdatesAccumulate()
        {
            var t = new FenwickTree(3);
            t.Add(1, 10);
            t.Add(1, 5);
            Check.Equal(15L, t.ValueAt(1));
            Check.Equal(15L, t.PrefixSum(2));
        }

        public void NegativeDelta()
        {
            var t = new FenwickTree(3);
            t.Add(0, 10);
            t.Add(0, -4);
            Check.Equal(6L, t.ValueAt(0));
        }

        public void OutOfRange_Throws()
        {
            var t = new FenwickTree(3);
            Check.Throws<ArgumentOutOfRangeException>(() => t.Add(3, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => t.PrefixSum(3));
            Check.Throws<ArgumentException>(() => t.RangeSum(2, 1));
        }

        // Differential: mirror random Add operations against a plain array and compare prefix/range sums.
        public void Differential_MatchesNaiveArray()
        {
            var rng = new Random(1408);
            for (int trial = 0; trial < 300; trial++)
            {
                int n = rng.Next(1, 40);
                var tree = new FenwickTree(n);
                var array = new long[n];

                for (int op = 0; op < 100; op++)
                {
                    int idx = rng.Next(n);
                    long delta = rng.Next(-100, 100);
                    tree.Add(idx, delta);
                    array[idx] += delta;

                    int from = rng.Next(n);
                    int to = rng.Next(from, n);
                    long expected = 0;
                    for (int k = from; k <= to; k++) expected += array[k];
                    Check.Equal(expected, tree.RangeSum(from, to), $"trial {trial} op {op}: range [{from},{to}]");

                    long prefix = 0;
                    for (int k = 0; k <= to; k++) prefix += array[k];
                    Check.Equal(prefix, tree.PrefixSum(to), $"trial {trial} op {op}: prefix {to}");
                }
            }
        }
    }
}
