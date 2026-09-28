using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class AsyncLockTests
    {
        public async Task ProvidesMutualExclusion()
        {
            var gate = new AsyncLock();
            int counter = 0;
            int inside = 0;
            int maxInside = 0;

            var tasks = Enumerable.Range(0, 100).Select(async _ =>
            {
                using (await gate.LockAsync())
                {
                    int cur = Interlocked.Increment(ref inside);
                    if (cur > maxInside) maxInside = cur; // safe: only one task is ever here
                    await Task.Yield();
                    int snapshot = counter;   // read-modify-write that races without the lock
                    await Task.Delay(1);
                    counter = snapshot + 1;
                    Interlocked.Decrement(ref inside);
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            Check.Equal(100, counter);   // no lost updates
            Check.Equal(1, maxInside);   // never more than one holder
        }

        public void TryLock_SucceedsOnlyWhenFree()
        {
            var gate = new AsyncLock();

            var first = gate.TryLock();
            Check.NotNull(first);
            Check.Null(gate.TryLock()); // already held

            first!.Dispose();           // release
            var again = gate.TryLock();
            Check.NotNull(again);
            again!.Dispose();
        }

        public async Task Cancellation_WhileWaiting_Throws()
        {
            var gate = new AsyncLock();
            var held = await gate.LockAsync(); // hold the lock

            using var cts = new CancellationTokenSource();
            var waiting = gate.LockAsync(cts.Token);
            cts.Cancel();

            await Check.ThrowsAsync<OperationCanceledException>(() => waiting);
            held.Dispose();
        }

        public async Task DoubleDispose_OfHandle_IsSafe()
        {
            var gate = new AsyncLock();
            var handle = await gate.LockAsync();
            handle.Dispose();
            handle.Dispose(); // must not over-release the semaphore

            // If the double release had corrupted the count, this second acquisition path would misbehave;
            // acquire-and-release once more to confirm the lock still enforces a single holder.
            var again = gate.TryLock();
            Check.NotNull(again);
            Check.Null(gate.TryLock());
            again!.Dispose();
        }

        public async Task Disposed_Lock_Throws()
        {
            var gate = new AsyncLock();
            gate.Dispose();
            await Check.ThrowsAsync<ObjectDisposedException>(() => gate.LockAsync());
            Check.Throws<ObjectDisposedException>(() => gate.TryLock());
        }
    }
}
