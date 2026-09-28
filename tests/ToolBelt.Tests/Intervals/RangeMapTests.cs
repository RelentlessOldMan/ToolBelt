using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Intervals;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Intervals
{
    public sealed class RangeMapTests
    {
        public void BasicAssignAndLookup()
        {
            var map = new RangeMap<int, string>();
            map.Set(0, 10, "low");
            map.Set(10, 20, "high");
            Check.True(map.TryGet(5, out var a) && a == "low");
            Check.True(map.TryGet(15, out var b) && b == "high");
            Check.False(map.TryGet(20, out _)); // half-open
            Check.False(map.TryGet(-1, out _));
        }

        public void OverwriteSplitsMiddle()
        {
            var map = new RangeMap<int, string>();
            map.Set(0, 100, "base");
            map.Set(40, 60, "override");
            Check.True(map.TryGet(20, out var l) && l == "base");
            Check.True(map.TryGet(50, out var m) && m == "override");
            Check.True(map.TryGet(80, out var r) && r == "base");
            Check.Equal(3, map.SegmentCount); // [0,40)base [40,60)override [60,100)base
        }

        public void AdjacentEqualValuesCoalesce()
        {
            var map = new RangeMap<int, string>();
            map.Set(0, 10, "x");
            map.Set(10, 20, "x");
            Check.Equal(1, map.SegmentCount); // coalesced into [0,20)
        }

        public void EmptyRangeIsNoOp_AndInvalidThrows()
        {
            var map = new RangeMap<int, string>();
            map.Set(5, 5, "nothing");
            Check.Equal(0, map.SegmentCount);
            Check.Throws<ArgumentException>(() => map.Set(10, 5, "bad"));
        }

        public void Remove()
        {
            var map = new RangeMap<int, string>();
            map.Set(0, 100, "v");
            map.Remove(40, 60);
            Check.True(map.TryGet(20, out _));
            Check.False(map.TryGet(50, out _));
            Check.Equal(2, map.SegmentCount);
        }

        // Property: after any random sequence of Set operations, lookups match a "last write wins" model
        // over a discretized domain, and the segment list is always a minimal coalesced disjoint cover.
        public void Property_MatchesModelAndStaysMinimal()
        {
            var rng = new Random(320);
            const int D = 25;
            for (int trial = 0; trial < 800; trial++)
            {
                var map = new RangeMap<int, int>();
                var model = new int?[D];

                int ops = rng.Next(1, 12);
                for (int o = 0; o < ops; o++)
                {
                    int s = rng.Next(0, D);
                    int e = rng.Next(s, D + 1);
                    int v = rng.Next(0, 3); // few values so coalescing happens
                    map.Set(s, e, v);
                    for (int k = s; k < e; k++) model[k] = v;
                }

                for (int k = 0; k < D; k++)
                {
                    bool hit = map.TryGet(k, out int got);
                    Check.Equal(model[k].HasValue, hit, $"trial {trial}: presence at {k}");
                    if (model[k].HasValue) Check.Equal(model[k]!.Value, got, $"trial {trial}: value at {k}");
                }

                AssertMinimalCover(map, trial);
            }
        }

        private static void AssertMinimalCover(RangeMap<int, int> map, int trial)
        {
            var segs = map.Segments.ToList();
            for (int i = 0; i < segs.Count; i++)
            {
                Check.True(segs[i].Key.Start < segs[i].Key.End, $"trial {trial}: empty segment");
                if (i > 0)
                {
                    var prev = segs[i - 1];
                    Check.True(prev.Key.End <= segs[i].Key.Start, $"trial {trial}: overlap/disorder");
                    // touching segments must not share a value (else they should have coalesced)
                    if (prev.Key.End == segs[i].Key.Start)
                        Check.True(prev.Value != segs[i].Value, $"trial {trial}: uncoalesced equal neighbors");
                }
            }
        }
    }
}
