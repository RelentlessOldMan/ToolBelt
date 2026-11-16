using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Round 97 (concurrency, sixth pass): each regression failed on the code before its fix (the hangs by a bounded wait,
    // so the old code fails rather than stalling the run).
    public sealed class ConcurrencyReview97Tests
    {
        // ---- ConcurrentPipeline: CompleteAsync from the consumer ----

        public void Pipeline_ASentinelConsumerCanCompleteThePipeline()
        {
            foreach (bool yieldFirst in new[] { false, true })
            {
                var seen = new ConcurrentQueue<int>();
                ConcurrentPipeline<int>? p = null;
                p = new ConcurrentPipeline<int>(async (item, ct) =>
                {
                    if (yieldFirst) await Task.Yield();
                    if (item < 0) await p!.CompleteAsync();
                    else seen.Enqueue(item);
                }, 8);
                p.TryEnqueue(1);
                p.TryEnqueue(2);
                p.TryEnqueue(-1);
                Task outside = p.CompleteAsync();
                Check.True(outside.Wait(TimeSpan.FromSeconds(10)), $"yield {yieldFirst}: CompleteAsync hung behind the consumer's own call");
                Check.Equal("1,2", string.Join(",", seen));
                Check.Throws<InvalidOperationException>(() => p.TryEnqueue(3));
                Check.True(Task.Run(() => p.Dispose()).Wait(TimeSpan.FromSeconds(10)), "Dispose hung");
            }
        }

        public void Pipeline_ItemsAfterTheSentinelStillDrain()
        {
            var seen = new ConcurrentQueue<int>();
            var gate = new ManualResetEventSlim();
            ConcurrentPipeline<int>? p = null;
            p = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                if (item == 0) gate.Wait();
                if (item < 0) await p!.CompleteAsync();
                else seen.Enqueue(item);
            }, 8);
            p.TryEnqueue(0);
            p.TryEnqueue(-1);
            p.TryEnqueue(5);
            gate.Set();
            Check.True(p.CompleteAsync().Wait(TimeSpan.FromSeconds(10)), "CompleteAsync hung behind the consumer's own call");
            Check.Equal("0,5", string.Join(",", seen));
            p.Dispose();
        }

        // Guard: Dispose from the consumer, now that it cannot deadlock, stops the pipeline.
        public void Pipeline_DisposeFromTheConsumerDoesNotHang()
        {
            var done = new ManualResetEventSlim();
            ConcurrentPipeline<int>? p = null;
            p = new ConcurrentPipeline<int>((item, ct) => { p!.Dispose(); done.Set(); return Task.CompletedTask; }, 4);
            p.TryEnqueue(1);
            Check.True(done.Wait(TimeSpan.FromSeconds(10)), "Dispose from the consumer hung");
            Check.Throws<ObjectDisposedException>(() => p.TryEnqueue(2));
        }

        // ---- ConcurrentPipeline: TryEnqueue after Dispose under the drop policies ----

        public void Pipeline_DropPoliciesThrowAfterDispose()
        {
            foreach (PipelineOverflowPolicy policy in new[] { PipelineOverflowPolicy.DropNewest, PipelineOverflowPolicy.DropOldest })
                foreach (bool full in new[] { false, true })
                {
                    var hold = new ManualResetEventSlim();
                    var p = new ConcurrentPipeline<int>((i, ct) => { hold.Wait(ct); return Task.CompletedTask; }, 1, policy);
                    if (full) { p.TryEnqueue(1); p.TryEnqueue(2); }
                    p.Dispose();
                    long dropped = p.DroppedCount;
                    Check.Throws<ObjectDisposedException>(() => p.TryEnqueue(3));
                    Check.Equal(dropped, p.DroppedCount);
                }
        }

        public void Pipeline_DropPoliciesThrowAfterDisposeFromOnError()
        {
            foreach (PipelineOverflowPolicy policy in new[] { PipelineOverflowPolicy.DropNewest, PipelineOverflowPolicy.DropOldest })
            {
                var result = new TaskCompletionSource<string>();
                ConcurrentPipeline<int>? p = null;
                p = new ConcurrentPipeline<int>((i, ct) => throw new InvalidOperationException("boom"), 4, policy, ex =>
                {
                    p!.Dispose();
                    try { result.TrySetResult("returned " + p.TryEnqueue(9)); }
                    catch (Exception e) { result.TrySetResult(e.GetType().Name); }
                });
                p.TryEnqueue(1);
                Check.True(result.Task.Wait(TimeSpan.FromSeconds(10)));
                Check.Equal(nameof(ObjectDisposedException), result.Task.Result);
            }
        }

        // ---- TaskExtensions: one observer per task given up on ----

        public void WithTimeout_PollingATaskThatNeverEndsDoesNotPileUpContinuations()
        {
            var tcs = new TaskCompletionSource<int>();
            Poll(tcs.Task, 1000);
            long before = GC.GetTotalMemory(true);
            Poll(tcs.Task, 200_000);
            long grown = GC.GetTotalMemory(true) - before;
            Check.True(grown < 4L << 20, $"200,000 polls kept {grown:N0} bytes");
            GC.KeepAlive(tcs);
        }

        private static void Poll(Task<int> task, int times)
        {
            for (int i = 0; i < times; i++)
            {
                try { task.WithTimeout(TimeSpan.Zero).GetAwaiter().GetResult(); }
                catch (TimeoutException) { }
                try { task.WithCancellation(new CancellationToken(true)).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
            }
        }

        // Guard: a task given up on many times and then faulted is still observed.
        public void WithTimeout_ATaskGivenUpOnAndFaultedLaterIsObserved()
        {
            int unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (s, e) => { if (e.Exception?.InnerException?.Message == "r97 late") Interlocked.Increment(ref unobserved); };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                Fault();
                for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Check.Equal(0, Volatile.Read(ref unobserved));
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Fault()
        {
            var tcs = new TaskCompletionSource<int>();
            Poll(tcs.Task, 3);
            tcs.SetException(new InvalidOperationException("r97 late"));
        }
    }
}
