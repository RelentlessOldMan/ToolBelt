using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.IO;
using ToolBelt.Logging;
using ToolBelt.Net;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    /// <summary>
    /// Regressions from review round 55 (cross-cutting: cancellation and dispose races). A cancelled call must throw an
    /// exception carrying the caller's own token, a cancelled token must stop the work it guards, and a disposed object
    /// must say so rather than report success, keep working, or wait behind a blocked operation. Each test failed before
    /// its fix, except the guards (named as such).
    /// </summary>
    public sealed class CancelDisposeReview55Tests
    {
        private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static async Task<bool> Within(Func<bool> condition, int milliseconds = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > milliseconds) return false;
                await Task.Delay(10);
            }
            return true;
        }

        // ---------- the caller's token ----------

        public async Task AsyncLock_CancelWhileWaiting_CarriesTheCallersToken()
        {
            using var gate = new AsyncLock();
            using (await gate.LockAsync())
            {
                using var cts = new CancellationTokenSource();
                Task<IDisposable> waiting = gate.LockAsync(cts.Token);
                cts.Cancel();
                var ex = await Check.ThrowsAsync<OperationCanceledException>(() => waiting);
                Check.True(ex.CancellationToken == cts.Token, "the caller's token, not the linked one");
            }
        }

        public async Task Bulkhead_CancelWhileQueued_CarriesTheCallersToken()
        {
            using var bulkhead = new Bulkhead(1);
            var release = new TaskCompletionSource<bool>();
            Task running = bulkhead.ExecuteAsync(_ => release.Task);
            using var cts = new CancellationTokenSource();
            Task queued = bulkhead.ExecuteAsync(_ => Task.CompletedTask, cts.Token);
            cts.Cancel();
            var ex = await Check.ThrowsAsync<OperationCanceledException>(() => queued);
            Check.True(ex.CancellationToken == cts.Token, "the caller's token, not the linked one");
            release.SetResult(true);
            await running;
        }

        public async Task HttpDownload_CancelDuringTheBody_CarriesTheCallersToken()
        {
            // Cancelled from the server once the head is out, so the client is waiting in the body read. (OnProgress can't
            // be the trigger: .NET Framework's response stream doesn't return the partial body, so it never fired there.)
            using var cts = new CancellationTokenSource();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
                    _ = Task.Run(async () =>
                    {
                        using (c)
                        {
                            try
                            {
                                NetworkStream ns = c.GetStream();
                                var buf = new byte[4096];
                                await ns.ReadAsync(buf, 0, buf.Length);
                                byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\nhello");
                                await ns.WriteAsync(head, 0, head.Length);
                                await ns.FlushAsync();
                                cts.CancelAfter(300);
                                await Task.Delay(10000);   // the rest of the body never comes
                            }
                            catch { }
                        }
                    });
                }
            });
            string dest = Path.Combine(Path.GetTempPath(), "tb-r55-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new HttpDownloadOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(10) };
                var ex = await Check.ThrowsAsync<OperationCanceledException>(
                    () => HttpDownload.ToFileAsync($"http://127.0.0.1:{port}/file", dest, options, cts.Token));
                Check.True(ex.CancellationToken == cts.Token, "the caller's token, not the idle timer's");
            }
            finally
            {
                listener.Stop();
                foreach (string f in Directory.GetFiles(Path.GetDirectoryName(dest)!, Path.GetFileName(dest) + "*"))
                    try { File.Delete(f); } catch { }
            }
        }

        // ---------- a cancelled token stops the work ----------

#if !NETFRAMEWORK
        private sealed class Rec { public int N { get; set; } }

        public async Task JsonLines_ReadAsync_CancelMidStream_StopsAtTheNextRecord()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 100; i++) sb.Append("{\"N\":").Append(i).Append("}\n");
            using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())));
            using var cts = new CancellationTokenSource();
            int n = 0;
            await Check.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (Rec r in JsonLines.ReadAsync<Rec>(reader, null, cts.Token))
                    if (++n == 3) cts.Cancel();
            });
            Check.Equal(3, n);   // every buffered line came back before: all 100
        }
