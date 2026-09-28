using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class ConcurrentPipelineTests
    {
        public async Task BlockPolicyProcessesEverything()
        {
            var consumed = new ConcurrentQueue<int>();
            var pipe = new ConcurrentPipeline<int>(
                (item, _) => { consumed.Enqueue(item); return Task.CompletedTask; },
                capacity: 4, policy: PipelineOverflowPolicy.Block);

            for (int i = 0; i < 20; i++) pipe.TryEnqueue(i);
            await pipe.CompleteAsync();

            Check.Equal(20, consumed.Count);
            Check.Equal(0L, pipe.DroppedCount);
            Check.True(consumed.SequenceEqual(Enumerable.Range(0, 20))); // single consumer preserves order
            pipe.Dispose();
        }

        public async Task DropNewestDropsWhenFull()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new SemaphoreSlim(0);
            var consumed = new ConcurrentQueue<int>();

            var pipe = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                if (item == 0) { started.TrySetResult(true); await release.WaitAsync(ct); }
                consumed.Enqueue(item);
            }, capacity: 2, policy: PipelineOverflowPolicy.DropNewest);

            pipe.TryEnqueue(0);
            await started.Task;                 // consumer took item 0 and is now gated; queue is empty

            Check.True(pipe.TryEnqueue(1));      // buffered (1/2)
            Check.True(pipe.TryEnqueue(2));      // buffered (2/2, full)
            Check.False(pipe.TryEnqueue(3));     // dropped
            Check.False(pipe.TryEnqueue(4));     // dropped
            Check.Equal(2L, pipe.DroppedCount);

            release.Release();
            await pipe.CompleteAsync();

            Check.True(consumed.SequenceEqual(new[] { 0, 1, 2 }));
            pipe.Dispose();
        }

        public async Task DropOldestEvictsOldest()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new SemaphoreSlim(0);
            var consumed = new ConcurrentQueue<int>();

            var pipe = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                if (item == 0) { started.TrySetResult(true); await release.WaitAsync(ct); }
                consumed.Enqueue(item);
            }, capacity: 2, policy: PipelineOverflowPolicy.DropOldest);

            pipe.TryEnqueue(0);
            await started.Task;

            pipe.TryEnqueue(1);   // buffer [1]
            pipe.TryEnqueue(2);   // buffer [1,2] full
            pipe.TryEnqueue(3);   // drops 1 -> buffer [2,3]
            Check.Equal(1L, pipe.DroppedCount);

            release.Release();
            await pipe.CompleteAsync();

            Check.True(consumed.SequenceEqual(new[] { 0, 2, 3 })); // 1 was evicted
            pipe.Dispose();
        }

        public void EnqueueAfterComplete_Throws()
        {
            var pipe = new ConcurrentPipeline<int>((_, __) => Task.CompletedTask, capacity: 2);
            pipe.CompleteAsync().Wait();
            Check.Throws<InvalidOperationException>(() => pipe.TryEnqueue(1));
            pipe.Dispose();
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new ConcurrentPipeline<int>(null!, 2));
            Check.Throws<ArgumentOutOfRangeException>(() => new ConcurrentPipeline<int>((_, __) => Task.CompletedTask, 0));
        }
    }
}
