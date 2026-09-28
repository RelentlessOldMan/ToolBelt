using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class AsyncManualResetEventTests
    {
        public async Task InitiallySet_CompletesImmediately()
        {
            var mre = new AsyncManualResetEvent(initiallySet: true);
            Check.True(mre.IsSet);
            await mre.WaitAsync(); // must not block
        }

        public async Task Set_ReleasesWaiters()
        {
            var mre = new AsyncManualResetEvent();
            Check.False(mre.IsSet);

            var waiter = mre.WaitAsync();
            await Task.Delay(20);
            Check.False(waiter.IsCompleted, "waiter should block before Set");

            mre.Set();
            await waiter; // completes now
            Check.True(mre.IsSet);
        }

        public async Task MultipleWaiters_AllReleased()
        {
            var mre = new AsyncManualResetEvent();
            var waiters = Enumerable.Range(0, 20).Select(_ => mre.WaitAsync()).ToArray();
            mre.Set();
            await Task.WhenAll(waiters); // all complete
        }

        public async Task Reset_MakesFutureWaitersBlockAgain()
        {
            var mre = new AsyncManualResetEvent();
            mre.Set();
            await mre.WaitAsync(); // completes

            mre.Reset();
            Check.False(mre.IsSet);

            var waiter = mre.WaitAsync();
            await Task.Delay(20);
            Check.False(waiter.IsCompleted, "after Reset, new waiters block");

            mre.Set();
            await waiter;
        }

        public async Task Cancellation_WhileWaiting_Throws()
        {
            var mre = new AsyncManualResetEvent();
            using var cts = new CancellationTokenSource();
            var waiter = mre.WaitAsync(cts.Token);
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => waiter);
        }
    }
}
