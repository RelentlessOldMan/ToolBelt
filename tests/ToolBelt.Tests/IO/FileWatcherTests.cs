using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class FileWatcherTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime At(int ms) => T0.AddMilliseconds(ms);
        private static FileChange C(FileChangeKind k, string p, string? old = null) => new FileChange(k, p, old);
        private static string Show(IEnumerable<FileChange> cs) => string.Join(" | ", cs.Select(c => c.ToString()));

        // ---------- coalescing rules (deterministic) ----------

        public void Coalesce_CreatedThenChangesIsOneCreated()
        {
            var co = new FileChangeCoalescer(ignoreCase: false);
            co.Add(C(FileChangeKind.Created, "a"), At(0));
            co.Add(C(FileChangeKind.Changed, "a"), At(5));
            co.Add(C(FileChangeKind.Changed, "a"), At(9));
            Check.Equal("Created a", Show(co.TakeAll()));
        }

        public void Coalesce_NetEffects()
        {
            string Net(params FileChange[] events)
            {
                var co = new FileChangeCoalescer(ignoreCase: false);
                int t = 0;
                foreach (var e in events) co.Add(e, At(t += 10));
                return Show(co.TakeAll());
            }
            Check.Equal("", Net(C(FileChangeKind.Created, "a"), C(FileChangeKind.Deleted, "a")));                    // came and went
            Check.Equal("Changed a", Net(C(FileChangeKind.Deleted, "a"), C(FileChangeKind.Created, "a")));           // replaced
            Check.Equal("Deleted a", Net(C(FileChangeKind.Changed, "a"), C(FileChangeKind.Deleted, "a")));
            Check.Equal("Renamed a -> b", Net(C(FileChangeKind.Renamed, "b", "a"), C(FileChangeKind.Changed, "b")));
            Check.Equal("Created b", Net(C(FileChangeKind.Created, "a"), C(FileChangeKind.Renamed, "b", "a")));      // new, then renamed
            Check.Equal("Renamed a -> c", Net(C(FileChangeKind.Renamed, "b", "a"), C(FileChangeKind.Renamed, "c", "b")));
            Check.Equal("Changed a", Net(C(FileChangeKind.Renamed, "b", "a"), C(FileChangeKind.Renamed, "a", "b")));  // renamed back
            Check.Equal("Deleted a", Net(C(FileChangeKind.Renamed, "b", "a"), C(FileChangeKind.Deleted, "b")));       // original is gone
            Check.Equal("Changed b", Net(C(FileChangeKind.Deleted, "b"), C(FileChangeKind.Created, "a"), C(FileChangeKind.Renamed, "b", "a"))); // save-via-temp pattern
        }

        public void Coalesce_DebounceAndOrder()
        {
            var co = new FileChangeCoalescer(ignoreCase: false);
            co.Add(C(FileChangeKind.Created, "first"), At(0));
            co.Add(C(FileChangeKind.Created, "second"), At(50));
            Check.Equal(0, co.TakeQuiet(At(200), TimeSpan.FromMilliseconds(250)).Count);   // not quiet yet
            co.Add(C(FileChangeKind.Changed, "first"), At(200));                         // activity resets its quiet period
            Check.Equal("Created second", Show(co.TakeQuiet(At(310), TimeSpan.FromMilliseconds(250))));
            Check.Equal("Created first", Show(co.TakeQuiet(At(460), TimeSpan.FromMilliseconds(250))));
            Check.Equal(0, co.PendingCount);
        }

        public void Coalesce_ReleasedInFirstSeenOrder()
        {
            var co = new FileChangeCoalescer(ignoreCase: false);
            co.Add(C(FileChangeKind.Created, "z"), At(0));
            co.Add(C(FileChangeKind.Created, "a"), At(1));
            co.Add(C(FileChangeKind.Created, "m"), At(2));
            Check.Equal("Created z | Created a | Created m", Show(co.TakeAll()));
        }

        public void Coalesce_NotReadyChangesAreHeldBack()
        {
            var co = new FileChangeCoalescer(ignoreCase: false);
            co.Add(C(FileChangeKind.Created, "big.bin"), At(0));
            bool ready = false;
            Check.Equal(0, co.TakeQuiet(At(300), TimeSpan.FromMilliseconds(100), _ => ready).Count);
            Check.Equal(1, co.PendingCount);
            ready = true;
            Check.Equal(0, co.TakeQuiet(At(350), TimeSpan.FromMilliseconds(100), _ => ready).Count); // quiet period restarted at 300
            Check.Equal("Created big.bin", Show(co.TakeQuiet(At(400), TimeSpan.FromMilliseconds(100), _ => ready)));
        }

        public void Coalesce_CaseInsensitivePaths()
        {
            var co = new FileChangeCoalescer(ignoreCase: true);
            co.Add(C(FileChangeKind.Created, @"C:\Dir\A.txt"), At(0));
            co.Add(C(FileChangeKind.Deleted, @"c:\dir\a.TXT"), At(1));
            Check.Equal(0, co.TakeAll().Count);
        }

        // ---------- snapshot diff ----------

        public void Snapshot_DiffsCreatedChangedDeleted()
        {
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("keep.txt"), "k");
            File.WriteAllText(tmp.Combine("edit.txt"), "1");
            File.WriteAllText(tmp.Combine("gone.txt"), "g");
            DirectorySnapshot before = DirectorySnapshot.Take(tmp.Path);
            File.WriteAllText(tmp.Combine("edit.txt"), "1234");
            File.Delete(tmp.Combine("gone.txt"));
            File.WriteAllText(tmp.Combine("new.txt"), "n");
            var diff = DirectorySnapshot.Diff(before, DirectorySnapshot.Take(tmp.Path))
                .Select(c => c.Kind + " " + Path.GetFileName(c.FullPath)).OrderBy(x => x, StringComparer.Ordinal);
            Check.Equal("Changed edit.txt|Created new.txt|Deleted gone.txt", string.Join("|", diff));
        }

        // ---------- the real watcher ----------

        private sealed class Recorder
        {
            public readonly ConcurrentQueue<FileChange> Changes = new ConcurrentQueue<FileChange>();
            public readonly ConcurrentQueue<Exception> Errors = new ConcurrentQueue<Exception>();
            public readonly ConcurrentQueue<DateTime> BatchTimes = new ConcurrentQueue<DateTime>();
            public int Batches;

            public void Attach(FileWatcher w)
            {
                w.Changed += (_, e) => { Interlocked.Increment(ref Batches); BatchTimes.Enqueue(DateTime.UtcNow); foreach (var c in e.Changes) Changes.Enqueue(c); };
                w.Error += (_, e) => Errors.Enqueue(e.GetException());
            }

            public bool WaitFor(Func<FileChange[], bool> condition, int timeoutMs = 6000)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (condition(Changes.ToArray())) return true;
                    Thread.Sleep(20);
                }
                return condition(Changes.ToArray());
            }

            public FileChange[] For(string name) => Changes.Where(c => Path.GetFileName(c.FullPath) == name).ToArray();
        }

        private static FileWatcher Watch(TempDirectory tmp, Recorder r, FileWatcherOptions? o = null)
        {
            var w = new FileWatcher(tmp.Path, o ?? new FileWatcherOptions { Debounce = TimeSpan.FromMilliseconds(150) });
            r.Attach(w);
            w.Start();
            return w;
        }

        public void Watcher_BurstOfWritesIsOneCreated()
        {
            using var tmp = new TempDirectory();
            var r = new Recorder();
            using var w = Watch(tmp, r);
            string path = tmp.Combine("data.csv");
            for (int i = 0; i < 6; i++) File.AppendAllText(path, "row " + i + "\n");
            Check.True(r.WaitFor(cs => cs.Any(c => c.FullPath.EndsWith("data.csv", StringComparison.Ordinal))), "no change reported");
            Thread.Sleep(500);                                                     // nothing more should trickle in
            Check.Equal("Created", string.Join(",", r.For("data.csv").Select(c => c.Kind)));
            Check.True(r.Errors.IsEmpty);
        }

        public void Watcher_RenameIsOneRenamed()
        {
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("old.txt"), "x");
            var r = new Recorder();
            using var w = Watch(tmp, r);
            File.Move(tmp.Combine("old.txt"), tmp.Combine("new.txt"));
            Check.True(r.WaitFor(cs => cs.Length > 0), "no change reported");
            Thread.Sleep(400);
            FileChange[] all = r.Changes.ToArray();
            Check.Equal(1, all.Length);
            Check.Equal(FileChangeKind.Renamed, all[0].Kind);
            Check.Equal("old.txt", Path.GetFileName(all[0].OldFullPath));
            Check.Equal("new.txt", Path.GetFileName(all[0].FullPath));
        }

        public void Watcher_CreateThenDeleteIsSilent()
        {
            using var tmp = new TempDirectory();
            var r = new Recorder();
            using var w = Watch(tmp, r, new FileWatcherOptions { Debounce = TimeSpan.FromMilliseconds(400) });
            string path = tmp.Combine("blip.tmp");
            File.WriteAllText(path, "x");
            File.Delete(path);
            File.WriteAllText(tmp.Combine("marker.txt"), "m");                  // proves the watcher was live
            Check.True(r.WaitFor(cs => cs.Any(c => c.FullPath.EndsWith("marker.txt", StringComparison.Ordinal))));
            Thread.Sleep(300);
            Check.Equal(0, r.For("blip.tmp").Length);
        }

        public void Watcher_StableForWaitsUntilWritingStops()
        {
            using var tmp = new TempDirectory();
            var r = new Recorder();
            using var w = Watch(tmp, r, new FileWatcherOptions { Debounce = TimeSpan.FromMilliseconds(100), StableFor = TimeSpan.FromMilliseconds(400) });
            string path = tmp.Combine("copying.bin");
            DateTime lastWrite = DateTime.MinValue;
            for (int i = 0; i < 6; i++)                                           // a slow copy: a chunk every 150 ms
            {
                using (var f = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) f.Write(new byte[1000], 0, 1000);
                lastWrite = DateTime.UtcNow;
                Thread.Sleep(150);
            }
            Check.True(r.WaitFor(cs => cs.Any(c => c.FullPath.EndsWith("copying.bin", StringComparison.Ordinal))), "never reported");
            Check.Equal(1, r.For("copying.bin").Length);
            Check.True(r.BatchTimes.First() - lastWrite >= TimeSpan.FromMilliseconds(300), "reported before the file was stable");
        }

        public void Watcher_ChangesWhileStoppedAreFoundOnRestart()
        {
            using var tmp = new TempDirectory();
            var r = new Recorder();
            using var w = Watch(tmp, r);
            w.Stop();
            File.WriteAllText(tmp.Combine("while-stopped.txt"), "x");
            w.Start();
            Check.True(r.WaitFor(cs => cs.Any(c => c.Kind == FileChangeKind.Created && c.FullPath.EndsWith("while-stopped.txt", StringComparison.Ordinal))),
                "missed a change made while stopped");
        }

        public void Watcher_DirectoryMovedInReportsItsFiles()
        {
            using var outside = new TempDirectory();
            Directory.CreateDirectory(Path.Combine(outside.Path, "batch", "inner"));
            File.WriteAllText(Path.Combine(outside.Path, "batch", "one.txt"), "1");
            File.WriteAllText(Path.Combine(outside.Path, "batch", "inner", "two.txt"), "2");
            using var tmp = new TempDirectory();
            var r = new Recorder();
            using var w = Watch(tmp, r);
            Directory.Move(Path.Combine(outside.Path, "batch"), tmp.Combine("batch"));
            Check.True(r.WaitFor(cs => r.For("one.txt").Length == 1 && r.For("two.txt").Length == 1), "files inside a moved-in directory were not reported");
            Check.Equal(FileChangeKind.Created, r.For("two.txt")[0].Kind);
        }

        public void Watcher_ExplicitRescanAndFlush()
        {
            using var tmp = new TempDirectory();
            var r = new Recorder();
            using var w = Watch(tmp, r);
            w.Stop();
            File.WriteAllText(tmp.Combine("x.txt"), "x");
            File.WriteAllText(tmp.Combine("y.txt"), "y");
            Check.Equal(2, w.Rescan());
            IReadOnlyList<FileChange> flushed = w.Flush();
            Check.Equal("x.txt,y.txt", string.Join(",", flushed.Select(c => Path.GetFileName(c.FullPath)).OrderBy(n => n, StringComparer.Ordinal)));
            Check.Equal(0, w.Rescan());                                             // the snapshot now includes them
        }

        public void Watcher_HandlerExceptionsGoToErrorAndWatchingContinues()
        {
            using var tmp = new TempDirectory();
            var r = new Recorder();
            using var w = new FileWatcher(tmp.Path, new FileWatcherOptions { Debounce = TimeSpan.FromMilliseconds(100) });
            bool thrown = false;
            w.Changed += (_, __) => { if (!thrown) { thrown = true; throw new InvalidOperationException("handler bug"); } };
            r.Attach(w);
            w.Start();
            File.WriteAllText(tmp.Combine("one.txt"), "1");
            Check.True(r.WaitFor(_ => !r.Errors.IsEmpty || r.Batches > 0));
            Thread.Sleep(300);
            File.WriteAllText(tmp.Combine("two.txt"), "2");
            Check.True(r.WaitFor(cs => cs.Any(c => c.FullPath.EndsWith("two.txt", StringComparison.Ordinal))), "watching stopped after a handler threw");
            Check.True(r.Errors.Any(e => e.Message == "handler bug"));
        }

        public void Watcher_Validation()
        {
            using var tmp = new TempDirectory();
            Check.Throws<DirectoryNotFoundException>(() => new FileWatcher(tmp.Combine("missing")));
            Check.Throws<ArgumentOutOfRangeException>(() => new FileWatcher(tmp.Path, new FileWatcherOptions { StableFor = TimeSpan.Zero }));
            var w = new FileWatcher(tmp.Path);
            w.Dispose();
            w.Dispose();                                                            // idempotent
            Check.Throws<ObjectDisposedException>(() => w.Start());
            Check.Throws<ArgumentException>(() => new FileChange(FileChangeKind.Renamed, "b"));
        }
    }
}