#endif

        public async Task ConcurrentPipeline_DisposeDuringTheDrain_CompleteAsyncThrows()
        {
            int processed = 0;
            var started = new ManualResetEventSlim();
            using var pipeline = new ConcurrentPipeline<int>(async (x, ct) =>
            {
                started.Set();
                await Task.Delay(Timeout.Infinite, ct);
                Interlocked.Increment(ref processed);
            }, 100);
            for (int i = 0; i < 10; i++) pipeline.TryEnqueue(i);
            started.Wait(5000);
            Task complete = pipeline.CompleteAsync();
            pipeline.Dispose();
            await Check.ThrowsAsync<ObjectDisposedException>(() => complete);   // it reported success with 0 of 10 done
            Check.Equal(0, processed);
        }

        public async Task ConcurrentPipeline_DisposeAfterTheDrain_CompleteAsyncSucceeds_Guard()
        {
            int processed = 0;
            var pipeline = new ConcurrentPipeline<int>((x, ct) => { Interlocked.Increment(ref processed); return Task.CompletedTask; }, 100);
            for (int i = 0; i < 10; i++) pipeline.TryEnqueue(i);
            await pipeline.CompleteAsync();
            pipeline.Dispose();
            Check.Equal(10, processed);
        }

        public async Task ConcurrentPipeline_CompleteEmptyThenDispose_Succeeds_Guard()
        {
            var pipeline = new ConcurrentPipeline<int>((x, ct) => Task.CompletedTask, 4);
            Task complete = pipeline.CompleteAsync();
            pipeline.Dispose();
            await complete;   // nothing was buffered, so nothing was lost
        }

        // ---------- Ctrl+C: a throwing token callback ----------

