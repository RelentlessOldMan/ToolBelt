using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class BatchTests
    {
        public void Flatten_Reconstructs_Source()
        {
            var source = Enumerable.Range(0, 23).ToList();
            var flattened = source.Batch(5).SelectMany(b => b).ToList();
            Check.True(source.SequenceEqual(flattened), "flatten(batches) must equal source");
        }

        public void AllButLast_HaveExactSize()
        {
            var batches = Enumerable.Range(0, 23).Batch(5).ToList();
            for (int i = 0; i < batches.Count - 1; i++)
                Check.Equal(5, batches[i].Count);
            Check.Equal(3, batches[^1].Count); // 23 = 4*5 + 3
        }

        public void BatchCount_IsCeilingDivision()
        {
            Check.Equal(5, Enumerable.Range(0, 23).Batch(5).Count());
            Check.Equal(4, Enumerable.Range(0, 20).Batch(5).Count());
        }

        public void EmptySource_ProducesNoBatches()
        {
            Check.Equal(0, Enumerable.Empty<int>().Batch(3).Count());
        }

        public void InvalidSize_Throws_Eagerly()
        {
            // Eager: the exception must surface on the call, not deferred to enumeration.
            Check.Throws<ArgumentOutOfRangeException>(() => Enumerable.Range(0, 3).Batch(0));
        }

        public void NullSource_Throws()
        {
            IEnumerable<int> source = null!;
            Check.Throws<ArgumentNullException>(() => source.Batch(3));
        }

        public void Differential_MatchesNaiveReference_OverRandomTrials()
        {
            var rng = new Random(1234); // fixed seed → deterministic
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.Next(0, 200);
                int size = rng.Next(1, 40);
                var source = Enumerable.Range(0, n).Select(_ => rng.Next()).ToList();

                var actual = source.Batch(size).Select(b => b.ToArray()).ToList();
                var expected = NaiveBatch(source, size);

                Check.Equal(expected.Count, actual.Count, $"trial {trial}: batch count (n={n}, size={size})");
                for (int i = 0; i < expected.Count; i++)
                    Check.True(expected[i].SequenceEqual(actual[i]), $"trial {trial}: batch {i} contents");
            }
        }

        // Deliberately dumb reference: build each batch with an index-window copy.
        private static List<int[]> NaiveBatch(List<int> source, int size)
        {
            var result = new List<int[]>();
            for (int start = 0; start < source.Count; start += size)
            {
                int count = Math.Min(size, source.Count - start);
                var batch = new int[count];
                for (int j = 0; j < count; j++)
                    batch[j] = source[start + j];
                result.Add(batch);
            }
            return result;
        }
    }
}
