using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Round 219 (concurrency, thirteenth pass): each regression failed on the code before its fix.
    public sealed class ConcurrencyReview219Tests
    {
        // ---- ConcurrentPipeline ----

        // A second Dispose, made while the first was still waiting for the consumer, returned at once with the consumer
        // still running (R97-3; PeriodicWorker's was fixed in round 206).
        public void Pipeline_SecondDispose_WaitsForTheConsumer()
        {
            for (int trial = 0; trial < 5; trial++)
            {
                int running = 0;
                var started = new ManualResetEventSlim();
                var p = new ConcurrentPipeline<int>((i, ct) =>
                {
                    Volatile.Write(ref running, 1);
                    started.Set();
                    Thread.Sleep(300); // ignores the token
                    Volatile.Write(ref running, 0);
                    return Task.CompletedTask;
                }, 4);
                Check.True(p.TryEnqueue(1));
                Check.True(started.Wait(TimeSpan.FromSeconds(10)));
                var first = Task.Run(() => p.Dispose());
                Thread.Sleep(30);
                p.Dispose();
                Check.Equal(0, Volatile.Read(ref running), "trial " + trial);
                Check.True(first.Wait(TimeSpan.FromSeconds(10)));
            }
        }

        // Guard: a Dispose from a callback on the token the first Dispose is cancelling must not wait. Callbacks run last
        // registered first, so the consumer's own wait (registered before it) is cancelled only after it returns:
        // waiting there for the consumer would wait for good. (Not a regression: it guards the second-Dispose wait.)
        public void Pipeline_DisposeFromATokenCallback_DoesNotWait()
        {
            ConcurrentPipeline<int>? p = null;
            var registered = new ManualResetEventSlim();
            p = new ConcurrentPipeline<int>(async (i, ct) =>
            {
                Task wait = Task.Delay(Timeout.Infinite, ct);
                // Without the consumer's flow (a registration under the callback's flow runs in it and is recognised).
                using (ExecutionContext.SuppressFlow()) ct.Register(() => p!.Dispose());
                registered.Set();
                await wait;
            }, 4);
            Check.True(p.TryEnqueue(1));
            Check.True(registered.Wait(TimeSpan.FromSeconds(10)));
            var dispose = Task.Run(() => p.Dispose());
            Check.True(dispose.Wait(TimeSpan.FromSeconds(10)), "Dispose hung");
        }

        // ---- ProcessRunner ----

        private static int Threads()
        {
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            return me.Threads.Count;
        }

        // Each run whose grandchild kept the inherited pipes left its two reader threads blocked in Read, with the pipe
        // handles, until the grandchild exited: ten runs that left a daemon behind took the host from 7 threads to 31.
        public void ProcessRunner_GrandchildHoldingThePipes_DoesNotKeepTheReaderThreads()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            const string Args = "/c \"start /b ping -n 12 127.0.0.1 >nul & echo hi\"";
            ProcessRunner.Run("cmd.exe", "/c echo warm"); // the pool and the runner's statics, before the baseline
            int before = Threads();
            const int Runs = 6;
            for (int i = 0; i < Runs; i++)
            {
                var r = ProcessRunner.Run("cmd.exe", Args);
                Check.Equal(0, r.ExitCode);
                Check.True(r.StandardOutput.Contains("hi"), r.StandardOutput);
            }
            // The grandchildren live about 11 s more; their runs' readers must be gone well before that.
            int after = 0;
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 4000)
            {
                after = Threads();
                if (after <= before + 3) break;
                Thread.Sleep(100);
            }
            Check.True(after <= before + 3, $"threads {before} -> {after} after {Runs} runs");
        }

        // The same for RunAsync.
        public async Task ProcessRunner_GrandchildHoldingThePipes_Async_DoesNotKeepTheReaderThreads()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            await ProcessRunner.RunAsync("cmd.exe", "/c echo warm");
            int before = Threads();
            for (int i = 0; i < 6; i++)
            {
                var r = await ProcessRunner.RunAsync("cmd.exe", "/c \"start /b ping -n 12 127.0.0.1 >nul & echo hi\"");
                Check.True(r.StandardOutput.Contains("hi"), r.StandardOutput);
            }
            int after = 0;
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 4000)
            {
                after = Threads();
                if (after <= before + 3) break;
                await Task.Delay(100);
            }
            Check.True(after <= before + 3, $"threads {before} -> {after} after 6 runs");
        }

        private const string ThirtyLines = "/c for /L %i in (1,1,30) do @echo line%i";

        // A child that exited in milliseconds was reported TimedOut (exit code 0) when OnOutputLine took 100 ms a line,
        // because the callback's backlog counted against the child's timeout, and the callback missed 12 of the 30 lines
        // StandardOutput held.
        public void ProcessRunner_SlowCallbackBacklog_IsNotATimeout()
        {
            int calls = 0;
            var r = ProcessRunner.Run("cmd.exe", ThirtyLines, new ProcessRunOptions
            {
                Timeout = TimeSpan.FromSeconds(2),
                OnOutputLine = l => { Thread.Sleep(100); Interlocked.Increment(ref calls); },
            });
            Check.False(r.TimedOut, "a child that exited on time was reported timed out");
            Check.Equal(0, r.ExitCode);
            Check.Equal(30, Volatile.Read(ref calls));
            Check.Equal(30, r.StandardOutput.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length);
        }

        public async Task ProcessRunner_SlowCallbackBacklog_IsNotATimeout_Async()
        {
            int calls = 0;
            var r = await ProcessRunner.RunAsync("cmd.exe", ThirtyLines, new ProcessRunOptions
            {
                Timeout = TimeSpan.FromSeconds(2),
                OnOutputLine = l => { Thread.Sleep(100); Interlocked.Increment(ref calls); },
            });
            Check.False(r.TimedOut, "a child that exited on time was reported timed out");
            Check.Equal(30, Volatile.Read(ref calls));
        }

        // Guard: a callback stuck on one line still ends the run at its timeout (plus the grace period), timed out.
        public void ProcessRunner_StuckCallback_StillTimesOut()
        {
            using var release = new ManualResetEventSlim();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = ProcessRunner.Run("cmd.exe", ThirtyLines, new ProcessRunOptions
                {
                    Timeout = TimeSpan.FromSeconds(1),
                    OnOutputLine = l => release.Wait(TimeSpan.FromSeconds(20)),
                });
                Check.True(r.TimedOut);
                Check.True(sw.ElapsedMilliseconds < 8000, "took " + sw.ElapsedMilliseconds + " ms");
            }
            finally { release.Set(); }
        }

        // ---- WhichExe ----

        // A drive-relative name ("C:tool.exe") went down the PATH loop, where Path.Combine dropped each folder for the
        // rooted name: it found the file in that drive's current directory with any PATH set, and null with an empty one.
        public void WhichExe_DriveRelativeName_DoesNotDependOnPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            string cwd = Environment.CurrentDirectory;
            string file = "tb219-" + Guid.NewGuid().ToString("N") + ".exe";
            string full = Path.Combine(cwd, file);
            string? oldPath = Environment.GetEnvironmentVariable("PATH");
            File.WriteAllBytes(full, new byte[0]);
            try
            {
                string name = cwd.Substring(0, 2) + file; // "C:tb219-....exe"
                Environment.SetEnvironmentVariable("PATH", "");
                string? withNoPath = WhichExe.Find(name);
                Environment.SetEnvironmentVariable("PATH", oldPath);
                string? withPath = WhichExe.Find(name);
                Check.Equal(full, withNoPath ?? "(null)");
                Check.Equal(full, withPath ?? "(null)");
                // With PATHEXT, as for a name with a folder.
                Environment.SetEnvironmentVariable("PATH", "");
                string? viaPathExt = WhichExe.Find(name.Substring(0, name.Length - 4));
                Check.True(string.Equals(full, viaPathExt, StringComparison.OrdinalIgnoreCase), viaPathExt ?? "(null)");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", oldPath);
                File.Delete(full);
            }
        }

        // "cmd.exe::$DATA" (an NTFS stream) resolved to the stream's path on .NET 8 and to null on .NET Framework.
        public void WhichExe_StreamName_IsNullOnEveryRuntime()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            Check.True(WhichExe.Find("cmd.exe") != null, "cmd.exe itself must resolve");
            foreach (string name in new[] { "cmd.exe::$DATA", "cmd.EXE::$DATA", "cmd::$DATA", "cmd.exe:x", "C:cmd.exe:x", "C:" })
                Check.Equal("(null)", WhichExe.Find(name) ?? "(null)", name);
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Check.Equal("(null)", WhichExe.Find(Path.Combine(sys, "cmd.exe") + "::$DATA") ?? "(null)");
            // Guard: a full path with its drive, and an extended one, still resolve.
            Check.True(WhichExe.Find(Path.Combine(sys, "cmd.exe")) != null);
            Check.True(WhichExe.Find(@"\\?\" + Path.Combine(sys, "cmd.exe")) != null);
        }
    }
}
