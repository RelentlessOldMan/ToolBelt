using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    /// <summary>
    /// Regressions from review round 21 (second pass over Threading, Resilience, Process, Runtime): each test failed
    /// before its fix. Anything that used to hang runs under a bounded wait.
    /// </summary>
    public sealed class ConcurrencyReview2Tests
    {
        private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static async Task<bool> FinishesWithin(Task task, int milliseconds)
            => await Task.WhenAny(task, Task.Delay(milliseconds)) == task;

        private sealed class ManualClock
        {
            public DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            public DateTimeOffset Read() => Now;
        }

        // --- Retry ---

        public void Retry_ZeroBaseExponentialStaysZero()
        {
            var policy = new RetryPolicy { BaseDelay = TimeSpan.Zero, Backoff = BackoffStrategy.Exponential, MaxAttempts = 5000 };
            Check.Equal(TimeSpan.Zero, policy.DelayForAttempt(1024));
            Check.Equal(TimeSpan.Zero, policy.DelayForAttempt(1025));
            Check.Equal(TimeSpan.Zero, policy.DelayForAttempt(int.MaxValue));
            policy.MaxDelay = Timeout.InfiniteTimeSpan;
            Check.Equal(TimeSpan.Zero, policy.DelayForAttempt(2000), "a zero base must not become 'forever'");
        }

        // --- CircuitBreaker ---

        public async Task CircuitBreaker_StaleCallAfterRecloseDoesNotReopen()
        {
            var clock = new ManualClock();
            var breaker = new CircuitBreaker(1, TimeSpan.FromSeconds(10), clock.Read);
            var stale = new TaskCompletionSource<int>();
            Task<int> a = breaker.ExecuteAsync(_ => stale.Task);                       // started while closed
            await Check.ThrowsAsync<InvalidOperationException>(() => breaker.ExecuteAsync<int>(_ => throw new InvalidOperationException()));
            Check.Equal(CircuitState.Open, breaker.State);
            clock.Now += TimeSpan.FromSeconds(11);
            Check.Equal(5, await breaker.ExecuteAsync(_ => Task.FromResult(5)));        // the trial closes it
            Check.Equal(CircuitState.Closed, breaker.State);

            stale.SetException(new InvalidOperationException("stale"));
            try { await a; } catch (InvalidOperationException) { }
            Check.Equal(CircuitState.Closed, breaker.State, "a call from before the circuit opened changed the state");
        }

        public void CircuitBreaker_ClockSteppingBackDoesNotKeepItOpen()
        {
            var clock = new ManualClock();
            var breaker = new CircuitBreaker(1, TimeSpan.FromSeconds(10), clock.Read);
            try { breaker.ExecuteAsync<int>(_ => throw new InvalidOperationException()).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { }
            Check.Equal(CircuitState.Open, breaker.State);
            clock.Now -= TimeSpan.FromHours(1);
            Check.Equal(CircuitState.Open, breaker.State);
            clock.Now += TimeSpan.FromSeconds(11);
            Check.Equal(CircuitState.HalfOpen, breaker.State, "waited the reset timeout plus the clock's jump");
        }

        // --- Throttle / TokenBucket ---

        public void Throttle_ClockSteppingBackWaitsOneIntervalAtMost()
        {
            var clock = new ManualClock();
            var throttle = new Throttle(TimeSpan.FromSeconds(1), clock.Read);
            Check.True(throttle.TryAcquire());
            clock.Now -= TimeSpan.FromHours(1);
            Check.False(throttle.TryAcquire());
            Check.True(throttle.TimeUntilNext <= TimeSpan.FromSeconds(1));
            clock.Now += TimeSpan.FromSeconds(1);
            Check.True(throttle.TryAcquire(), "refused for the length of the clock's jump");
        }

        public void Throttle_MaxIntervalWithClockBackDoesNotOverflow()
        {
            var clock = new ManualClock();
            var throttle = new Throttle(TimeSpan.MaxValue, clock.Read);
            Check.True(throttle.TryAcquire());
            clock.Now -= TimeSpan.FromTicks(1);
            Check.Equal(TimeSpan.MaxValue, throttle.TimeUntilNext);
        }

        public void TokenBucket_ClockSteppingBackStillRefills()
        {
            var clock = new ManualClock();
            var bucket = new TokenBucketRateLimiter(1, 1, clock.Read);
            Check.True(bucket.TryAcquire());
            clock.Now -= TimeSpan.FromHours(1);
            Check.False(bucket.TryAcquire());
            clock.Now += TimeSpan.FromMinutes(30);
            Check.Close(1.0, bucket.AvailableTokens, 1e-9, "no refill until the clock passed the old timestamp");
        }

        public void TokenBucket_CountAboveCapacityIsRejected()
        {
            var bucket = new TokenBucketRateLimiter(5, 1);
            Check.True(bucket.TryAcquire(5));
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => bucket.TryAcquire(6));
            Check.Equal("count", ex.ParamName);
        }

        // --- TaskExtensions / TaskRace ---

        public async Task WhenAllOrFirstException_IsLinear()
        {
            const int n = 40000;
            var tasks = Enumerable.Range(0, n).Select(i => Task.FromResult(i)).ToArray();
            var sw = Stopwatch.StartNew();
            int[] results = await ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(tasks);
            sw.Stop();
            Check.Equal(n, results.Length);
            Check.Equal(n - 1, results[n - 1]);
            Check.True(sw.ElapsedMilliseconds < 1500, $"{n} tasks took {sw.ElapsedMilliseconds} ms");
        }

        public async Task WhenAllOrFirstException_StillFailsFast()
        {
            var never = new TaskCompletionSource<int>();
            var fail = Task.FromException<int>(new InvalidOperationException("boom"));
            var ex = await Check.ThrowsAsync<InvalidOperationException>(() => ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(new[] { never.Task, fail }));
            Check.Equal("boom", ex.Message);
            var cancelled = Task.FromCanceled<int>(new CancellationToken(true));
            await Check.ThrowsAsync<TaskCanceledException>(() => ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(new[] { never.Task, cancelled }));
            Check.Equal(0, (await ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(Array.Empty<Task<int>>())).Length);
        }

        public async Task Hedged_AFailureDoesNotRestartTheHedgeTimer()
        {
            var sw = Stopwatch.StartNew();
            var starts = new long[3];
            int result = await TaskRace.Hedged(async (n, ct) =>
            {
                starts[n] = sw.ElapsedMilliseconds;
                if (n == 0) { await Task.Delay(1500, ct); throw new InvalidOperationException(); }
                if (n == 1) { await Task.Delay(Timeout.Infinite, ct); return -1; }
                return 2;
            }, TimeSpan.FromSeconds(1), maxAttempts: 3);
            Check.Equal(2, result);
            Check.True(starts[2] < 2300, $"the third attempt started at {starts[2]} ms, not about 2000 ms");
        }

        public async Task Hedged_InfiniteOrHugeDelayNeverHedges()
        {
            foreach (TimeSpan delay in new[] { Timeout.InfiniteTimeSpan, TimeSpan.FromDays(60) })
            {
                int calls = 0;
                int result = await TaskRace.Hedged(async (n, ct) =>
                {
                    Interlocked.Increment(ref calls);
                    await Task.Delay(50, ct);
                    return 7;
                }, delay, maxAttempts: 2);
                Check.Equal(7, result);
                Check.Equal(1, calls);
            }
        }

        // --- ParallelUtils ---

        private static IEnumerable<int> Counted(long[] memory)
        {
            for (int i = 0; i < 300000; i++)
            {
                if (i == 1000 || i == 299999)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    memory[i == 1000 ? 0 : 1] = GC.GetTotalMemory(true);
                }
                yield return i;
            }
        }

        public async Task ParallelUtils_FinishedBodiesAreNotKept()
        {
            var memory = new long[2];
            await ParallelUtils.ForEachAsync(Counted(memory), 4, (item, ct) => Task.CompletedTask);
            long grown = memory[1] - memory[0];
            Check.True(grown < 10_000_000, $"{grown / 1e6:F1} MB retained across 300000 finished items");
        }

        public async Task ParallelUtils_StillWaitsForEveryBody()
        {
            int done = 0;
            await ParallelUtils.ForEachAsync(Enumerable.Range(0, 50), 8, async (i, ct) =>
            {
                await Task.Delay(i % 5, ct);
                Interlocked.Increment(ref done);
            });
            Check.Equal(50, Volatile.Read(ref done));
            var agg = await Check.ThrowsAsync<AggregateException>(() => ParallelUtils.ForEachAsync(Enumerable.Range(0, 20), 3, async (i, ct) =>
            {
                await Task.Yield();
                if (i == 5) throw new InvalidOperationException("five");
            }));
            Check.True(agg.InnerExceptions.Any(e => e.Message == "five"));
        }

        // --- ConcurrentPipeline / PeriodicWorker ---

        public void Pipeline_DropOldestDoesNotPileUpSignals()
        {
            var gate = new ManualResetEventSlim(false);
            var pipe = new ConcurrentPipeline<int>((i, ct) => { gate.Wait(ct); return Task.CompletedTask; }, 10, PipelineOverflowPolicy.DropOldest);
            try
            {
                for (int i = 0; i < 100000; i++) pipe.TryEnqueue(i);
                var signal = (SemaphoreSlim)typeof(ConcurrentPipeline<int>)
                    .GetField("_signal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pipe)!;
                Check.True(signal.CurrentCount <= 11, $"{signal.CurrentCount} wake-ups pending for at most 10 items");
            }
            finally
            {
                gate.Set();
                pipe.Dispose();
            }
        }

        public async Task Pipeline_DisposeFromOnErrorDoesNotDeadlock()
        {
            ConcurrentPipeline<int>? pipe = null;
            var disposed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pipe = new ConcurrentPipeline<int>((i, ct) => throw new InvalidOperationException(), 4,
                onError: ex => { pipe!.Dispose(); disposed.TrySetResult(true); });
            pipe.TryEnqueue(1);
            Check.True(await FinishesWithin(disposed.Task, 5000), "Dispose from onError never returned");
        }

        public async Task PeriodicWorker_DisposeFromOnErrorDoesNotDeadlock()
        {
            PeriodicWorker? worker = null;
            int calls = 0;
            var disposed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            worker = new PeriodicWorker(ct => { Interlocked.Increment(ref calls); throw new InvalidOperationException(); },
                TimeSpan.FromMilliseconds(10), ex => { worker!.Dispose(); disposed.TrySetResult(true); });
            worker.Start();
            Check.True(await FinishesWithin(disposed.Task, 5000), "Dispose from onError never returned");
            await Task.Delay(200);
            Check.Equal(1, Volatile.Read(ref calls), "the loop kept running after Dispose");
        }

        // --- AsyncLazy / AsyncEvent ---

        public async Task AsyncLazy_SynchronousThrowIsAFaultedTask()
        {
            int runs = 0;
            var lazy = new AsyncLazy<int>(() => { runs++; throw new InvalidOperationException("sync"); });
            Task<int> t = null!;
            Check.True(NoThrow(() => t = lazy.Value), "Value threw instead of returning the faulted task");
            Check.True(t.IsFaulted);
            Check.True(ReferenceEquals(t, lazy.Value));
            var ex = await Check.ThrowsAsync<InvalidOperationException>(async () => await lazy);
            Check.Equal("sync", ex.Message);
            Check.Equal(1, runs);
        }

        private static bool NoThrow(Action a)
        {
            try { a(); return true; }
            catch { return false; }
        }

        public async Task AsyncEvent_CancellationKeepsEarlierFailures()
        {
            var ev = new AsyncEvent<int>();
            using var cts = new CancellationTokenSource();
            ev.Subscribe((x, ct) => { cts.Cancel(); throw new InvalidOperationException("first"); });
            ev.Subscribe((x, ct) => Task.CompletedTask);
            Exception? caught = null;
            try { await ev.InvokeSequentialAsync(1, cts.Token); }
            catch (Exception ex) { caught = ex; }
            Check.True(caught is AggregateException, $"got {caught?.GetType().Name}");
            var agg = (AggregateException)caught!;
            Check.True(agg.InnerExceptions.Any(e => e.Message == "first"), "the first handler's failure was lost");
            Check.True(agg.InnerExceptions.Any(e => e is OperationCanceledException));
        }

        // --- Process ---

        public async Task ProcessRunner_PreCancelledTokenDoesNotStartTheChild()
        {
            if (!Windows) return;
            string dir = Path.Combine(Path.GetTempPath(), "tb-r21-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string marker = Path.Combine(dir, "marker.txt");
                var token = new CancellationToken(true);
                await Check.ThrowsAsync<OperationCanceledException>(() =>
                    ProcessRunner.RunAsync("cmd.exe", $"/c echo hi> \"{marker}\"", null, token));
                await Task.Delay(1000);
                Check.False(File.Exists(marker), "the child ran although the token was already cancelled");
                // Deterministic: a launch failure means the start was attempted.
                await Check.ThrowsAsync<OperationCanceledException>(() =>
                    ProcessRunner.RunAsync("tb-no-such-exe-r21", null, null, token));
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        public void WhichExe_QuotedPathEntryIsSearched()
        {
            if (!Windows) return;
            string dir = Path.Combine(Path.GetTempPath(), "tb r21 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string? oldPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                string tool = Path.Combine(dir, "tbr21tool.cmd");
                File.WriteAllText(tool, "@echo hi");
                Environment.SetEnvironmentVariable("PATH", "\"" + dir + "\"" + Path.PathSeparator + oldPath);
                Check.True(string.Equals(tool, WhichExe.Find("tbr21tool"), StringComparison.OrdinalIgnoreCase), "the quoted PATH entry was not searched");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", oldPath);
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }
    }
}
