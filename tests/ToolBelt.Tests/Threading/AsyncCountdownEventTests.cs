using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class AsyncCountdownEventTests
    {
        public async Task ZeroInitial_IsAlreadySet()
        {
            var cde = new AsyncCountdownEvent(0);
            Check.True(cde.IsSet);
            await cde.WaitAsync();
        }

        public async Task CountsDownToZero()
        {
            var cde = new AsyncCountdownEvent(3);
            var waiter = cde.WaitAsync();

            cde.Signal();
            cde.Signal();
            await Task.Delay(20);
            Check.False(waiter.IsCompleted, "should still be waiting at count 1");

            cde.Signal();
            await waiter;
            Check.True(cde.IsSet);
            Check.Equal(0, cde.CurrentCount);
        }

        public void SignalMultiple()
        {
            var cde = new AsyncCountdownEvent(5);
            cde.Signal(5);
            Check.True(cde.IsSet);
        }

        public void AddCount_RaisesCount()
        {
            var cde = new AsyncCountdownEvent(1);
            cde.AddCount(2);
            Check.Equal(3, cde.CurrentCount);
        }

        public void SignalPastZero_Throws()
        {
            var cde = new AsyncCountdownEvent(1);
            cde.Signal();
            Check.Throws<InvalidOperationException>(() => cde.Signal());       // already zero
            Check.Throws<InvalidOperationException>(() => cde.AddCount());      // cannot revive
        }

        public void SignalMoreThanCount_Throws()
        {
            var cde = new AsyncCountdownEvent(2);
            Check.Throws<InvalidOperationException>(() => cde.Signal(3));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new AsyncCountdownEvent(-1));
            Check.Throws<ArgumentOutOfRangeException>(() => new AsyncCountdownEvent(1).Signal(0));
        }

        public async Task Cancellation_WhileWaiting_Throws()
        {
            var cde = new AsyncCountdownEvent(1);
            using var cts = new CancellationTokenSource();
            var waiter = cde.WaitAsync(cts.Token);
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => waiter);
        }
    }
}
