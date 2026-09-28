using System;
using System.Collections.Concurrent;
using System.Linq;
using ToolBelt.Logging;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class AsyncLogSinkTests
    {
        private static LogEvent Ev(int i)
            => new LogEvent(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", "m" + i, null);

        public void DeliversAllEventsThenDrains()
        {
            var received = new ConcurrentQueue<LogEvent>();
            var inner = new DelegateSink(received.Enqueue);
            using (var async = new AsyncLogSink(inner, capacity: 256, overflowPolicy: PipelineOverflowPolicy.Block))
            {
                for (int i = 0; i < 1000; i++) async.Emit(Ev(i));
            } // Dispose drains the buffer
            Check.Equal(1000, received.Count);
        }

        public void DisposeIsOrdered()
        {
            var received = new ConcurrentQueue<LogEvent>();
            var async = new AsyncLogSink(new DelegateSink(received.Enqueue), overflowPolicy: PipelineOverflowPolicy.Block);
            for (int i = 0; i < 200; i++) async.Emit(Ev(i));
            async.Dispose();
            // Single consumer preserves order.
            Check.True(received.Select(e => e.Message).SequenceEqual(Enumerable.Range(0, 200).Select(i => "m" + i)));
        }

        public void DropPolicyCountsDrops()
        {
            // A gated inner sink stalls, so a tiny drop-newest buffer overflows deterministically.
            var gate = new System.Threading.ManualResetEventSlim(false);
            var inner = new DelegateSink(_ => gate.Wait());
            var async = new AsyncLogSink(inner, capacity: 2, overflowPolicy: PipelineOverflowPolicy.DropNewest);
            for (int i = 0; i < 100; i++) async.Emit(Ev(i));
            System.Threading.Thread.Sleep(50);
            gate.Set();
            async.Dispose();
            Check.True(async.DroppedCount > 0, "expected some drops under a stalled sink");
        }

        public void NullInner_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new AsyncLogSink(null!));
        }
    }
}
