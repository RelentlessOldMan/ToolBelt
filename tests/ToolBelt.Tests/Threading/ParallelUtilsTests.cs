using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class ParallelUtilsTests
    {
        public async Task ProcessesEveryItem()
        {
            var seen = new ConcurrentBag<int>();
            await ParallelUtils.ForEachAsync(Enumerable.Range(0, 100), 8,
                async (i, _) => { await Task.Yield(); seen.Add(i); });
            Check.Equal(100, seen.Count);
            Check.True(seen.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 100)));
        }

        public async Task RespectsConcurrencyCap()
        {
            int current = 0, max = 0;
            var gate = new object();
            await ParallelUtils.ForEachAsync(Enumerable.Range(0, 40), 4, async (_, __) =>
            {
                int now = Interlocked.Increment(ref current);
                lock (gate) if (now > max) max = now;
                await Task.Delay(15);
                Interlocked.Decrement(ref current);
            });
            Check.True(max <= 4, $"observed concurrency {max} exceeded cap 4");
        }

        public async Task SerialWhenCapIsOne()
        {
            int current = 0, max = 0;
            var gate = new object();
            await ParallelUtils.ForEachAsync(Enumerable.Range(0, 20), 1, async (_, __) =>
            {
                int now = Interlocked.Increment(ref current);
                lock (gate) if (now > max) max = now;
                await Task.Delay(5);
                Interlocked.Decrement(ref current);
            });
            Check.Equal(1, max); // cap of 1 is strictly serial (deterministic)
        }

        public async Task CollectAllExceptions()
        {
            var ex = await Check.ThrowsAsync<AggregateException>(() =>
                ParallelUtils.ForEachAsync(Enumerable.Range(0, 6), 3, async (i, _) =>
                {
                    await Task.Yield();
                    if (i % 2 == 0) throw new InvalidOperationException("even " + i);
                }, collectAllExceptions: true));
            Check.Equal(3, ex.InnerExceptions.Count); // 0, 2, 4
        }

        public async Task FailFastStops()
        {
            var processed = new ConcurrentBag<int>();
            var ex = await Check.ThrowsAsync<AggregateException>(() =>
                ParallelUtils.ForEachAsync(Enumerable.Range(0, 50), 1, async (i, _) =>
                {
                    await Task.Yield();
                    if (i == 3) throw new InvalidOperationException("stop");
                    processed.Add(i);
                }, collectAllExceptions: false));
            Check.True(ex.InnerExceptions.Count >= 1);
            Check.True(processed.Count < 50, "fail-fast should not process every item");
        }

        public async Task InvalidArguments_Throw()
        {
            await Check.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                ParallelUtils.ForEachAsync(new[] { 1 }, 0, (_, __) => Task.CompletedTask));
        }
    }
}
