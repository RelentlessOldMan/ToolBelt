using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// Regressions from review round 81 (Threading, Resilience, Process, Runtime): each test failed before its fix.
    /// Anything that used to hang runs under a bounded wait, so a regression fails instead of stalling the run.
    /// </summary>
    public sealed class ConcurrencyReview81Tests
    {
        private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // The exception the task ends with (null on success), or a TimeoutException if it hasn't finished in time.
        private static Exception? Outcome(Task task, int milliseconds = 10000)
        {
            if (!task.Wait(milliseconds, CancellationToken.None) && !task.IsCompleted)
            {
                // Wait throws for a faulted or cancelled task; a task still running reaches here.
                return new TimeoutException("the task never finished");
            }
            return null;
        }

        private static Exception? Fault(Task task, int milliseconds = 10000)
        {
            try { return Outcome(task, milliseconds); }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1) { return ex.InnerException; }
            catch (Exception ex) { return ex; }
        }

        // ---------- ProcessRunner ----------

        public void ProcessRunner_InputTheEncoderRejects_ThrowsToTheCaller()
        {
            if (!Windows) return;
            // A strict encoder threw on the writer thread, where nothing caught it, and the host died.
            var options = new ProcessRunOptions
            {
                Encoding = new UTF8Encoding(false, true),
                StandardInput = "bad \uD800 surrogate",
                Timeout = TimeSpan.FromSeconds(20),
            };
            Check.Throws<EncoderFallbackException>(() => ProcessRunner.Run("cmd.exe", "/c more", options));
            var ex = Fault(ProcessRunner.RunAsync("cmd.exe", "/c more", options));
            Check.True(ex is EncoderFallbackException, $"RunAsync gave {ex?.GetType().Name ?? "no exception"}");
        }

        public void ProcessRunner_EnsureSuccess_ExitCodeIsInvariant()
        {
            if (!Windows) return;
            var culture = CultureInfo.CurrentCulture;
            try
            {
                // sv-SE writes a negative number with U+2212, not '-'.
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                var result = ProcessRunner.Run("cmd.exe", "/c exit -5");
                var ex = Check.Throws<ProcessRunException>(() => result.EnsureSuccess());
                Check.True(ex.Message.Contains("code -5"), "message: " + ex.Message);
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }

        // ---------- AsyncEvent ----------

        public void AsyncEvent_InvokeParallel_CallerCancel_ThrowsOperationCanceled()
        {
            var ev = new AsyncEvent<int>();
            ev.Subscribe((_, ct) => Task.Delay(Timeout.Infinite, ct));
            ev.Subscribe((_, ct) => Task.Delay(Timeout.Infinite, ct));
            using var cts = new CancellationTokenSource();
            var t = ev.InvokeParallelAsync(1, cts.Token);
            cts.CancelAfter(50);
            var ex = Fault(t);
            // It was an AggregateException of TaskCanceledExceptions; InvokeSequentialAsync throws OperationCanceledException.
            Check.True(ex is OperationCanceledException, $"got {ex?.GetType().Name ?? "no exception"}");
        }

        public void AsyncEvent_InvokeParallel_CancelWithAFailure_ReportsBoth()
        {
            var ev = new AsyncEvent<int>();
            ev.Subscribe((_, __) => Task.FromException(new IOException("boom")));
            ev.Subscribe((_, ct) => Task.Delay(Timeout.Infinite, ct));
            ev.Subscribe((_, ct) => Task.Delay(Timeout.Infinite, ct));
            using var cts = new CancellationTokenSource();
            var t = ev.InvokeParallelAsync(1, cts.Token);
            cts.CancelAfter(50);
            var ex = Fault(t);
            var agg = ex as AggregateException;
            Check.True(agg != null, $"got {ex?.GetType().Name ?? "no exception"}");
            // The failure, plus the cancellation once (it was one TaskCanceledException per cancelled handler).
            Check.Equal(2, agg!.InnerExceptions.Count);
            Check.True(agg.InnerExceptions[0] is IOException, agg.InnerExceptions[0].GetType().Name);
            Check.True(agg.InnerExceptions[1] is OperationCanceledException, agg.InnerExceptions[1].GetType().Name);
        }

        // ---------- TaskExtensions ----------

        private const string Marker = "review81 late fault";
        private static int s_unobserved;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AbandonLaterFault()
        {
            var first = new TaskCompletionSource<int>();
            var second = new TaskCompletionSource<int>();
            var t = ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(new[] { first.Task, second.Task });
            first.SetException(new IOException("first"));
            Fault(t);
            second.SetException(new IOException(Marker));
        }

        public void WhenAllOrFirstException_LaterFault_IsObserved()
        {
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            {
                if (e.Exception.InnerExceptions.Any(x => x.Message == Marker)) Interlocked.Increment(ref s_unobserved);
            };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                Volatile.Write(ref s_unobserved, 0);
                for (int i = 0; i < 5; i++) AbandonLaterFault();
                for (int i = 0; i < 5; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Check.Equal(0, Volatile.Read(ref s_unobserved), "a fault after the first went unobserved");
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }

        // ---------- Retry ----------

        public void Retry_DelayReturningNull_ThrowsInvalidOperationWithTheOperationsException()
        {
            var policy = new RetryPolicy { MaxAttempts = 2, DelayAsync = (_, __) => null! };
            var op = new TimeoutException("op");
            var ex = Fault(Retry.ExecuteAsync<int>(_ => Task.FromException<int>(op), policy));
            // It was a NullReferenceException, and the operation's own exception was lost.
            Check.True(ex is InvalidOperationException, $"got {ex?.GetType().Name ?? "no exception"}");
            Check.True(ReferenceEquals(op, ex!.InnerException), "the operation's exception is not the inner exception");
        }

        // ---------- ConcurrentPipeline ----------

        public void Pipeline_ProducerBlockedDuringDispose_GetsObjectDisposed()
        {
            var pipeline = new ConcurrentPipeline<int>((_, ct) => Task.Delay(Timeout.Infinite, ct), 1);
            Exception? error = null;
            var blocked = new Thread(() =>
            {
                try { for (int i = 0; i < 10; i++) pipeline.TryEnqueue(i); }
                catch (Exception ex) { error = ex; }
            }) { IsBackground = true };
            blocked.Start();
            Check.False(blocked.Join(300), "the producer never blocked on a full pipeline");
            pipeline.Dispose();
            Check.True(blocked.Join(10000), "the producer never woke");
            // It was an OperationCanceledException for the pipeline's own token, which the caller never saw.
            Check.True(error is ObjectDisposedException, $"got {error?.GetType().Name ?? "no exception"}");
            Check.Throws<ObjectDisposedException>(() => pipeline.TryEnqueue(99));
        }

        // ---------- TaskRace ----------

        public void Hedged_FailedAttempts_DoNotPileUpTimers()
        {
#if NET
            const int attempts = 5000;
            long before = Timer.ActiveCount;
            long during = -1;
            var t = TaskRace.Hedged((n, _) =>
            {
                if (n < attempts - 1) return Task.FromException<int>(new IOException("fail " + n));
                during = Timer.ActiveCount;
                return Task.FromResult(n);
            }, TimeSpan.FromHours(1), attempts);
            Check.Null(Fault(t, 60000));
            Check.Equal(attempts - 1, t.Result);
            // One timer per failed attempt stayed scheduled until the call ended.
            Check.True(during - before < 100, $"{during - before} timers live during the last attempt");
#endif
        }

        private const string HedgeMarker = "review81 hedge fault on cancel";

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HedgeFaultsAsTheCallerCancels()
        {
            using var cts = new CancellationTokenSource();
            var t = TaskRace.Hedged<int>((_, ct) =>
            {
                var tcs = new TaskCompletionSource<int>();
                ct.Register(() => tcs.TrySetException(new IOException(HedgeMarker)));
                return tcs.Task;
            }, Timeout.InfiniteTimeSpan, 1, cts.Token);
            cts.Cancel();
            var ex = Fault(t);
            Check.True(ex is OperationCanceledException, $"got {ex?.GetType().Name ?? "no exception"}");
        }

        public void Hedged_AttemptFaultingAsTheCallerCancels_IsObserved()
        {
            int unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            {
                if (e.Exception.InnerExceptions.Any(x => x.Message == HedgeMarker)) Interlocked.Increment(ref unobserved);
            };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                for (int i = 0; i < 5; i++) HedgeFaultsAsTheCallerCancels();
                for (int i = 0; i < 5; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Check.Equal(0, Volatile.Read(ref unobserved), "the attempt's fault went unobserved");
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }

        public void AsyncEvent_HandlersOwnCancellation_IsReportedAsThrown()
        {
            var own = new OperationCanceledException("the handler's own");
            var ev = new AsyncEvent<int>();
            ev.Subscribe((_, __) => Task.FromException(own));
            foreach (bool parallel in new[] { false, true })
            {
                var ex = Fault(parallel ? ev.InvokeParallelAsync(1) : ev.InvokeSequentialAsync(1));
                var agg = ex as AggregateException;
                Check.True(agg != null, $"parallel={parallel}: got {ex?.GetType().Name ?? "no exception"}");
                // Parallel replaced it with a new TaskCanceledException.
                Check.True(ReferenceEquals(own, agg!.InnerExceptions.Single()), $"parallel={parallel}: got {agg.InnerExceptions[0].GetType().Name}");
            }
        }

        public void AsyncEvent_NoHandlers_AlreadyCancelled_BothStylesThrow()
        {
            var ev = new AsyncEvent<int>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            // Sequential reported success where parallel threw.
            Check.True(Fault(ev.InvokeSequentialAsync(1, cts.Token)) is OperationCanceledException, "sequential");
            Check.True(Fault(ev.InvokeParallelAsync(1, cts.Token)) is OperationCanceledException, "parallel");
        }

        // ---------- ArrayPoolScope ----------

        public void ArrayPoolScope_LongerThanThePoolKeeps_IsClearedAndNotPooled()
        {
#if NET
            // 1 GB of bytes, held only for the test.
            byte[] array;
            using (var scope = ToolBelt.Runtime.ArrayPoolScope.Rent<byte>((1 << 30) + 1, clearOnReturn: true))
            {
                // The pool doesn't keep an array this long: Return dropped it without the clear.
                Check.False(scope.IsPooled, "reported pooled");
                array = scope.Array;
                array[0] = 42;
                array[array.Length - 1] = 42;
            }
            Check.Equal((byte)0, array[0]);
            Check.Equal((byte)0, array[array.Length - 1]);
            array = null!;
            GC.Collect();
#endif
        }

        // ---------- WhichExe ----------

        public void WhichExe_NoPathext_UsesTheShellsDefaultOrder()
        {
            if (!Windows) return;
            string dir = Path.Combine(Path.GetTempPath(), "tb81-" + Guid.NewGuid().ToString("N"));
            string? pathext = Environment.GetEnvironmentVariable("PATHEXT");
            Directory.CreateDirectory(dir);
            try
            {
                foreach (string ext in new[] { ".COM", ".EXE", ".BAT", ".CMD" }) File.WriteAllText(Path.Combine(dir, "zzfoo" + ext), "");
                Environment.SetEnvironmentVariable("PATHEXT", null); // this process only
                string? found = WhichExe.Find(Path.Combine(dir, "zzfoo"));
                // It was zzfoo.EXE; cmd's default PATHEXT is .COM;.EXE;.BAT;.CMD.
                Check.True(found != null && found.EndsWith(".COM", StringComparison.OrdinalIgnoreCase), "found " + (found ?? "nothing"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATHEXT", pathext);
                Directory.Delete(dir, true);
            }
        }

        // ---------- ProcessRunner output ----------

        public void ProcessRunner_NoTimeout_GrandchildStreamingWithoutNewlines_IsReadToTheEnd()
        {
            if (!Windows) return;
            // The child waits ~2 s so the grandchild is writing when it exits; the grandchild then writes a dot every 300 ms
            // with no line break. Counted by whole lines, that looked quiet: the run ended early and dropped the dots.
            var r = ProcessRunner.Run("cmd.exe",
                "/c start \"\" /b powershell -NoProfile -Command \"1..15 | % { [Console]::Out.Write('.'); [Console]::Out.Flush(); Start-Sleep -Milliseconds 300 }\" & ping -n 3 127.0.0.1 >nul");
            Check.False(r.TimedOut, "timed out");
            Check.Equal(new string('.', 15) + Environment.NewLine, r.StandardOutput);
        }

        public void ProcessRunner_LinesSplitAcrossReads_MatchReadLine()
        {
            if (!Windows) return;
            // Line breaks of every kind, a \r\n split by a pause between writes, and an unfinished last line.
            string script = Path.Combine(Path.GetTempPath(), "tb81-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(script,
                "$o = [Console]::OpenStandardOutput()\n" +
                "function W($s) { $b = [Text.Encoding]::ASCII.GetBytes($s); $o.Write($b, 0, $b.Length); $o.Flush() }\n" +
                "W \"a`r\"\nStart-Sleep -Milliseconds 300\nW \"`nb`rc`n`nd\"\n");
            try
            {
                var lines = new List<string>();
                var r = ProcessRunner.Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                    new ProcessRunOptions { OnOutputLine = l => { lock (lines) lines.Add(l); }, Timeout = TimeSpan.FromSeconds(30) });
                string nl = Environment.NewLine;
                Check.Equal("a" + nl + "b" + nl + "c" + nl + nl + "d" + nl, r.StandardOutput);
                lock (lines) Check.Equal("a|b|c||d", string.Join("|", lines));
            }
            finally { File.Delete(script); }
        }

        // ---------- Jitter ----------

        public void Jitter_Equal_NeverBelowHalfTheDelay()
        {
            var rng = new Random(81);
            for (long ticks = 1; ticks <= 2000; ticks++)
                foreach (double r in new[] { 0.0, rng.NextDouble(), 0.9999999999 })
                {
                    long got = Jitter.Apply(TimeSpan.FromTicks(ticks), JitterMode.Equal, () => r).Ticks;
                    // Rounded down, an odd count fell below delay/2 (3 ticks gave 1).
                    Check.True(2 * got >= ticks, $"{ticks} ticks, r={r}: {got}");
                    Check.True(got < ticks || ticks == 1, $"{ticks} ticks, r={r}: {got} not below the delay");
                }
        }
    }
}
