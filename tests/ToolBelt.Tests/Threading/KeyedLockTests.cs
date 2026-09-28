using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class KeyedLockTests
    {
        public async Task SameKey_Serializes()
        {
            var keyed = new KeyedLock<string>();
            int inside = 0, maxInside = 0, counter = 0;

            var tasks = Enumerable.Range(0, 50).Select(async _ =>
            {
                using (await keyed.LockAsync("shared"))
                {
                    int cur = Interlocked.Increment(ref inside);
                    if (cur > maxInside) maxInside = cur;
                    await Task.Yield();
                    int snapshot = counter;
                    await Task.Delay(1);
                    counter = snapshot + 1;
                    Interlocked.Decrement(ref inside);
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            Check.Equal(50, counter);
            Check.Equal(1, maxInside);
        }

        public async Task DifferentKeys_RunConcurrently()
        {
            var keyed = new KeyedLock<string>();
            var a = await keyed.LockAsync("a");
            // Acquiring a different key must not block behind "a".
            var b = keyed.LockAsync("b");
            var completed = await Task.WhenAny(b, Task.Delay(1000));
            Check.True(completed == b, "different key should acquire without waiting");
            (await b).Dispose();
            a.Dispose();
        }

        public async Task SameKey_SecondWaitsUntilRelease()
        {
            var keyed = new KeyedLock<string>();
            var first = await keyed.LockAsync("k");
            var second = keyed.LockAsync("k");

            await Task.Delay(20);
            Check.False(second.IsCompleted, "second acquisition must wait while the key is held");

            first.Dispose();
            var handle = await second;
            handle.Dispose();
        }

        public async Task Entries_AreReclaimed_AfterRelease()
        {
            var keyed = new KeyedLock<int>();
            for (int i = 0; i < 20; i++)
            {
                using (await keyed.LockAsync(i)) { }
            }
            Check.Equal(0, keyed.ActiveKeyCount); // no leak
        }

        public async Task Cancellation_WhileWaiting_Throws_AndReclaims()
        {
            var keyed = new KeyedLock<string>();
            var held = await keyed.LockAsync("k");

            using var cts = new CancellationTokenSource();
            var waiting = keyed.LockAsync("k", cts.Token);
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => waiting);

            held.Dispose();
            Check.Equal(0, keyed.ActiveKeyCount);
        }

        public async Task NullKey_Throws()
        {
            var keyed = new KeyedLock<string>();
            await Check.ThrowsAsync<ArgumentNullException>(() => keyed.LockAsync(null!));
        }
    }
}
