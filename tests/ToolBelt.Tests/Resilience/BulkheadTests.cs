using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Resilience
{
    public sealed class BulkheadTests
    {
        public async Task LimitsConcurrency()
        {
            const int limit = 3;
            using var bulkhead = new Bulkhead(limit);
            int current = 0, observedMax = 0;

            var tasks = Enumerable.Range(0, 50).Select(_ => bulkhead.ExecuteAsync(async ct =>
            {
                int now = Interlocked.Increment(ref current);
                // Track the peak concurrency without a lock via CAS.
                int seen;
                do { seen = observedMax; } while (now > seen && Interlocked.CompareExchange(ref observedMax, now, seen) != seen);
                await Task.Delay(5, ct);
                Interlocked.Decrement(ref current);
            })).ToArray();

            await Task.WhenAll(tasks);
            Check.True(observedMax <= limit, $"observed {observedMax} concurrent, limit {limit}");
            Check.True(observedMax >= 2, "expected some real concurrency");
        }

        public async Task ReturnsResult()
        {
            using var bulkhead = new Bulkhead(2);
            int result = await bulkhead.ExecuteAsync(_ => Task.FromResult(21 * 2));
            Check.Equal(42, result);
        }

        public async Task ReleasesSlotOnException()
        {
            using var bulkhead = new Bulkhead(1);
            await Check.ThrowsAsync<InvalidOperationException>(() =>
                bulkhead.ExecuteAsync<int>(_ => throw new InvalidOperationException("boom")));

            // If the slot leaked, this second call would deadlock; it must run.
            int ok = await bulkhead.ExecuteAsync(_ => Task.FromResult(1));
            Check.Equal(1, ok);
            Check.Equal(1, bulkhead.AvailableSlots);
        }

        public async Task Cancellation_WhileWaiting_Throws()
        {
            using var bulkhead = new Bulkhead(1);
            // Occupy the only slot with a task we control.
            var gate = new TaskCompletionSource<bool>();
            var holding = bulkhead.ExecuteAsync(async _ => { await gate.Task; return 0; });

            using var cts = new CancellationTokenSource();
            var waiting = bulkhead.ExecuteAsync(_ => Task.FromResult(1), cts.Token);
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => waiting);

            gate.SetResult(true);
            await holding;
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new Bulkhead(0));
        }
    }
}
