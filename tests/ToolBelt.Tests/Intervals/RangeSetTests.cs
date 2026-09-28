using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Intervals;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Intervals
{
    public sealed class RangeSetTests
    {
        private static Interval<int> I(int s, int e) => new Interval<int>(s, e);

        public void NormalizeMergesOverlappingAndTouching()
        {
            var set = new RangeSet<int>(new[] { I(0, 5), I(5, 10), I(3, 7), I(20, 25) });
            Check.Equal(2, set.Count);                       // [0,10) and [20,25)
            Check.True(set.Intervals[0] == I(0, 10));
            Check.True(set.Intervals[1] == I(20, 25));
        }

        public void Contains()
        {
            var set = new RangeSet<int>(new[] { I(0, 10), I(20, 30) });
            Check.True(set.Contains(0));
            Check.True(set.Contains(9));
            Check.False(set.Contains(10));
            Check.False(set.Contains(15));
            Check.True(set.Contains(25));
        }

        public void Union()
        {
            var a = new RangeSet<int>(new[] { I(0, 10) });
            var b = new RangeSet<int>(new[] { I(5, 20) });
            Check.True(a.Union(b).Intervals.SequenceEqual(new[] { I(0, 20) }));
        }

        public void Intersect()
        {
            var a = new RangeSet<int>(new[] { I(0, 10), I(20, 30) });
            var b = new RangeSet<int>(new[] { I(5, 25) });
            Check.True(a.Intersect(b).Intervals.SequenceEqual(new[] { I(5, 10), I(20, 25) }));
        }

        public void Except()
        {
            var a = new RangeSet<int>(new[] { I(0, 100) });
            var b = new RangeSet<int>(new[] { I(10, 20), I(30, 40) });
            Check.True(a.Except(b).Intervals.SequenceEqual(new[] { I(0, 10), I(20, 30), I(40, 100) }));
        }

        public void Complement()
        {
            var set = new RangeSet<int>(new[] { I(10, 20) });
            Check.True(set.Complement(I(0, 30)).Intervals.SequenceEqual(new[] { I(0, 10), I(20, 30) }));
        }

        public void EmptyIntervalsIgnored()
        {
            var set = new RangeSet<int>(new[] { I(5, 5), I(0, 10) });
            Check.Equal(1, set.Count);
        }

        // Differential: every set operation must match boolean set algebra on a discretized domain.
        public void Property_MatchesBitsetAlgebra()
        {
            var rng = new Random(33);
            const int D = 40;
            for (int trial = 0; trial < 1000; trial++)
            {
                var (setA, bitsA) = RandomSet(rng, D);
                var (setB, bitsB) = RandomSet(rng, D);

                CheckAgrees(setA.Union(setB), Combine(bitsA, bitsB, (x, y) => x || y), $"trial {trial}: union");
                CheckAgrees(setA.Intersect(setB), Combine(bitsA, bitsB, (x, y) => x && y), $"trial {trial}: intersect");
                CheckAgrees(setA.Except(setB), Combine(bitsA, bitsB, (x, y) => x && !y), $"trial {trial}: except");

                var universe = I(0, D);
                var compBits = bitsA.Select(x => !x).ToArray();
                CheckAgrees(setA.Complement(universe), compBits, $"trial {trial}: complement");
            }
        }

        private static (RangeSet<int>, bool[]) RandomSet(Random rng, int d)
        {
            var bits = new bool[d];
            var intervals = new List<Interval<int>>();
            int count = rng.Next(0, 5);
            for (int k = 0; k < count; k++)
            {
                int s = rng.Next(0, d);
                int e = rng.Next(s, d + 1);
                intervals.Add(new Interval<int>(s, e));
                for (int x = s; x < e && x < d; x++) bits[x] = true;
            }
            return (new RangeSet<int>(intervals), bits);
        }

        private static bool[] Combine(bool[] a, bool[] b, Func<bool, bool, bool> op)
        {
            var r = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = op(a[i], b[i]);
            return r;
        }

        private static void CheckAgrees(RangeSet<int> set, bool[] expected, string message)
        {
            for (int i = 0; i < expected.Length; i++)
                Check.Equal(expected[i], set.Contains(i), $"{message} at {i}");
        }
    }
}
