using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class BatchHardeningTests
    {
        public void SizeLargerThanSource_YieldsSingleBatch()
        {
            var batches = Enumerable.Range(0, 3).Batch(100).ToList();
            Check.Equal(1, batches.Count);
            Check.Equal(3, batches[0].Count);
        }

        public void SizeOne_YieldsSingletonBatches()
        {
            var batches = Enumerable.Range(0, 4).Batch(1).ToList();
            Check.Equal(4, batches.Count);
            Check.True(batches.All(b => b.Count == 1));
        }

        public void ExactMultiple_HasNoShortTail()
        {
            var batches = Enumerable.Range(0, 10).Batch(5).ToList();
            Check.Equal(2, batches.Count);
            Check.True(batches.All(b => b.Count == 5));
        }

        public void Source_IsEnumeratedLazily_AndOnlyOnce()
        {
            var counter = new CountingEnumerable(Enumerable.Range(0, 10));

            // Materializing only the first batch must not drain the whole source.
            var first = counter.Batch(3).First();

            Check.Equal(3, first.Count);
            // 3 pulled to fill the batch + 1 that trips Count==size before yield => 3 (bucket yields at 3).
            Check.True(counter.Pulled <= 3, $"expected lazy pull <= 3, got {counter.Pulled}");
            Check.Equal(1, counter.EnumerationCount);
        }

        private sealed class CountingEnumerable : IEnumerable<int>
        {
            private readonly IEnumerable<int> _inner;
            public CountingEnumerable(IEnumerable<int> inner) => _inner = inner;
            public int Pulled { get; private set; }
            public int EnumerationCount { get; private set; }

            public IEnumerator<int> GetEnumerator()
            {
                EnumerationCount++;
                foreach (var item in _inner)
                {
                    Pulled++;
                    yield return item;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
