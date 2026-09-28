using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class AsyncEventTests
    {
        public async Task SequentialInvokesInOrder()
        {
            var ev = new AsyncEvent<int>();
            var log = new System.Collections.Generic.List<string>();
            ev.Subscribe(async (n, _) => { await Task.Yield(); log.Add("a" + n); });
            ev.Subscribe(async (n, _) => { await Task.Yield(); log.Add("b" + n); });

            await ev.InvokeSequentialAsync(5);
            Check.Equal(2, log.Count);
            Check.Equal("a5", log[0]);
            Check.Equal("b5", log[1]);
        }

        public async Task ParallelInvokesAll()
        {
            var ev = new AsyncEvent<int>();
            int count = 0;
            ev.Subscribe(async (_, __) => { await Task.Yield(); Interlocked.Increment(ref count); });
            ev.Subscribe(async (_, __) => { await Task.Yield(); Interlocked.Increment(ref count); });
            ev.Subscribe(async (_, __) => { await Task.Yield(); Interlocked.Increment(ref count); });

            await ev.InvokeParallelAsync(0);
            Check.Equal(3, count);
        }

        public async Task AggregatesExceptions()
        {
            var ev = new AsyncEvent<int>();
            ev.Subscribe((_, __) => throw new InvalidOperationException("one"));
            ev.Subscribe((_, __) => Task.CompletedTask);
            ev.Subscribe((_, __) => throw new ArgumentException("two"));

            var seq = await Check.ThrowsAsync<AggregateException>(() => ev.InvokeSequentialAsync(0));
            Check.Equal(2, seq.InnerExceptions.Count);

            var par = await Check.ThrowsAsync<AggregateException>(() => ev.InvokeParallelAsync(0));
            Check.Equal(2, par.InnerExceptions.Count);
        }

        public void SubscribeUnsubscribe()
        {
            var ev = new AsyncEvent<int>();
            Func<int, CancellationToken, Task> handler = (_, __) => Task.CompletedTask;
            ev.Subscribe(handler);
            Check.Equal(1, ev.HandlerCount);
            Check.True(ev.Unsubscribe(handler));
            Check.Equal(0, ev.HandlerCount);
            Check.False(ev.Unsubscribe(handler));
        }

        public void NullHandler_Throws()
        {
            var ev = new AsyncEvent<int>();
            Check.Throws<ArgumentNullException>(() => ev.Subscribe(null!));
        }
    }
}
