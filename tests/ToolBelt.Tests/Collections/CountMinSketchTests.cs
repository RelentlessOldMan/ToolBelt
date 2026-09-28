using System;
using System.Collections.Generic;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class CountMinSketchTests
    {
        public void EstimatesExactWhenNoCollisions()
        {
            var sketch = new CountMinSketch<string>(width: 4096, depth: 5);
            sketch.Add("apple", 3);
            sketch.Add("banana");
            sketch.Add("apple");

            Check.Equal(4L, sketch.Estimate("apple"));   // 3 + 1
            Check.Equal(1L, sketch.Estimate("banana"));
            Check.Equal(5L, sketch.TotalCount);           // 3 + 1 + 1
        }

        public void UnseenItemEstimatesZero()
        {
            var sketch = new CountMinSketch<string>();
            sketch.Add("x");
            Check.Equal(0L, sketch.Estimate("never-added"));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(width: 0));
            Check.Throws<ArgumentOutOfRangeException>(() => new CountMinSketch<int>(depth: 0));
            var s = new CountMinSketch<int>();
            Check.Throws<ArgumentOutOfRangeException>(() => s.Add(1, -1));
        }

        // Core guarantee: estimates NEVER under-count the true frequency, for every key.
        public void Property_NeverUnderestimates()
        {
            var rng = new Random(29);
            var truth = new Dictionary<int, long>();
            var sketch = new CountMinSketch<int>(width: 512, depth: 4);

            for (int i = 0; i < 50000; i++)
            {
                int key = rng.Next(0, 2000);
                sketch.Add(key);
                truth[key] = truth.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            foreach (var kv in truth)
                Check.True(sketch.Estimate(kv.Key) >= kv.Value,
                    $"under-estimated key {kv.Key}: est < {kv.Value}");
        }

        // With generous width/depth relative to distinct keys, over-count should be modest on average.
        public void Property_ReasonablyAccurateWhenWide()
        {
            var rng = new Random(7);
            var truth = new Dictionary<int, long>();
            var sketch = new CountMinSketch<int>(width: 8192, depth: 6);

            for (int i = 0; i < 20000; i++)
            {
                int key = rng.Next(0, 500);
                sketch.Add(key);
                truth[key] = truth.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            long totalError = 0;
            foreach (var kv in truth)
                totalError += sketch.Estimate(kv.Key) - kv.Value;

            double avgError = (double)totalError / truth.Count;
            Check.True(avgError < 5.0, $"average over-count too high: {avgError}");
        }
    }
}
