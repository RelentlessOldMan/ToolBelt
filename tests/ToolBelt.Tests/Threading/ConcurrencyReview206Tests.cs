using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Resilience;
using ToolBelt.Tests.Framework;
using ToolBelt.Threading;

namespace ToolBelt.Tests.Threading
{
    // Round 206 (concurrency, twelfth pass): each regression failed on the code before its fix.
    public sealed class ConcurrencyReview206Tests
    {
        private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // ---- WhichExe: PATH parsing as cmd does it ----

        private static void WithTool(string folderName, Func<string, string> pathFor, string toolName)
        {
            string root = Path.Combine(Path.GetTempPath(), "tb r206 " + Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(root, folderName);
            Directory.CreateDirectory(dir);
            string? oldPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                string tool = Path.Combine(dir, toolName + ".cmd");
                File.WriteAllText(tool, "@echo hi");
                Environment.SetEnvironmentVariable("PATH", pathFor(dir) + ";" + oldPath);
                string? found = WhichExe.Find(toolName);
                Check.True(string.Equals(tool, found, StringComparison.OrdinalIgnoreCase), "PATH entry not searched: got " + (found ?? "null"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", oldPath);
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        // A quoted entry holding a ';' ("C:\a;b") was split at it into two broken pieces; cmd ran the file.
        public void WhichExe_QuotedEntryWithSemicolon_IsOneFolder()
        {
            if (!Windows) return;
            WithTool("a;b", dir => "\"" + dir + "\"", "tbr206semi");
            WithTool("c;d;e", dir => "\"" + dir + "\"", "tbr206semi2");
            WithTool("plain", dir => "\"" + dir.Substring(0, 3) + "\"" + dir.Substring(3), "tbr206part"); // quotes around part
        }

        // A leading space ("C:\a; C:\b") was read as a relative folder on .NET 8 (null) but trimmed on .NET Framework.
        public void WhichExe_EntryWithLeadingSpace_IsSearched()
        {
            if (!Windows) return;
            WithTool("plain", dir => " " + dir, "tbr206space");
            WithTool("plain", dir => "   " + dir, "tbr206space3");
            WithTool("plain", dir => " \"" + dir + "\" ", "tbr206spaceq");
        }

        // ---- PeriodicWorker: a second Dispose waits too ----

        // Two Disposes at once: the loser returned at once while the in-flight iteration was still running (R97-3).
        public void PeriodicWorker_ConcurrentDispose_BothWaitForTheIteration()
        {
            for (int round = 0; round < 20; round++)
            {
                int running = 0;
                var entered = new ManualResetEventSlim(false);
                var w = new PeriodicWorker(async ct =>
                {
                    Volatile.Write(ref running, 1);
                    entered.Set();
                    Thread.Sleep(50);
                    Volatile.Write(ref running, 0);
                    await Task.Yield();
                }, TimeSpan.FromHours(1));
                w.Start();
                Check.True(entered.Wait(TimeSpan.FromSeconds(10)));
                var go = new ManualResetEventSlim(false);
                int stillRunning = 0;
                Thread[] ts = new Thread[2];
                for (int i = 0; i < 2; i++)
                {
                    ts[i] = new Thread(() =>
                    {
                        go.Wait();
                        w.Dispose();
                        if (Volatile.Read(ref running) == 1) Interlocked.Increment(ref stillRunning);
                    });
                    ts[i].Start();
                }
                go.Set();
                foreach (Thread t in ts) Check.True(t.Join(TimeSpan.FromSeconds(20)), "Dispose hung");
                Check.Equal(0, stillRunning, "a Dispose returned while the work was running (round " + round + ")");
            }
        }

        // A Dispose from a callback on the work's token, run by the first Dispose's cancel, must not wait on the loop.
        public void PeriodicWorker_DisposeFromTokenCallback_DoesNotHang()
        {
            PeriodicWorker? w = null;
            var entered = new ManualResetEventSlim(false);
            w = new PeriodicWorker(async ct =>
            {
                ct.Register(() => w!.Dispose());
                entered.Set();
                await Task.Delay(Timeout.Infinite, ct);
            }, TimeSpan.FromHours(1));
            w.Start();
            Check.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var t = Task.Run(() => w.Dispose());
            Check.True(t.Wait(TimeSpan.FromSeconds(20)), "Dispose hung");
        }

        // The work completed a TaskCompletionSource whose awaiter ran inline on the loop's thread and disposed the
        // worker: Dispose waited for the loop it was running on and hung (R128-1, the PeriodicWorker half).
        public void PeriodicWorker_DisposeFromInlineContinuation_DoesNotHang()
        {
            var signal = new TaskCompletionSource<bool>();
            int runs = 0;
            var w = new PeriodicWorker(ct =>
            {
                Interlocked.Increment(ref runs);
                signal.TrySetResult(true);
                return Task.CompletedTask;
            }, TimeSpan.Zero);
            Task disposer = signal.Task.ContinueWith(_ => w.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
            w.Start();
            Check.True(disposer.Wait(TimeSpan.FromSeconds(20)), "Dispose from the inline continuation hung");
            Check.True(disposer.Exception is null);
            int seen = Volatile.Read(ref runs);
            Thread.Sleep(100);
            Check.Equal(seen, Volatile.Read(ref runs), "the loop kept running after Dispose");
            w.Dispose(); // a later Dispose from another thread waits for the stopped loop and returns
        }

        // Dispose straight from the work's synchronous part stops the loop too.
        public void PeriodicWorker_DisposeFromSynchronousWork_DoesNotHang()
        {
            PeriodicWorker? w = null;
            var done = new ManualResetEventSlim(false);
            w = new PeriodicWorker(ct => { w!.Dispose(); done.Set(); return Task.CompletedTask; }, TimeSpan.Zero);
            w.Start();
            Check.True(done.Wait(TimeSpan.FromSeconds(20)), "Dispose from the work hung");
            w.Dispose();
        }

        // ---- TokenBucketRateLimiter: capacity a double can count ----

        // Above 2^53 tokens - 1 == tokens: from a bucket of 1e17 every acquire was granted and the bucket never emptied.
        public void TokenBucket_LargeBucket_DrawsDownExactly()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            foreach (double cap in new[] { 1e17, 18014398509481984.0, 1e18 })
            {
                var lim = new TokenBucketRateLimiter(cap, 1e-9, () => now);
                for (int i = 0; i < 1000; i++) Check.True(lim.TryAcquire(1));
                double ulp = Math.Pow(2, Math.Floor(Math.Log(cap, 2)) - 52);
                Check.True(lim.AvailableTokens < cap, "nothing was taken from " + cap);
                Check.True(Math.Abs(lim.AvailableTokens - (cap - 1000)) <= ulp, "capacity " + cap + ": " + lim.AvailableTokens);
            }
            // A refill past double.MaxValue (a huge rate after a long wait) fills the bucket, as it did.
            DateTimeOffset t0 = now;
            var huge = new TokenBucketRateLimiter(1e17, double.MaxValue, () => t0);
            Check.True(huge.TryAcquire(1000));
            t0 += TimeSpan.FromDays(1);
            Check.Equal(1e17, huge.AvailableTokens);
            Check.True(huge.TryAcquire(1));
            Check.True(!double.IsNaN(huge.AvailableTokens) && huge.AvailableTokens <= 1e17);

            // Drained in whole chunks of int.MaxValue: exactly floor(C / M) grants, then the remainder, then nothing.
            var rnd = new Random(206);
            for (int trial = 0; trial < 3; trial++)
            {
                long c = 9007199254740992L + 2L * rnd.Next(1, 1 << 30);           // above 2^53, a whole double
                var l = new TokenBucketRateLimiter(c, 1e-9, () => now);
                long grants = 0;
                while (l.TryAcquire(int.MaxValue)) grants++;
                Check.Equal(c / int.MaxValue, grants, "capacity " + c);
                int rest = (int)(c - grants * int.MaxValue);
                Check.True(l.TryAcquire(rest), "the remainder " + rest + " of " + c);
                Check.True(!l.TryAcquire(1), "a token past the capacity " + c);
            }
        }
    }
}