#if !NETFRAMEWORK
        // Raises Console.CancelKeyPress the way the runtime does, without a real console signal.
        private static bool RaiseCtrlC()
        {
            FieldInfo? field = typeof(Console).GetField("s_cancelCallbacks", BindingFlags.NonPublic | BindingFlags.Static);
            Check.NotNull(field);
            var handlers = (ConsoleCancelEventHandler?)field!.GetValue(null);
            Check.NotNull(handlers);
            ConstructorInfo ctor = typeof(ConsoleCancelEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(c => c.GetParameters().Length == 1);
            var args = (ConsoleCancelEventArgs)ctor.Invoke(new object[] { ConsoleSpecialKey.ControlC });
            handlers!(null, args);
            return args.Cancel;
        }

        public async Task ConsoleApp_CtrlC_ThrowingTokenCallback_DoesNotEscapeTheHandler()
        {
            var registered = new TaskCompletionSource<bool>();
            var err = new StringWriter();
            Task<int> run = ConsoleApp.RunAsync(async ct =>
            {
                ct.Register(() => throw new InvalidOperationException("callback"));
                registered.SetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
                return 0;
            }, err, verbose: false, hookConsole: true, externalToken: CancellationToken.None);
            await registered.Task;
            bool suppressed = RaiseCtrlC();   // threw the callback's AggregateException out of the handler: a process crash
            Check.True(suppressed, "the first Ctrl+C is still suppressed");
            Check.Equal(ExitCodes.Interrupted, await run);
            Check.True(err.ToString().Contains("callback"), "the callback's failure is reported");
        }

        public void ConsoleApp_InterruptSource_ThrowingTokenCallback_DoesNotEscapeTheHandler()
        {
            using CancellationTokenSource source = ConsoleApp.CreateInterruptSource();
            source.Token.Register(() => throw new InvalidOperationException("callback"));
            Check.True(RaiseCtrlC());
            Check.True(source.IsCancellationRequested);
        }
#endif

        // ---------- ProcessRunner ----------

        public void ProcessRunner_CallbackStillRunningAtTheTimeout_IsWaitedForAndItsErrorSurfaces()
        {
            if (!Windows) return;
            bool returned = false, calledAfterReturn = false;
            int lines = 0;
            var options = new ProcessRunOptions
            {
                Timeout = TimeSpan.FromMilliseconds(300),
                OnOutputLine = _ =>
                {
                    if (Volatile.Read(ref returned)) calledAfterReturn = true;
                    if (Interlocked.Increment(ref lines) != 2) return;
                    Thread.Sleep(1500);   // past the kill and the second's grace for the output
                    throw new FormatException("late");
                },
            };
            Exception? thrown = null;
            try { ProcessRunner.Run("cmd.exe", "/c \"for /L %i in (1,0,2) do @echo x\"", options); }
            catch (Exception ex) { thrown = ex; }
            Volatile.Write(ref returned, true);
            Thread.Sleep(300);
            Check.True(thrown is FormatException, "the callback's exception reaches the caller, got " + (thrown?.GetType().Name ?? "none"));
            Check.False(calledAfterReturn, "no callback after the run returned");
        }

        public async Task ProcessRunner_RunAsync_CallbackStillRunningAtTheTimeout_IsWaitedFor()
        {
            if (!Windows) return;
            int lines = 0;
            var options = new ProcessRunOptions
            {
                Timeout = TimeSpan.FromMilliseconds(300),
                OnOutputLine = _ =>
                {
                    if (Interlocked.Increment(ref lines) != 2) return;
                    Thread.Sleep(1500);
                    throw new FormatException("late");
                },
            };
            await Check.ThrowsAsync<FormatException>(() => ProcessRunner.RunAsync("cmd.exe", "/c \"for /L %i in (1,0,2) do @echo x\"", options));
        }

        public void ProcessRunner_GrandchildHoldsThePipes_KeepsTheChildsExitCode()
        {
            if (!Windows) return;
            var options = new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(2) };
            ProcessResult r = ProcessRunner.Run("cmd.exe", "/c \"start /b ping -n 5 127.0.0.1 >nul & echo parent-done & exit 3\"", options);
            Check.True(r.TimedOut, "the output wait still timed out");
            Check.Equal(3, r.ExitCode);   // was -1: the child had exited on its own
            Check.True(r.StandardOutput.Contains("parent-done"));
        }

        // ---------- after Dispose ----------

        private static LogEvent Event() => new LogEvent(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", "m", null);

        private sealed class NullSink : ILogSink
        {
            public void Emit(LogEvent logEvent) { }
            public void Dispose() { }
        }

        public void AsyncLogSink_EmitAfterDispose_ThrowsObjectDisposed()
        {
            var sink = new AsyncLogSink(new NullSink());
            sink.Dispose();
            Check.Throws<ObjectDisposedException>(() => sink.Emit(Event()));   // was InvalidOperationException
        }

        public void SyslogSink_EmitAfterDispose_ThrowsObjectDisposed()
        {
            var sink = new SyslogSink("127.0.0.1", 9, SyslogTransport.Udp);
            sink.Dispose();
            Check.Throws<ObjectDisposedException>(() => sink.Emit(Event()));   // was silently dropped
        }

        public void SyslogSink_Dispose_DoesNotWaitBehindABlockedConnect()
        {
            var sink = new SyslogSink("10.255.255.1", 601, SyslogTransport.Tcp) { ConnectTimeout = TimeSpan.FromSeconds(6) }; // non-routable
            var emitting = new Thread(() => { try { sink.Emit(Event()); } catch { } }) { IsBackground = true };
            var watch = Stopwatch.StartNew();
            emitting.Start();
            Thread.Sleep(500);   // the emit is now inside the connect, holding the sink's lock
            if (!emitting.IsAlive) return;   // no route at all here: the connect failed at once, nothing to race
            var disposing = Stopwatch.StartNew();
            sink.Dispose();
            disposing.Stop();
            Check.True(disposing.ElapsedMilliseconds < 2000, $"Dispose took {disposing.ElapsedMilliseconds} ms");
            Check.True(emitting.Join(3000), "the blocked emit was released");
        }

        public void CountingStream_LeaveOpen_ClosedAfterDispose()
        {
            var inner = new MemoryStream();
            var s = new CountingStream(inner, leaveOpen: true);
            s.Write(new byte[3], 0, 3);
            s.Dispose();
            Check.False(s.CanWrite);
            Check.False(s.CanRead);
            Check.Throws<ObjectDisposedException>(() => s.Write(new byte[3], 0, 3));
            Check.Throws<ObjectDisposedException>(() => s.Read(new byte[3], 0, 3));
            Check.Equal(3L, s.BytesWritten);
            Check.True(inner.CanWrite, "the inner stream was left open");
        }

        public void TeeStream_LeaveOpen_ClosedAfterDispose()
        {
            var primary = new MemoryStream();
            var copy = new MemoryStream();
            var s = new TeeStream(primary, copy, leavePrimaryOpen: true, leaveCopyOpen: true);
            s.Write(new byte[2], 0, 2);
            s.Dispose();
            Check.False(s.CanWrite);
            Check.Throws<ObjectDisposedException>(() => s.Write(new byte[2], 0, 2));
            Check.Equal(2L, copy.Length);   // the write after Dispose still teed
            Check.True(primary.CanWrite && copy.CanWrite, "both left open");
        }

        public async Task FileWatcher_Dispose_WaitsForAFlushRaisingOnAnotherThread()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r55-fw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var w = new FileWatcher(dir, new FileWatcherOptions { Debounce = TimeSpan.FromSeconds(30) });
                var entered = new ManualResetEventSlim();
                bool finished = false;
                w.Changed += (_, __) => { entered.Set(); Thread.Sleep(500); Volatile.Write(ref finished, true); };
                w.Start();
                File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
                await Task.Delay(1000);   // the OS event arrives; the 30 s debounce keeps it pending for Flush
                var flushing = new Thread(() => { try { w.Flush(); } catch { } }) { IsBackground = true };
                flushing.Start();
                Check.True(entered.Wait(5000), "the handler is running");
                w.Dispose();
                Check.True(Volatile.Read(ref finished), "Dispose returned while a handler was still raising");
                flushing.Join(5000);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
