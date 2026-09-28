using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class RandomUtilsTests
    {
        public void NextDoubleRange()
        {
            var r = new DeterministicRandom(1);
            for (int i = 0; i < 1000; i++)
            {
                double d = r.NextDouble(-2, 5);
                Check.True(d >= -2 && d < 5, $"out of range: {d}");
            }
        }

        public void ShuffleIsPermutation()
        {
            var r = new DeterministicRandom(2);
            var list = Enumerable.Range(0, 50).ToList();
            r.Shuffle(list);
            Check.True(list.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 50)));
        }

        public void SampleWithoutReplacementDistinct()
        {
            var r = new DeterministicRandom(3);
            var sample = r.SampleWithoutReplacement(Enumerable.Range(0, 100).ToList(), 10);
            Check.Equal(10, sample.Length);
            Check.Equal(10, sample.Distinct().Count());
            Check.True(sample.All(x => x >= 0 && x < 100));
        }

        public void SampleWithoutReplacementBounds()
        {
            var r = new DeterministicRandom(3);
            Check.Equal(0, r.SampleWithoutReplacement(new[] { 1, 2, 3 }, 0).Length);
            Check.Equal(3, r.SampleWithoutReplacement(new[] { 1, 2, 3 }, 3).Length);
            Check.Throws<ArgumentOutOfRangeException>(() => r.SampleWithoutReplacement(new[] { 1 }, 2));
        }

        public void WeightedChoiceDeterministicExtremes()
        {
            var r = new DeterministicRandom(4);
            // Only the last item has weight -> always chosen.
            for (int i = 0; i < 100; i++)
                Check.Equal("c", r.WeightedChoice(new[] { "a", "b", "c" }, new double[] { 0, 0, 1 }));
        }

        public void WeightedChoiceProportions()
        {
            var r = new DeterministicRandom(5);
            int b = 0;
            const int n = 40000;
            for (int i = 0; i < n; i++)
                if (r.WeightedChoice(new[] { "a", "b" }, new double[] { 1, 3 }) == "b") b++;
            double ratio = (double)b / n;
            Check.True(Math.Abs(ratio - 0.75) < 0.02, $"ratio {ratio} not near 0.75");
        }

        // Statistical uniformity: over many shuffles, each element lands in each position about equally.
        public void Property_ShuffleUniformByPosition()
        {
            const int k = 5, trials = 40000;
            var r = new DeterministicRandom(99);
            var counts = new int[k, k]; // counts[element, position]
            for (int t = 0; t < trials; t++)
            {
                var list = Enumerable.Range(0, k).ToList();
                r.Shuffle(list);
                for (int pos = 0; pos < k; pos++) counts[list[pos], pos]++;
            }
            double expected = (double)trials / k;
            for (int e = 0; e < k; e++)
                for (int p = 0; p < k; p++)
                    Check.True(Math.Abs(counts[e, p] - expected) < expected * 0.1,
                        $"element {e} at position {p}: {counts[e, p]} vs ~{expected}");
        }

        public void GaussianAndExponentialStats()
        {
            var r = new DeterministicRandom(7);
            const int n = 200000;
            double sum = 0, sumSq = 0;
            for (int i = 0; i < n; i++) { double g = r.NextGaussian(10, 2); sum += g; sumSq += g * g; }
            double mean = sum / n;
            double variance = sumSq / n - mean * mean;
            Check.True(Math.Abs(mean - 10) < 0.05, $"gaussian mean {mean}");
            Check.True(Math.Abs(Math.Sqrt(variance) - 2) < 0.05, $"gaussian stddev {Math.Sqrt(variance)}");

            double esum = 0;
            for (int i = 0; i < n; i++) esum += r.NextExponential(2.0);
            Check.True(Math.Abs(esum / n - 0.5) < 0.02, "exponential mean should be 1/rate = 0.5");
        }

        public void InvalidWeights_Throw()
        {
            var r = new DeterministicRandom(1);
            Check.Throws<ArgumentException>(() => r.WeightedChoice(new[] { "a" }, new double[] { 0 }));
            Check.Throws<ArgumentException>(() => r.WeightedChoice(new[] { "a", "b" }, new double[] { 1 }));
        }
    }
}
