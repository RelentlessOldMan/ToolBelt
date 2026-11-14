using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// Regressions from review round 72 (fourth pass over Threading, Resilience, Process, Runtime): each test failed
    /// before its fix. Anything that used to hang runs under a bounded wait.
    /// </summary>
    public sealed class ConcurrencyReview4Tests
    {
        // The task's exception, or null if it is still running after the wait (a hang) or succeeded.
        private static Exception? Outcome(Task task, int ms = 5000)
        {
            try { task.Wait(ms); } catch (AggregateException) { }
            if (!task.IsCompleted) return null;
            return task.Exception?.InnerException ?? (task.IsCanceled ? new TaskCanceledException(task) : null);
        }

        // ---------- ProcessRunner ----------

        public void ProcessRunner_DecodingFailure_StillDrainsSoTheChildFinishes()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            // The reader stopped at the bad byte and left the pipe full: the child blocked until the timeout killed it.
            string file = Path.Combine(Path.GetTempPath(), "tb-r72-" + Guid.NewGuid().ToString("N") + ".txt");
            var bytes = new List<byte> { 0xFF, (byte)'\r', (byte)'\n' };
            byte[] line = Encoding.ASCII.GetBytes(new string('x', 98) + "\r\n");
            for (int i = 0; i < 3000; i++) bytes.AddRange(line);
            File.WriteAllBytes(file, bytes.ToArray());
            try
            {
                var sw = Stopwatch.StartNew();
                Exception? thrown = null;
                try
                {
                    ProcessRunner.Run("cmd.exe", "/c type \"" + file + "\"",
                        new ProcessRunOptions { Encoding = new UTF8Encoding(false, true), Timeout = TimeSpan.FromSeconds(20) });
                }
                catch (Exception ex) { thrown = ex; }
                sw.Stop();
                Check.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}: the child was left blocked on a full pipe");
                Check.True(thrown is DecoderFallbackException, $"expected the decoding failure, got {thrown?.GetType().Name ?? "nothing"}");
            }
            finally { File.Delete(file); }
        }

        public void ProcessRunner_LineAfterTheRunReturned_IsNotBuffered()
        {
            // A grandchild still writing to an inherited pipe after a timeout grew the buffer with nobody to read it.
            Type runner = typeof(ProcessRunner);
            Type stateType = runner.GetNestedType("RunState", BindingFlags.NonPublic)!;
            object state = Activator.CreateInstance(stateType, nonPublic: true)!;
            stateType.GetField("Done")!.SetValue(state, 1);
            var output = (StringBuilder)stateType.GetField("Output")!.GetValue(state)!;
            MethodInfo onLine = runner.GetMethod("OnLine", BindingFlags.NonPublic | BindingFlags.Static)!;
            for (int i = 0; i < 100; i++) onLine.Invoke(null, new object?[] { state, "late output", output, null });
            Check.Equal(0, output.Length, "lines read after the run returned were kept");
        }

        public void ProcessRunner_NoTimeout_GrandchildHoldingThePipes_DoesNotKeepTheRunWaiting()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            // The grandchild inherits the pipes and is quiet for eight seconds: the run waited for it.
            var sw = Stopwatch.StartNew();
            var r = ProcessRunner.Run("cmd.exe", "/c start \"\" /b cmd /c \"ping -n 9 127.0.0.1 >nul\" & echo hi");
            sw.Stop();
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(6), $"took {sw.Elapsed}: waited for the grandchild");
            Check.False(r.TimedOut, "no timeout was set");
            Check.Equal(0, r.ExitCode);
            Check.True(r.StandardOutput.Contains("hi"), "output: " + r.StandardOutput);
        }

        public void ProcessRunner_NoTimeoutAsync_GrandchildHoldingThePipes_DoesNotKeepTheRunWaiting()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            var sw = Stopwatch.StartNew();
            var r = ProcessRunner.RunAsync("cmd.exe", "/c start \"\" /b cmd /c \"ping -n 9 127.0.0.1 >nul\" & echo hi").GetAwaiter().GetResult();
            sw.Stop();
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(6), $"took {sw.Elapsed}: waited for the grandchild");
            Check.False(r.TimedOut, "no timeout was set");
            Check.Equal(0, r.ExitCode);
            Check.True(r.StandardOutput.Contains("hi"), "output: " + r.StandardOutput);
        }

        public void ProcessRunner_InvalidEnvironmentNames_Throw()
        {
            // "A=B" reached the child as A = "B=C".
            foreach (string key in new[] { "A=B", "", "A\0B" })
            {
                var options = new ProcessRunOptions { Environment = new Dictionary<string, string?> { [key] = "C" } };
                var ex = Check.Throws<ArgumentException>(() => ProcessRunner.Run("cmd.exe", "/c exit 0", options), "key " + key.Length);
                Check.Equal("Environment", ex.ParamName);
            }
            var nulValue = new ProcessRunOptions { Environment = new Dictionary<string, string?> { ["TB_OK"] = "a\0b" } };
            Check.Equal("Environment", Check.Throws<ArgumentException>(() => ProcessRunner.Run("cmd.exe", "/c exit 0", nulValue)).ParamName);
        }

        public void ProcessRunner_EmptyFileName_ThrowsArgumentException()
        {
            Check.Equal("fileName", Check.Throws<ArgumentException>(() => ProcessRunner.Run("")).ParamName);
            Check.Equal("fileName", Check.Throws<ArgumentException>(() => ProcessRunner.RunAsync("").GetAwaiter().GetResult()).ParamName);
        }

        public void ProcessRunner_KillTree_OfAnExitedChild_ReportsNotKilled()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            // A child that exited by itself as the timeout fired was reported with exit code -1, as if killed.
            using var p = System.Diagnostics.Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 5") { UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit();
            MethodInfo kill = typeof(ProcessRunner).GetMethod("KillTree", BindingFlags.NonPublic | BindingFlags.Static)!;
            object? killed = kill.Invoke(null, new object[] { p });
            Check.True(killed is bool b && !b, "an exited child was reported killed (or nothing was reported)");
        }

        // ---------- ConcurrentPipeline ----------

        public void Pipeline_Dispose_StopsHandingBufferedItemsToAConsumerThatIgnoresTheToken()
        {
            var gate = new ManualResetEventSlim();
            var started = new ManualResetEventSlim();
            int consumed = 0;
            var pipeline = new ConcurrentPipeline<int>((item, ct) =>
            {
                Interlocked.Increment(ref consumed);
                started.Set();
                gate.Wait(10000); // ignores the token
                return Task.CompletedTask;
            }, capacity: 10);
            for (int i = 0; i < 5; i++) Check.True(pipeline.TryEnqueue(i));
            Check.True(started.Wait(5000), "the consumer never started");
            var dispose = Task.Run(() => pipeline.Dispose());
            SpinWait.SpinUntil(() => false, 300);   // Dispose has cancelled and is waiting for the consumer
            gate.Set();
            Check.True(dispose.Wait(10000), "Dispose hung");
            Check.Equal(1, Volatile.Read(ref consumed), "items buffered at Dispose were still consumed");
        }

        // ---------- TaskExtensions ----------

        private static int s_unobserved;
        private static readonly string Marker = "tb-r72-unobserved-" + Guid.NewGuid().ToString("N");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AbandonByTimeout()
        {
            var tcs = new TaskCompletionSource<int>();
            Outcome(tcs.Task.WithTimeout(TimeSpan.FromMilliseconds(1)));
            tcs.SetException(new IOException(Marker));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AbandonByCancellation()
        {
            var tcs = new TaskCompletionSource<int>();
            using var cts = new CancellationTokenSource();
            var t = tcs.Task.WithCancellation(cts.Token);
            cts.Cancel();
            Outcome(t);
            tcs.SetException(new IOException(Marker));
        }

        private static void CheckObserved(Action abandon, string what)
        {
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            {
                if (e.Exception.InnerExceptions.Any(x => x.Message == Marker)) Interlocked.Increment(ref s_unobserved);
            };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                Volatile.Write(ref s_unobserved, 0);
                for (int i = 0; i < 5; i++) abandon();
                for (int i = 0; i < 5; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Check.Equal(0, Volatile.Read(ref s_unobserved), what + ": the abandoned task's fault went unobserved");
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }

        public void WithTimeout_AbandonedTaskFault_IsObserved() => CheckObserved(AbandonByTimeout, "WithTimeout");

        public void WithCancellation_AbandonedTaskFault_IsObserved() => CheckObserved(AbandonByCancellation, "WithCancellation");

        public void WithTimeout_NegativeTimeout_Throws()
        {
            // -0.5 ms timed out at once and -1.5 ms never did (Task.Delay truncated them to 0 and -1).
            foreach (double ms in new[] { -0.5, -1.5, -2 })
            {
                var tcs = new TaskCompletionSource<int>();
                var ex = Outcome(tcs.Task.WithTimeout(TimeSpan.FromTicks((long)(ms * TimeSpan.TicksPerMillisecond))), 2000);
                Check.True(ex is ArgumentOutOfRangeException, $"{ms} ms: got {ex?.GetType().Name ?? "no result (hung)"}");
                Check.Equal("timeout", ((ArgumentOutOfRangeException)ex!).ParamName);
                var ex2 = Outcome(((Task)tcs.Task).WithTimeout(TimeSpan.FromTicks((long)(ms * TimeSpan.TicksPerMillisecond))), 2000);
                Check.True(ex2 is ArgumentOutOfRangeException, $"{ms} ms (non-generic): got {ex2?.GetType().Name ?? "no result (hung)"}");
            }
        }

        public void WhenAllOrFirstException_ThrowsWhatAwaitWould()
        {
            var own = new AggregateException(new IOException("inner"));
            var e1 = Outcome(ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(new[] { Task.FromException<int>(own) }));
            Check.True(ReferenceEquals(own, e1), $"a task's own AggregateException was unwrapped to {e1?.GetType().Name}");

            var a = new IOException("a");
            var tcs = new TaskCompletionSource<int>();
            tcs.SetException(new Exception[] { a, new InvalidDataException("b") });
            var e2 = Outcome(ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(new[] { tcs.Task }));
            Check.True(ReferenceEquals(a, e2), $"a task faulted twice gave {e2?.GetType().Name}, not the first exception");
        }

        public void FireAndForget_ReportsWhatAwaitWould()
        {
            var own = new AggregateException(new IOException("inner"));
            Exception? got = null;
            var done = new ManualResetEventSlim();
            Task.FromException(own).FireAndForget(ex => { got = ex; done.Set(); });
            Check.True(done.Wait(5000), "onError never ran");
            Check.True(ReferenceEquals(own, got), $"onError got {got?.GetType().Name}, not the task's own exception");
        }

        // ---------- TaskRace ----------

        public void FirstMatching_CallerCancelsDuringARejectedAttempt_ThrowsCanceled()
        {
            using var cts = new CancellationTokenSource();
            var race = TaskRace.FirstMatching(new Func<CancellationToken, Task<int>>[]
            {
                async ct => { cts.Cancel(); await Task.Yield(); return 1; },
            }, v => v == 2, cts.Token);
            var ex = Outcome(race);
            Check.True(ex is OperationCanceledException, $"got {ex?.GetType().Name}");
        }

        public void FirstToComplete_CallerCancelled_ThrowsCanceledNotTheAttemptsFailure()
        {
            using var cts = new CancellationTokenSource();
            var race = TaskRace.FirstToComplete(new Func<CancellationToken, Task<int>>[]
            {
                async ct => { cts.Cancel(); await Task.Yield(); throw new IOException("lost"); },
            }, cts.Token);
            var ex = Outcome(race);
            Check.True(ex is OperationCanceledException, $"got {ex?.GetType().Name}");
            Check.True(((OperationCanceledException)ex!).CancellationToken == cts.Token, "not the caller's token");
        }

        public void Race_AlreadyCancelled_DoesNotSucceed()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var attempts = new Func<CancellationToken, Task<int>>[] { _ => Task.FromResult(5) };
            Check.True(Outcome(TaskRace.FirstSuccessful(attempts, cts.Token)) is OperationCanceledException, "FirstSuccessful succeeded");
            Check.True(Outcome(TaskRace.FirstToComplete(attempts, cts.Token)) is OperationCanceledException, "FirstToComplete succeeded");
            Check.True(Outcome(TaskRace.Hedged((_, __) => Task.FromResult(5), TimeSpan.FromSeconds(1), 2, cts.Token)) is OperationCanceledException,
                "Hedged succeeded");
        }

        // ---------- AsyncEvent ----------

        public void InvokeParallel_AlreadyCancelled_RunsNoHandler()
        {
            var ev = new AsyncEvent<int>();
            bool ran = false;
            ev.Subscribe((_, __) => { ran = true; return Task.CompletedTask; });
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = Outcome(ev.InvokeParallelAsync(1, cts.Token));
            Check.True(ex is OperationCanceledException, $"got {ex?.GetType().Name ?? "success"}");
            Check.False(ran, "a handler ran on a cancelled token");
        }

        public void InvokeSequential_HandlerHonouringTheCancellation_IsTheStopNotAFailure()
        {
            var ev = new AsyncEvent<int>();
            using var cts = new CancellationTokenSource();
            bool second = false;
            ev.Subscribe((_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
            ev.Subscribe((_, __) => { second = true; return Task.CompletedTask; });
            var ex = Outcome(ev.InvokeSequentialAsync(1, cts.Token));
            Check.True(ex is OperationCanceledException, $"got {ex?.GetType().Name}");
            Check.False(second, "a handler ran after the cancellation");
        }

        // ---------- ParallelUtils ----------

        private static IEnumerable<int> TwoThenThrow()
        {
            yield return 1;
            yield return 2;
            throw new InvalidDataException("source");
        }

        public void ForEach_SourceThrowsAfterABodyFailed_KeepsTheBodyFailure()
        {
            var task = ParallelUtils.ForEachAsync(TwoThenThrow(), 1, (i, _) =>
                i == 1 ? Task.FromException(new IOException("body")) : Task.CompletedTask, collectAllExceptions: true);
            var ex = Outcome(task);
            Check.True(ex is AggregateException, $"got {ex?.GetType().Name}: the body's failure was dropped");
            var inner = ((AggregateException)ex!).InnerExceptions;
            Check.True(inner.Any(e => e is InvalidDataException) && inner.Any(e => e is IOException), "missing one of the failures");
        }

        // ---------- AsyncLazy ----------

        public void AsyncLazy_NullTask_FaultsWithInvalidOperation()
        {
            var lazy = new AsyncLazy<int>(() => null!);
            Check.Throws<InvalidOperationException>(() => lazy.Value.GetAwaiter().GetResult());
        }

        // ---------- AtomicCounters ----------

        public void AtomicCounters_ToString_IsInvariantAndRoundTrips()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                var odd = (CultureInfo)CultureInfo.GetCultureInfo("de-DE").Clone();
                odd.NumberFormat.NegativeSign = "~";
                CultureInfo.CurrentCulture = odd;
                Check.Equal("-5", new AtomicLong(-5).ToString());
                double v = 0.1 + 0.2;
                string s = new AtomicDouble(v).ToString();
                Check.Equal(v, double.Parse(s, CultureInfo.InvariantCulture), "did not round-trip: " + s);
                Check.Equal("-1.5", new AtomicDouble(-1.5).ToString());
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        // ---------- AsyncManualResetEvent / AsyncCountdownEvent ----------

        public void WaitAsync_CancelledToken_WinsOverASetEvent()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Check.True(new AsyncManualResetEvent(true).WaitAsync(cts.Token).IsCanceled, "AsyncManualResetEvent");
            Check.True(new AsyncCountdownEvent(0).WaitAsync(cts.Token).IsCanceled, "AsyncCountdownEvent");
        }

        // ---------- CircuitBreaker ----------

        public void CircuitBreaker_CountersSaturateAndWrap_WithoutReplacingTheFailure()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var breaker = new CircuitBreaker(int.MaxValue, TimeSpan.FromMinutes(1), () => now);
            FieldInfo failures = typeof(CircuitBreaker).GetField("_consecutiveFailures", BindingFlags.NonPublic | BindingFlags.Instance)!;
            failures.SetValue(breaker, int.MaxValue - 1);
            for (int i = 0; i < 3; i++)
            {
                var ex = Outcome(breaker.ExecuteAsync<int>(_ => throw new IOException("op")));
                Check.True(ex is IOException || ex is CircuitOpenException, $"call {i}: got {ex?.GetType().Name}");
                now += TimeSpan.FromMinutes(2);
            }
            Check.True(breaker.ConsecutiveFailures > 0, $"the failure count went to {breaker.ConsecutiveFailures}");

            var b2 = new CircuitBreaker(1, TimeSpan.FromMinutes(1), () => now);
            typeof(CircuitBreaker).GetField("_generation", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(b2, int.MaxValue);
            var ex2 = Outcome(b2.ExecuteAsync<int>(_ => throw new IOException("op")));
            Check.True(ex2 is IOException, $"opening at the last generation threw {ex2?.GetType().Name}");
        }

        // ---------- Retry ----------

        public void DelayForAttempt_MatchesExactTicks()
        {
            var random = new Random(72);
            for (int trial = 0; trial < 3000; trial++)
            {
                long baseTicks = trial < 1000 ? trial + 1 : 1 + (long)(random.NextDouble() * 1e9);
                var backoff = (BackoffStrategy)(trial % 3);
                int attempt = 1 + random.Next(20);
                var policy = new RetryPolicy { BaseDelay = TimeSpan.FromTicks(baseTicks), MaxDelay = Timeout.InfiniteTimeSpan, Backoff = backoff };
                long factor = backoff == BackoffStrategy.Constant ? 1 : backoff == BackoffStrategy.Linear ? attempt : 1L << (attempt - 1);
                long expected = baseTicks * factor;   // well inside long here
                Check.Equal(TimeSpan.FromTicks(expected), policy.DelayForAttempt(attempt), $"{backoff} base {baseTicks} ticks attempt {attempt}");
            }
        }

        // ---------- TokenBucketRateLimiter ----------

        public void TokenBucket_LargeCount_IsNotGrantedWhenShort()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var bucket = new TokenBucketRateLimiter(int.MaxValue, 1e-6, () => now);
            Check.True(bucket.TryAcquire(2));
            Check.False(bucket.TryAcquire(int.MaxValue), "granted int.MaxValue with two tokens short");
            Check.True(bucket.TryAcquire(int.MaxValue - 2), "the rest is there");
        }

        // ---------- WhichExe ----------

        public void WhichExe_RelativePathEntry_GivesAFullPath()
        {
            string root = Path.Combine(Path.GetTempPath(), "tb-r72-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "relbin"));
            string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "tbr72tool.exe" : "tbr72tool";
            File.WriteAllText(Path.Combine(root, "relbin", exe), "");
            string savedDir = Environment.CurrentDirectory;
            string? savedPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.CurrentDirectory = root;
                Environment.SetEnvironmentVariable("PATH", "relbin");
                string? found = WhichExe.Find(exe);
                Check.NotNull(found);
                Check.True(Path.IsPathRooted(found!) && Path.GetFullPath(found!) == found, "not a full path: " + found);
                Check.Equal(Path.Combine(root, "relbin", exe), found);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", savedPath);
                Environment.CurrentDirectory = savedDir;
                Directory.Delete(root, true);
            }
        }
    }
}
