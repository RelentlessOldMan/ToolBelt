using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Round 183 (concurrency, eleventh pass): each regression failed on the code before its fix.
    public sealed class ConcurrencyReview183Tests
    {
        // ---- ConcurrentPipeline: drained verdicts ----

        // The last item failed and onError disposed the pipeline: every item had been processed, yet CompleteAsync said it
        // was disposed before its buffer drained (a last item that returned normally completed fine).
        public async Task Pipeline_LastItemFailsAndOnErrorDisposes_CompletesCleanly()
        {
            var go = new ManualResetEventSlim(false);
            ConcurrentPipeline<int>? p = null;
            int errors = 0;
            p = new ConcurrentPipeline<int>((x, ct) =>
            {
                go.Wait(TimeSpan.FromSeconds(10));
                throw new InvalidOperationException("boom");
            }, 4, onError: ex => { Interlocked.Increment(ref errors); p!.Dispose(); });
            Check.True(p.TryEnqueue(1));
            Task done = p.CompleteAsync();
            go.Set();
            Task first = await Task.WhenAny(done, Task.Delay(TimeSpan.FromSeconds(20)));
            Check.True(ReferenceEquals(first, done), "CompleteAsync hung");
            await done;
            Check.Equal(1, errors);
        }

        // A pipeline that never held an item, disposed and then completed, said it was disposed before its buffer drained.
        public async Task Pipeline_EmptyDisposedThenCompleted_DoesNotClaimALoss()
        {
            var p = new ConcurrentPipeline<int>((x, ct) => Task.CompletedTask, 4);
            p.Dispose();
            await p.CompleteAsync();
        }

        // A Dispose with an item still buffered is still reported.
        public async Task Pipeline_DisposedWithAnItemBuffered_StillThrows()
        {
            var started = new ManualResetEventSlim(false);
            var p = new ConcurrentPipeline<int>(async (x, ct) =>
            {
                started.Set();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }, 4);
            Check.True(p.TryEnqueue(1));
            Check.True(p.TryEnqueue(2));
            Check.True(started.Wait(TimeSpan.FromSeconds(10)));
            p.Dispose();
            await Check.ThrowsAsync<ObjectDisposedException>(() => p.CompleteAsync());
        }

        // ---- ProcessRunner ----

        // The synchronous Run waited on a WhenAll that completes on a pool thread: with every pool thread busy, a child
        // whose grandchild held a pipe for a second was reported TimedOut after the whole timeout.
        public void Run_WithThePoolBusy_DoesNotTimeOut()
        {
            ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
            ThreadPool.GetMinThreads(out int minWorkers, out int minIo);
            int cap = Math.Max(Environment.ProcessorCount, minWorkers);
            var release = new ManualResetEventSlim(false);
            try
            {
                Check.True(ThreadPool.SetMaxThreads(cap, maxIo), "cap the pool");
                for (int i = 0; i < cap * 2; i++) ThreadPool.QueueUserWorkItem(_ => release.Wait());
                var sw = Stopwatch.StartNew();
                ProcessResult r = ProcessRunner.Run(
                    "cmd.exe", "/c echo hi& start /b ping -n 2 127.0.0.1 >nul", new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(10) });
                sw.Stop();
                Check.True(!r.TimedOut && sw.ElapsedMilliseconds < 8000, $"timed out {r.TimedOut} after {sw.ElapsedMilliseconds} ms");
                Check.Equal(0, r.ExitCode);
                Check.Equal("hi", r.StandardOutput.Trim());
            }
            finally
            {
                release.Set();
                ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            }
        }

        // ---- TokenBucketRateLimiter ----

        // A full bucket of 2.9999999995 would grant 3 under the grant's rounding tolerance, but the capacity check refused
        // 3 as "could never be acquired".
        public void TokenBucket_CountWithinTheToleranceOfCapacity_IsGranted()
        {
            var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var limiter = new TokenBucketRateLimiter(2.9999999995, 1, () => t0);
            Check.True(limiter.TryAcquire(3));
            var big = new TokenBucketRateLimiter(1000 - 5e-7, 1, () => t0);
            Check.True(big.TryAcquire(1000));
            var over = new TokenBucketRateLimiter(2.99, 1, () => t0);
            Check.Throws<ArgumentOutOfRangeException>(() => over.TryAcquire(3));
        }
    }
}
