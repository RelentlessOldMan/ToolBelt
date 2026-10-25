using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    /// <summary>
    /// Regressions from review round 10 (Threading, Resilience, Process, Runtime): each test failed before its fix.
    /// Anything that used to hang runs under a bounded wait, so a regression fails instead of stalling the run.
    /// </summary>
    public sealed class ConcurrencyReviewTests
    {
        private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Runs the action on a background thread; false if it hasn't finished within the budget.
        private static bool Within(int milliseconds, Action action)
        {
            Exception? error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            thread.Start();
            if (!thread.Join(milliseconds)) return false;
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return true;
        }

        private static async Task<bool> FinishesWithin(Task task, int milliseconds)
            => await Task.WhenAny(task, Task.Delay(milliseconds)) == task;

        // ---------------- AtomicDouble ----------------

        public void AtomicDouble_AddOnNaNReturnsNaN()
        {
            var d = new AtomicDouble(double.NaN);
            double result = 0;
            Check.True(Within(2000, () => result = d.Add(1)), "Add spun forever on a NaN value");
            Check.True(double.IsNaN(result));
        }

        // ---------------- PeriodicWorker ----------------

        public void PeriodicWorker_ZeroIntervalWithSynchronousWorkStartReturns()
        {
            var worker = new PeriodicWorker(_ => Task.CompletedTask, TimeSpan.Zero);
            Check.True(Within(2000, worker.Start), "Start ran the loop on the caller's thread");
            worker.Dispose();
        }

        // A UI thread that is blocked never runs what is posted to it.
        private sealed class BlockedUiContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) { }
        }

        public void PeriodicWorker_DisposeOnASingleThreadedContextDoesNotDeadlock()
        {
            Check.True(Within(3000, () =>
            {
                SynchronizationContext.SetSynchronizationContext(new BlockedUiContext());
                var worker = new PeriodicWorker(async _ => await Task.Delay(50), TimeSpan.FromSeconds(1));
                worker.Start();
                worker.Dispose();
            }), "Dispose deadlocked waiting for work whose continuation was posted to the blocked thread");
        }

        public void PeriodicWorker_ThrowingOnErrorKeepsTheLoopRunning()
        {
            int runs = 0;
            var worker = new PeriodicWorker(
                _ => { Interlocked.Increment(ref runs); throw new InvalidOperationException("work"); },
                TimeSpan.FromMilliseconds(1),
                onError: _ => throw new InvalidOperationException("sink"));
            worker.Start();
            Thread.Sleep(300);
            worker.Dispose();
            Check.True(Volatile.Read(ref runs) >= 3, "runs = " + runs);
        }

        public void PeriodicWorker_SecondDisposeIsANoOp()
        {
            var worker = new PeriodicWorker(_ => Task.CompletedTask, TimeSpan.FromSeconds(1));
            worker.Start();
            worker.Dispose();
            worker.Dispose();
        }

        public void PeriodicWorker_IntervalTooLongForTaskDelayIsRejected()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new PeriodicWorker(_ => Task.CompletedTask, TimeSpan.FromDays(60)));
        }

        public void PeriodicWorker_DelayFailureIsReported()
        {
            var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new PeriodicWorker(
                _ => Task.CompletedTask,
                TimeSpan.FromSeconds(1),
                onError: ex => reported.TrySetResult(ex),
                delay: (_, __) => throw new InvalidOperationException("delay"));
            worker.Start();
            Check.True(reported.Task.Wait(2000), "a failing delay killed the loop silently");
            Check.Equal("delay", reported.Task.Result.Message);
            worker.Dispose();
        }

        // ---------------- ConcurrentPipeline ----------------

        public async Task Pipeline_ThrowingOnErrorDoesNotStallProducers()
        {
            int errors = 0;
            var pipe = new ConcurrentPipeline<int>(
                (_, __) => throw new InvalidOperationException("consumer"),
                capacity: 1,
                policy: PipelineOverflowPolicy.Block,
                onError: _ => { Interlocked.Increment(ref errors); throw new InvalidOperationException("sink"); });

            Check.True(Within(2000, () => { for (int i = 0; i < 5; i++) pipe.TryEnqueue(i); }), "producer blocked forever");
            Check.True(await FinishesWithin(pipe.CompleteAsync(), 2000));
            Check.Equal(5, Volatile.Read(ref errors));
            pipe.Dispose();
        }

        public void Pipeline_SecondDisposeIsANoOp()
        {
            var pipe = new ConcurrentPipeline<int>((_, __) => Task.CompletedTask, 2);
            pipe.Dispose();
            pipe.Dispose();
        }

        public async Task Pipeline_BlockCapacityCountsBufferedItemsLikeTheDropPolicies()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new SemaphoreSlim(0);
            var consumed = new ConcurrentQueue<int>();
            var pipe = new ConcurrentPipeline<int>(async (item, ct) =>
            {
                if (item == 0) { started.TrySetResult(true); await release.WaitAsync(ct); }
                consumed.Enqueue(item);
            }, capacity: 1, policy: PipelineOverflowPolicy.Block);

            pipe.TryEnqueue(0);
            await started.Task;
            Check.True(Within(1000, () => pipe.TryEnqueue(1)), "capacity 1 buffered nothing while an item was in flight");

            release.Release();
            await pipe.CompleteAsync();
            Check.True(consumed.SequenceEqual(new[] { 0, 1 }));
            pipe.Dispose();
        }

        public void Pipeline_NoAcceptedItemIsLostToCompletion()
        {
            for (int trial = 0; trial < 300; trial++)
            {
                var consumed = new ConcurrentQueue<int>();
                var accepted = new ConcurrentQueue<int>();
                var pipe = new ConcurrentPipeline<int>(
                    (item, _) => { consumed.Enqueue(item); return Task.CompletedTask; },
                    capacity: 1, policy: PipelineOverflowPolicy.Block);

                var producer = new Thread(() =>
                {
                    try { for (int i = 0; ; i++) if (pipe.TryEnqueue(i)) accepted.Enqueue(i); }
                    catch (InvalidOperationException) { } // completed
                    catch (OperationCanceledException) { }
                }) { IsBackground = true };
                producer.Start();
                Thread.SpinWait(2000 + trial * 50);
                Check.True(pipe.CompleteAsync().Wait(2000));
                Check.True(producer.Join(2000), "producer stuck after completion");
                Check.Equal(accepted.Count, consumed.Count, "trial " + trial + ": an accepted item was never consumed");
                pipe.Dispose();
            }
        }

        // ---------------- ParallelUtils ----------------

        public async Task ParallelUtils_ExternalCancelWaitsForRunningBodies()
        {
            int active = 0;
            using var cts = new CancellationTokenSource();
            var task = ParallelUtils.ForEachAsync(Enumerable.Range(0, 10), 2, async (_, __) =>
            {
                Interlocked.Increment(ref active);
                try { await Task.Delay(300); }
                finally { Interlocked.Decrement(ref active); }
            }, cts.Token);
            await Task.Delay(50);
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => task);
            Check.Equal(0, Volatile.Read(ref active), "bodies still running after ForEachAsync returned");
        }

        public async Task ParallelUtils_SourceFailureWaitsForBodiesAndRunsYieldedItems()
        {
            int active = 0, started = 0;
            static IEnumerable<int> Source()
            {
                yield return 1;
                yield return 2;
                throw new InvalidOperationException("source");
            }
            var ex = await Check.ThrowsAsync<InvalidOperationException>(() => ParallelUtils.ForEachAsync(Source(), 4, async (_, __) =>
            {
                Interlocked.Increment(ref started);
                Interlocked.Increment(ref active);
                try { await Task.Delay(200); }
                finally { Interlocked.Decrement(ref active); }
            }));
            Check.Equal("source", ex.Message);
            Check.Equal(0, Volatile.Read(ref active));
            Check.Equal(2, Volatile.Read(ref started));
        }

        public async Task ParallelUtils_ExternalCancelDuringBodiesIsOperationCanceled()
        {
            using var cts = new CancellationTokenSource(100);
            await Check.ThrowsAsync<OperationCanceledException>(
                () => ParallelUtils.ForEachAsync(new[] { 1, 2, 3 }, 3, (_, ct) => Task.Delay(5000, ct), cts.Token));
        }

        // ---------------- Retry ----------------

        public async Task Retry_InfiniteMaxDelayMeansUncapped()
        {
            var policy = new RetryPolicy { MaxAttempts = 3, MaxDelay = Timeout.InfiniteTimeSpan };
            Check.Equal(TimeSpan.FromMilliseconds(100), policy.DelayForAttempt(1));
            Check.Equal(TimeSpan.FromMilliseconds(200), policy.DelayForAttempt(2));

            var delays = new List<TimeSpan>();
            policy.DelayAsync = (d, _) => { delays.Add(d); return Task.CompletedTask; };
            int calls = 0;
            await Check.ThrowsAsync<InvalidOperationException>(
                () => Retry.ExecuteAsync<int>(_ => { calls++; throw new InvalidOperationException(); }, policy));
            Check.Equal(3, calls);
            Check.True(delays.All(d => d > TimeSpan.Zero));
        }

        public async Task Retry_NegativeBaseDelayIsRejectedBeforeTheFirstAttempt()
        {
            int calls = 0;
            var policy = new RetryPolicy { BaseDelay = TimeSpan.FromMilliseconds(-5) };
            await Check.ThrowsAsync<ArgumentOutOfRangeException>(
                () => Retry.ExecuteAsync<int>(_ => { calls++; throw new InvalidOperationException(); }, policy));
            Check.Equal(0, calls);
            Check.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy { MaxDelay = TimeSpan.FromMilliseconds(-5) }.DelayForAttempt(1));
        }

        public async Task Retry_DefaultDelayAcceptsDelaysBeyondTheTaskDelayLimit()
        {
            var policy = new RetryPolicy();
            using var cts = new CancellationTokenSource(100);
            await Check.ThrowsAsync<OperationCanceledException>(() => policy.DelayAsync(TimeSpan.FromDays(77), cts.Token));
        }

        // ---------------- ProcessRunner ----------------

        public void ProcessRunner_UnreadStandardInputDoesNotDefeatTheTimeout()
        {
            if (!Windows) return;
            var sw = Stopwatch.StartNew();
            var r = ProcessRunner.Run("cmd.exe", "/c ping -n 8 127.0.0.1 >nul", new ProcessRunOptions
            {
                StandardInput = new string('x', 2_000_000),
                Timeout = TimeSpan.FromSeconds(1),
            });
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(5), "took " + sw.Elapsed);
            Check.True(r.TimedOut);
        }

        public void ProcessRunner_GrandchildHoldingThePipesDoesNotDefeatTheTimeout()
        {
            if (!Windows) return;
            var sw = Stopwatch.StartNew();
            var r = ProcessRunner.Run("cmd.exe", "/c \"start /b ping -n 8 127.0.0.1 >nul & echo hi\"",
                new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(2) });
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(5), "took " + sw.Elapsed);
            Check.True(r.StandardOutput.Contains("hi"), r.StandardOutput);
        }

        public async Task ProcessRunner_GrandchildHoldingThePipesDoesNotDefeatTheTimeoutAsync()
        {
            if (!Windows) return;
            var sw = Stopwatch.StartNew();
            var r = await ProcessRunner.RunAsync("cmd.exe", "/c \"start /b ping -n 8 127.0.0.1 >nul & echo hi\"",
                new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(2) });
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(5), "took " + sw.Elapsed);
            Check.True(r.StandardOutput.Contains("hi"), r.StandardOutput);
        }

        public void ProcessRunner_ThrowingLineCallbackSurfacesToTheCaller()
        {
            if (!Windows) return;
            var ex = Check.Throws<InvalidOperationException>(() => ProcessRunner.Run("cmd.exe", "/c echo hi",
                new ProcessRunOptions { OnOutputLine = _ => throw new InvalidOperationException("sink") }));
            Check.Equal("sink", ex.Message);
        }

        public void ProcessRunner_StandardInputUsesTheConfiguredEncoding()
        {
            if (!Windows) return;
            var r = ProcessRunner.Run("findstr.exe", "^", new ProcessRunOptions
            {
                StandardInput = "é€\r\n",
                Encoding = new UTF8Encoding(false),
                Timeout = TimeSpan.FromSeconds(10),
            });
            Check.True(r.StandardOutput.Contains("é€"), r.StandardOutput);
        }

        public async Task ProcessRunner_InvalidTimeoutIsRejectedBeforeStarting()
        {
            if (!Windows) return;
            string marker = Path.Combine(Path.GetTempPath(), "tb-r10-" + Guid.NewGuid().ToString("N"));
            string args = "/c echo x> \"" + marker + "\"";
            Check.Throws<ArgumentOutOfRangeException>(
                () => ProcessRunner.Run("cmd.exe", args, new ProcessRunOptions { Timeout = TimeSpan.FromDays(30) }));
            await Check.ThrowsAsync<ArgumentOutOfRangeException>(
                () => ProcessRunner.RunAsync("cmd.exe", args, new ProcessRunOptions { Timeout = TimeSpan.FromMilliseconds(-5) }));
            Thread.Sleep(1000);
            bool started = File.Exists(marker);
            if (started) File.Delete(marker);
            Check.False(started, "the child ran although the options were invalid");
        }

        // ---------------- WhichExe ----------------

        public void WhichExe_PrefersPathextOverAnExtensionlessFile()
        {
            if (!Windows) return;
            string dir = Path.Combine(Path.GetTempPath(), "tb-r10-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string? oldPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                File.WriteAllText(Path.Combine(dir, "tbtool"), "#!/bin/sh\n");
                File.WriteAllText(Path.Combine(dir, "tbtool.cmd"), "@echo x\r\n");
                Environment.SetEnvironmentVariable("PATH", dir);
                string? found = WhichExe.Find("tbtool");
                Check.True(string.Equals(Path.Combine(dir, "tbtool.cmd"), found, StringComparison.OrdinalIgnoreCase), found);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", oldPath);
                Directory.Delete(dir, true);
            }
        }

        // ---------------- CircuitBreaker ----------------

        public async Task CircuitBreaker_HalfOpenAdmitsASingleTrial()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var breaker = new CircuitBreaker(1, TimeSpan.FromSeconds(10), () => now);
            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync<int>(_ => throw new InvalidOperationException()));
            now += TimeSpan.FromSeconds(11);

            var gate = new TaskCompletionSource<int>();
            int entered = 0;
            var calls = Enumerable.Range(0, 3)
                .Select(_ => breaker.ExecuteAsync(ct => { entered++; return gate.Task; }))
                .ToList();
            Check.Equal(1, entered);
            gate.SetResult(1);
            Check.Equal(1, await calls[0]);
            await Check.ThrowsAsync<CircuitOpenException>(() => calls[1]);
            await Check.ThrowsAsync<CircuitOpenException>(() => calls[2]);
            Check.Equal(CircuitState.Closed, breaker.State);
        }

        public async Task CircuitBreaker_CallerCancellationIsNotAFailure()
        {
            var breaker = new CircuitBreaker(2, TimeSpan.FromSeconds(10));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            for (int i = 0; i < 3; i++)
                await Check.ThrowsAsync<OperationCanceledException>(
                    () => breaker.ExecuteAsync<int>(ct => Task.FromCanceled<int>(ct), cts.Token));
            Check.Equal(CircuitState.Closed, breaker.State);
            Check.Equal(0, breaker.ConsecutiveFailures);
        }

        // ---------------- AsyncEvent, AsyncCountdownEvent, TaskExtensions ----------------

        public async Task AsyncEvent_ParallelReportsACancelledHandlerLikeSequential()
        {
            var ev = new AsyncEvent<int>();
            ev.Subscribe((_, __) => Task.FromCanceled(new CancellationToken(true)));
            await Check.ThrowsAsync<AggregateException>(() => ev.InvokeSequentialAsync(0));
            await Check.ThrowsAsync<AggregateException>(() => ev.InvokeParallelAsync(0));
        }

        public void AsyncCountdownEvent_AddCountOverflowThrows()
        {
            var ev = new AsyncCountdownEvent(1);
            Check.Throws<InvalidOperationException>(() => ev.AddCount(int.MaxValue));
            ev.Signal();
            Check.True(ev.IsSet);
        }

        public async Task WhenAllOrFirstException_CancelledMemberThrowsTaskCanceled()
        {
            await Check.ThrowsAsync<TaskCanceledException>(() => ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(
                new[] { Task.FromResult(1), Task.FromCanceled<int>(new CancellationToken(true)) }));
        }

        public async Task WithTimeout_InvalidTimeoutNamesTheParameter()
        {
            var ex = await Check.ThrowsAsync<ArgumentOutOfRangeException>(
                () => Task.FromResult(1).WithTimeout(TimeSpan.FromMilliseconds(-5)));
            Check.Equal("timeout", ex.ParamName);
            ex = await Check.ThrowsAsync<ArgumentOutOfRangeException>(
                () => Task.CompletedTask.WithTimeout(TimeSpan.MaxValue));
            Check.Equal("timeout", ex.ParamName);
        }

        // ---------------- AsyncLock, Bulkhead ----------------

        public async Task AsyncLock_DisposeFaultsWaitersAndReleaseStillWorks()
        {
            var gate = new AsyncLock();
            var held = await gate.LockAsync();
            var waiter = gate.LockAsync();
            gate.Dispose();
            Check.True(await FinishesWithin(waiter, 2000), "a waiter on a disposed lock never completed");
            await Check.ThrowsAsync<ObjectDisposedException>(() => waiter);
            held.Dispose();
        }

        public async Task Bulkhead_DisposeKeepsTheInFlightResultAndFaultsQueuedCalls()
        {
            var bulkhead = new Bulkhead(1);
            var tcs = new TaskCompletionSource<int>();
            var inFlight = bulkhead.ExecuteAsync(_ => tcs.Task);
            var queued = bulkhead.ExecuteAsync(_ => Task.FromResult(7));
            bulkhead.Dispose();
            tcs.SetResult(42);
            Check.Equal(42, await inFlight);
            Check.True(await FinishesWithin(queued, 2000), "a queued call on a disposed bulkhead never completed");
            await Check.ThrowsAsync<ObjectDisposedException>(() => queued);
        }

        // ---------------- TokenBucketRateLimiter ----------------

        public void TokenBucket_RejectsNaNAndInfinity()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(double.NaN, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(1, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(double.PositiveInfinity, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new TokenBucketRateLimiter(1, double.PositiveInfinity));
        }
    }
}
