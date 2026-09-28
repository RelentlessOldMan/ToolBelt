using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Intervals;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Intervals
{
    public sealed class IntervalTreeTests
    {
        private static KeyValuePair<Interval<int>, string> Iv(int s, int e, string v)
            => new KeyValuePair<Interval<int>, string>(new Interval<int>(s, e), v);

        public void PointQuery()
        {
            var tree = new IntervalTree<int, string>(new[]
            {
                Iv(0, 10, "a"), Iv(5, 15, "b"), Iv(20, 30, "c"),
            });
            Check.True(tree.Query(7).Select(kv => kv.Value).OrderBy(x => x).SequenceEqual(new[] { "a", "b" }));
            Check.True(tree.Query(10).Select(kv => kv.Value).SequenceEqual(new[] { "b" })); // half-open: 10 not in [0,10)
            Check.Equal(0, tree.Query(18).Count);
        }

        public void RangeQuery()
        {
            var tree = new IntervalTree<int, string>(new[]
            {
                Iv(0, 10, "a"), Iv(5, 15, "b"), Iv(20, 30, "c"),
            });
            var hits = tree.Query(new Interval<int>(12, 22)).Select(kv => kv.Value).OrderBy(x => x).ToList();
            Check.True(hits.SequenceEqual(new[] { "b", "c" }));
        }

        public void Empty()
        {
            var tree = new IntervalTree<int, string>(Array.Empty<KeyValuePair<Interval<int>, string>>());
            Check.Equal(0, tree.Count);
            Check.Equal(0, tree.Query(0).Count);
        }

        // Differential: match a naive linear scan over randomized interval sets, for both query kinds.
        public void Property_MatchesLinearScan()
        {
            var rng = new Random(32);
            for (int trial = 0; trial < 1000; trial++)
            {
                int n = rng.Next(0, 40);
                var items = new List<KeyValuePair<Interval<int>, int>>();
                for (int k = 0; k < n; k++)
                {
                    int s = rng.Next(0, 100);
                    int len = rng.Next(0, 20);
                    items.Add(new KeyValuePair<Interval<int>, int>(new Interval<int>(s, s + len), k));
                }
                var tree = new IntervalTree<int, int>(items);

                for (int q = 0; q < 5; q++)
                {
                    int point = rng.Next(-5, 105);
                    var expected = items.Where(it => it.Key.Contains(point)).Select(it => it.Value).OrderBy(x => x);
                    var actual = tree.Query(point).Select(kv => kv.Value).OrderBy(x => x);
                    Check.True(expected.SequenceEqual(actual), $"trial {trial}: point {point}");

                    int rs = rng.Next(-5, 105);
                    var range = new Interval<int>(rs, rs + rng.Next(0, 30));
                    var expR = items.Where(it => it.Key.Overlaps(range)).Select(it => it.Value).OrderBy(x => x);
                    var actR = tree.Query(range).Select(kv => kv.Value).OrderBy(x => x);
                    Check.True(expR.SequenceEqual(actR), $"trial {trial}: range {range}");
                }
            }
        }
    }
}
