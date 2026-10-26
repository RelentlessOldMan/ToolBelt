using System;
using System.IO;
using System.IO.Pipes;
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
    /// Regressions from review round 42 (third pass over Threading, Resilience, Process, Runtime): each test failed
    /// before its fix. Anything that used to hang runs under a bounded wait.
    /// </summary>
    public sealed class ConcurrencyReview3Tests
    {
        // ---------- ProcessRunner ----------

        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);
        [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int which, IntPtr handle);
        private const int STD_INPUT_HANDLE = -10;

        public void ProcessRunner_NullStandardInput_DoesNotInheritTheCallersStdin()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            // The child read the caller's own stdin: here a pipe holding a line, which `sort` printed back.
            using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            byte[] data = Encoding.ASCII.GetBytes("from-the-parent\r\n");
            pipe.Write(data, 0, data.Length);
            pipe.Flush();
            IntPtr saved = GetStdHandle(STD_INPUT_HANDLE);
            SetStdHandle(STD_INPUT_HANDLE, pipe.ClientSafePipeHandle.DangerousGetHandle());
            ProcessResult result;
            try
            {
                result = ProcessRunner.Run("cmd.exe", "/c sort", new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(15) });
            }
            finally { SetStdHandle(STD_INPUT_HANDLE, saved); }
            Check.False(result.TimedOut, "timed out");
            Check.False(result.StandardOutput.Contains("from-the-parent"), "the child read the caller's stdin");
            Check.Equal(0, result.ExitCode);
        }

        // ---------- TokenBucketRateLimiter ----------

        public void TokenBucket_RefillInSmallSteps_GrantsTheDueToken()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var bucket = new TokenBucketRateLimiter(1, 1, () => now);
            Check.True(bucket.TryAcquire());
            for (int i = 0; i < 10; i++)
            {
                now += TimeSpan.FromMilliseconds(100);
                _ = bucket.AvailableTokens;
            }
            Check.True(bucket.TryAcquire(), "a full second passed");
        }

        public void TokenBucket_PolledOften_GrantsTheFullRate()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var bucket = new TokenBucketRateLimiter(1, 10, () => now);
            Check.True(bucket.TryAcquire());
            int granted = 0;
            for (int i = 0; i < 1000; i++)
            {
                now += TimeSpan.FromMilliseconds(10);
                if (bucket.TryAcquire()) granted++;
            }
            Check.Equal(100, granted);
        }

        // ---------- PeriodicWorker ----------

        public void PeriodicWorker_DelayThrowingItsOwnCancellation_IsReported()
        {
            var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var worker = new PeriodicWorker(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(1),
                ex => reported.TrySetResult(ex),
                (_, __) => Task.FromException(new OperationCanceledException("the delay's own timeout")));
            worker.Start();
            Check.True(reported.Task.Wait(10000), "the loop stopped without reporting");
            Check.True(reported.Task.Result is OperationCanceledException);
        }

        // ---------- AsyncEvent ----------

        public void AsyncEvent_Parallel_ReportsTheHandlersExceptionAsSequentialDoes()
        {
            var evt = new AsyncEvent<int>();
            evt.Subscribe((_, __) => throw new AggregateException(new InvalidOperationException()));
            var seq = Check.Throws<AggregateException>(() => evt.InvokeSequentialAsync(1).GetAwaiter().GetResult());
            var par = Check.Throws<AggregateException>(() => evt.InvokeParallelAsync(1).GetAwaiter().GetResult());
            Check.True(seq.InnerExceptions[0] is AggregateException);
            Check.True(par.InnerExceptions[0] is AggregateException, par.InnerExceptions[0].GetType().Name);
        }

        // ---------- Jitter ----------

        public void Jitter_NaNFromTheRandomSource_IsRejected()
        {
            foreach (var mode in new[] { JitterMode.Full, JitterMode.Equal })
            {
                var e = Check.Throws<ArgumentOutOfRangeException>(() => Jitter.Apply(TimeSpan.FromSeconds(1), mode, () => double.NaN));
                Check.Equal("random", e.ParamName);
            }
        }

        // ---------- Retry ----------

        public void Retry_NullDelayOrShouldRetry_IsRejectedUpFront()
        {
            int calls = 0;
            Func<CancellationToken, Task<int>> op = _ => { calls++; throw new TimeoutException(); };
            var d = Check.Throws<ArgumentNullException>(() =>
                Retry.ExecuteAsync(op, new RetryPolicy { DelayAsync = null!, MaxAttempts = 3 }).GetAwaiter().GetResult());
            Check.Equal("DelayAsync", d.ParamName);
            var s = Check.Throws<ArgumentNullException>(() =>
                Retry.ExecuteAsync(op, new RetryPolicy { ShouldRetry = null!, MaxAttempts = 3 }).GetAwaiter().GetResult());
            Check.Equal("ShouldRetry", s.ParamName);
            Check.Equal(0, calls);
        }

        // ---------- CancellationTokenExtensions ----------

        public void CreateLinkedTimeout_TooLong_NamesTimeout()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => CancellationToken.None.CreateLinkedTimeout(TimeSpan.MaxValue));
            Check.Equal("timeout", e.ParamName);
        }
    }
}
