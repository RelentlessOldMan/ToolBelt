using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class SamplingTests
    {
        public void Shuffle_PreservesMultiset()
        {
            var rng = new Random(1);
            for (int trial = 0; trial < 500; trial++)
            {
                var list = Enumerable.Range(0, rng.Next(0, 50)).ToList();
                var original = list.ToList();
                Sampling.Shuffle(list, rng);
                // Same elements, possibly reordered.
                Check.True(original.OrderBy(x => x).SequenceEqual(list.OrderBy(x => x)),
                    $"trial {trial}: multiset changed");
            }
        }

        public void Shuffle_IsDeterministicForSeed()
        {
            var a = Enumerable.Range(0, 100).ToList();
            var b = Enumerable.Range(0, 100).ToList();
            Sampling.Shuffle(a, new Random(42));
            Sampling.Shuffle(b, new Random(42));
            Check.True(a.SequenceEqual(b), "same seed must produce same shuffle");
        }

        public void WeightedPick_ZeroWeightNeverChosen()
        {
            var rng = new Random(7);
            var weights = new double[] { 0, 1 };
            for (int i = 0; i < 1000; i++)
                Check.Equal(1, Sampling.WeightedPickIndex(weights, rng));
        }

        public void WeightedPick_ApproximatesDistribution()
        {
            var rng = new Random(12345);
            var weights = new double[] { 1, 3 }; // expect ~25% / ~75%
            int[] counts = new int[2];
            const int draws = 40000;
            for (int i = 0; i < draws; i++)
                counts[Sampling.WeightedPickIndex(weights, rng)]++;

            double p0 = (double)counts[0] / draws;
            Check.Close(0.25, p0, 0.02, "index-0 frequency"); // seeded → stable
        }

        public void WeightedPick_ByItem()
        {
            var rng = new Random(3);
            var picked = Sampling.WeightedPick(new[] { "a", "b" }, new double[] { 0, 1 }, rng);
            Check.Equal("b", picked);
        }

        public void InvalidArguments_Throw()
        {
            var rng = new Random(0);
            Check.Throws<ArgumentNullException>(() => Sampling.Shuffle<int>(null!, rng));
            Check.Throws<ArgumentException>(() => Sampling.WeightedPickIndex(new double[] { 0, 0 }, rng)); // zero sum
            Check.Throws<ArgumentException>(() => Sampling.WeightedPickIndex(new double[] { 1, -1 }, rng)); // negative
            Check.Throws<ArgumentException>(() => Sampling.WeightedPickIndex(Array.Empty<double>(), rng));
            Check.Throws<ArgumentException>(() => Sampling.WeightedPick(new[] { "a" }, new double[] { 1, 2 }, rng)); // length mismatch
        }
    }
}
