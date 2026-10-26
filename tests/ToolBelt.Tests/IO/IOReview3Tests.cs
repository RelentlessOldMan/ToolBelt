using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>Regressions from review round 40 (IO, third pass): each test failed before its fix.</summary>
    public sealed class IOReview3Tests
    {
        private static readonly DateTime Y2001 = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        // ---------------- DirectoryUtils ----------------

        public void Copy_FailedCopyKeepsTheExistingFile()
        {
            // The destination was deleted before File.Copy, so a copy that then failed (here: the source is locked) lost it.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(src, "a.txt"), "new");
            File.WriteAllText(Path.Combine(dst, "a.txt"), "old");
            var errors = new List<string>();
            using (new FileStream(Path.Combine(src, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = DirectoryUtils.Copy(src, dst, new CopyOptions { OnError = (p, _) => errors.Add(p) });
                Check.Equal(1, r.Errors);
            }
            Check.Equal("old", File.ReadAllText(Path.Combine(dst, "a.txt")));
            Check.Equal(1, Directory.GetFiles(dst).Length, "a temp file was left behind");
        }

        public void Copy_WithoutPreserveTimestampsTheCopyIsNew()
        {
            // File.Copy keeps the source's last-write time on Windows, so PreserveTimestamps = false changed nothing.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "x");
            File.SetLastWriteTimeUtc(Path.Combine(src, "a.txt"), Y2001);
            DirectoryUtils.Copy(src, dst, new CopyOptions { PreserveTimestamps = false });
            Check.True(File.GetLastWriteTimeUtc(Path.Combine(dst, "a.txt")) > DateTime.UtcNow.AddHours(-1), "kept the source's time");
            DirectoryUtils.Copy(src, tmp.Combine("dst2"));
            Check.Equal(Y2001, File.GetLastWriteTimeUtc(Path.Combine(tmp.Combine("dst2"), "a.txt")));
        }

        public void EnsureEmpty_NegativeRetriesAreRejected()
        {
            using var tmp = new TempDirectory();
            var e = Check.Throws<ArgumentOutOfRangeException>(() => DirectoryUtils.EnsureEmpty(tmp.Combine("d"), retries: -1));
            Check.Equal("retries", e.ParamName);
        }

        public void SearchPatternWithADirectoryPartIsRejected()
        {
            // "..\*" made Directory.GetFiles list the parent: a walk of the root returned files outside it.
            using var tmp = new TempDirectory();
            string root = tmp.Combine("root");
            Directory.CreateDirectory(root);
            File.WriteAllText(tmp.Combine("outside.txt"), "x");
            Check.Throws<ArgumentException>(() => DirectoryUtils.EnumerateFiles(root, "..\\*"));
            Check.Throws<ArgumentException>(() => DirectoryUtils.EnumerateFiles(root, "../*"));
            Check.Throws<ArgumentException>(() => DirectorySnapshot.Take(root, "..\\*", recursive: false));
            Check.Throws<ArgumentException>(() => new FileWatcher(root, new FileWatcherOptions { Filter = "..\\*" }));
            Check.Equal(0, DirectoryUtils.EnumerateFiles(root, "*.txt").Count());
        }

        // ---------------- ChecksumManifest ----------------

        public void Checksum_VerifyAndCompareTakeTheCreateFilter()
        {
            // A manifest made without *.log could not be verified: Verify re-hashed everything and reported the logs as added.
            using var tmp = new TempDirectory();
            string a = tmp.Combine("a"), b = tmp.Combine("b");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(a, "data.txt"), "x");
            File.WriteAllText(Path.Combine(b, "data.txt"), "x");
            File.WriteAllText(Path.Combine(a, "run.log"), "1");
            Func<string, bool> noLogs = p => !p.EndsWith(".log", StringComparison.Ordinal);
            ChecksumManifest m = ChecksumManifest.Create(a, noLogs);
            ManifestComparison v = m.Verify(a, fileFilter: noLogs);
            Check.Equal(0, v.Added.Count);
            Check.Equal(1, v.Matching.Count);
            Check.Equal(0, ChecksumManifest.CompareDirectories(a, b, noLogs).Added.Count + ChecksumManifest.CompareDirectories(a, b, noLogs).Missing.Count);
        }

        // ---------------- TarUtils ----------------

        // A ustar header block with a valid checksum.
        private static byte[] TarHeader(string name, char type, long size)
        {
            var h = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(h, 0);
            Encoding.ASCII.GetBytes("0000644\0").CopyTo(h, 100);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 108);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 116);
            Encoding.ASCII.GetBytes(Convert.ToString(size, 8).PadLeft(11, '0') + "\0").CopyTo(h, 124);
            Encoding.ASCII.GetBytes("00000000000\0").CopyTo(h, 136);
            h[156] = (byte)type;
            Encoding.ASCII.GetBytes("ustar\0").CopyTo(h, 257);
            Encoding.ASCII.GetBytes("00").CopyTo(h, 263);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            int sum = h.Sum(b => b);
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
            return h;
        }

        public void Tar_HugeMetadataEntryIsRejectedBeforeAllocating()
        {
            // TarReader allocated the declared size of a PAX header (here 1.5 GB) before reading it: a 1.5 KB archive could
            // exhaust memory.
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("bomb.tar");
            File.WriteAllBytes(tar, TarHeader("PaxHeaders/x", 'x', 1_500_000_000).Concat(new byte[1024]).ToArray());
            long before = GC.GetAllocatedBytesForCurrentThread();
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, tmp.Combine("dst")));
            Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 50_000_000, $"allocated {allocated} bytes");

            // A legitimate long name (GNU 'L') and PAX header still work through the guard.
            string ok = tmp.Combine("ok.tar");
            string longName = new string('n', 300) + ".txt";
            using (var fs = File.Create(ok))
            using (var w = new TarWriter(fs, TarEntryFormat.Gnu))
            {
                w.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, longName) { DataStream = new MemoryStream(new byte[700]) });
                w.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, "b.txt") { DataStream = new MemoryStream(Encoding.ASCII.GetBytes("hi")) });
            }
            Check.Equal(longName + "|b.txt", string.Join("|", TarUtils.ListEntries(ok)));
            string pax = tmp.Combine("pax.tar");
            using (var fs = File.Create(pax))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax))
            {
                var e = new PaxTarEntry(TarEntryType.RegularFile, "p.txt", new Dictionary<string, string> { ["comment"] = new string('c', 5000) })
                { DataStream = new MemoryStream(new byte[1300]) };
                w.WriteEntry(e);
                w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "q.txt") { DataStream = new MemoryStream(Encoding.ASCII.GetBytes("q")) });
            }
            TarUtils.ExtractToDirectory(pax, tmp.Combine("pax"));
            Check.Equal(1300L, new FileInfo(Path.Combine(tmp.Combine("pax"), "p.txt")).Length);
            Check.Equal("q", File.ReadAllText(Path.Combine(tmp.Combine("pax"), "q.txt")));
        }

        public void Tar_TruncatedHeaderIsInvalidData()
        {
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("t.tar");
            using (var fs = File.Create(tar))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax))
            {
                w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "a.txt") { DataStream = new MemoryStream(new byte[10]) });
                w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "b.txt") { DataStream = new MemoryStream(new byte[10]) });
            }
            byte[] all = File.ReadAllBytes(tar);
            // The first entry (a PAX header, its data, the file header, its data) is 4 blocks; cut 100 bytes into the next.
            File.WriteAllBytes(tar, all.Take(4 * 512 + 100).ToArray());
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, tmp.Combine("dst")));
            Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
        }

        public void Tar_ReservedDeviceNamesAreRejected()
        {
            if (Path.DirectorySeparatorChar != '\\') return;
            using var tmp = new TempDirectory();
            foreach (string name in new[] { "CON", "aux.txt", "x/COM1.log", "nul", "lpt9" })
            {
                string tar = tmp.Combine("d.tar");
                using (var fs = File.Create(tar))
                using (var w = new TarWriter(fs, TarEntryFormat.Pax))
                    w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(new byte[] { 1 }) });
                var e = Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, tmp.Combine("dst"), overwrite: true), name);
                Check.True(e.Message.Contains("device name"), name + ": " + e.Message);
            }
            Check.False(File.Exists(Path.Combine(tmp.Combine("dst"), "x", "COM1.log")), "COM1.log written");
        }

        public void Tar_FailedCreateKeepsTheExistingArchive()
        {
            // FileMode.Create truncated the old archive before the run, and a failure then deleted it.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), tar = tmp.Combine("a.tar");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            File.WriteAllText(Path.Combine(src, "b.txt"), "b");
            TarUtils.CreateFromDirectory(src, tar);
            byte[] good = File.ReadAllBytes(tar);
            Check.Throws<InvalidOperationException>(() => TarUtils.CreateFromDirectory(src, tar, filter: n => throw new InvalidOperationException("boom")));
            Check.True(File.Exists(tar) && File.ReadAllBytes(tar).SequenceEqual(good), "the existing archive was lost");
            Check.Equal(1, Directory.GetFiles(tmp.Path).Length, "a temp file was left behind");
        }

        public void Tar_ExtractRestoresModificationTimes()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), tar = tmp.Combine("t.tar"), dst = tmp.Combine("dst");
            Directory.CreateDirectory(Path.Combine(src, "d"));
            Directory.CreateDirectory(Path.Combine(src, "empty"));
            File.WriteAllText(Path.Combine(src, "d", "a.txt"), "a");
            File.SetLastWriteTimeUtc(Path.Combine(src, "d", "a.txt"), Y2001);
            Directory.SetLastWriteTimeUtc(Path.Combine(src, "empty"), Y2001);
            TarUtils.CreateFromDirectory(src, tar);
            TarUtils.ExtractToDirectory(tar, dst);
            Check.Equal(Y2001, File.GetLastWriteTimeUtc(Path.Combine(dst, "d", "a.txt")));
            Check.Equal(Y2001, Directory.GetLastWriteTimeUtc(Path.Combine(dst, "empty")));
        }

        // ---------------- ZipUtils ----------------

        public void Zip_FailedCreateKeepsTheExistingArchive()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), zip = tmp.Combine("a.zip");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            ZipUtils.CreateFromDirectory(src, zip);
            byte[] good = File.ReadAllBytes(zip);
            Check.Throws<InvalidOperationException>(() => ZipUtils.CreateFromDirectory(src, zip, filter: n => throw new InvalidOperationException("boom")));
            Check.True(File.Exists(zip) && File.ReadAllBytes(zip).SequenceEqual(good), "the existing archive was lost");
            Check.Equal(1, Directory.GetFiles(tmp.Path).Length, "a temp file was left behind");
        }

        public void Zip_KeepsEmptyDirectories()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), zip = tmp.Combine("a.zip"), dst = tmp.Combine("dst");
            Directory.CreateDirectory(Path.Combine(src, "empty", "deeper"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            ZipUtils.CreateFromDirectory(src, zip);
            Check.Equal("a.txt|empty/deeper/", string.Join("|", ZipUtils.ListEntries(zip).OrderBy(n => n, StringComparer.Ordinal)));
            ZipUtils.ExtractToDirectory(zip, dst);
            Check.True(Directory.Exists(Path.Combine(dst, "empty", "deeper")), "empty directory not restored");
        }

        public void Zip_KeepsLastWriteTimes()
        {
            // CreateEntry stamps the current time, and extraction did not set any time.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), zip = tmp.Combine("a.zip"), dst = tmp.Combine("dst");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            File.SetLastWriteTimeUtc(Path.Combine(src, "a.txt"), Y2001);
            ZipUtils.CreateFromDirectory(src, zip);
            using (var z = ZipFile.OpenRead(zip))
                Check.True(Math.Abs((z.GetEntry("a.txt")!.LastWriteTime.UtcDateTime - Y2001).TotalSeconds) <= 2, "entry time");
            ZipUtils.ExtractToDirectory(zip, dst);
            Check.True(Math.Abs((File.GetLastWriteTimeUtc(Path.Combine(dst, "a.txt")) - Y2001).TotalSeconds) <= 2, "extracted time");
        }

        public void Zip_ReadEntryTextDropsTheByteOrderMark()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("a.zip");
            ZipUtils.AddOrReplaceEntry(zip, "bom.txt", new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' });
            ZipUtils.AddOrReplaceEntry(zip, "u16.txt", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hé")).ToArray());
            Check.Equal("hi", ZipUtils.ReadEntryText(zip, "bom.txt"));
            Check.Equal("hé", ZipUtils.ReadEntryText(zip, "u16.txt"));
        }

        public void Zip_ReservedDeviceNamesAreRejected()
        {
            if (Path.DirectorySeparatorChar != '\\') return;
            using var tmp = new TempDirectory();
            foreach (string name in new[] { "CON", "aux.txt", "x/COM1.log", "nul" })
            {
                string zip = tmp.Combine(Guid.NewGuid().ToString("N") + ".zip");
                ZipUtils.AddOrReplaceEntry(zip, name, new byte[] { 1 });
                var e = Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(zip, tmp.Combine("dst"), overwrite: true), name);
                Check.True(e.Message.Contains("device name"), name + ": " + e.Message);
            }
            Check.False(File.Exists(Path.Combine(tmp.Combine("dst"), "x", "COM1.log")), "COM1.log written");
        }

        // ---------------- FileChangeCoalescer ----------------

        public void Coalescer_DeletingARenamedFileKeepsTheFileNowAtItsOldName()
        {
            // a→b, c→a, delete b: the Deleted reported for a (b's original name) overwrote the pending c→a, so c's
            // disappearance and a's new content were lost, and a — which exists — was reported deleted.
            var t = new DateTime(2026, 1, 1);
            var co = new FileChangeCoalescer(ignoreCase: false);
            co.Add(new FileChange(FileChangeKind.Renamed, "/w/b", "/w/a"), t);
            co.Add(new FileChange(FileChangeKind.Renamed, "/w/a", "/w/c"), t);
            co.Add(new FileChange(FileChangeKind.Deleted, "/w/b"), t);
            FileChange only = co.TakeAll().Single();
            Check.Equal(FileChangeKind.Renamed, only.Kind);
            Check.Equal("/w/a", only.FullPath);
            Check.Equal("/w/c", only.OldFullPath);
        }

        public void Coalescer_RenamingOntoARenamedFileKeepsTheFileNowAtItsOldName()
        {
            // a→b, c→a, d→b: as above, through the overwrite in OnRenamed.
            var t = new DateTime(2026, 1, 1);
            var co = new FileChangeCoalescer(ignoreCase: false);
            co.Add(new FileChange(FileChangeKind.Renamed, "/w/b", "/w/a"), t);
            co.Add(new FileChange(FileChangeKind.Renamed, "/w/a", "/w/c"), t);
            co.Add(new FileChange(FileChangeKind.Renamed, "/w/b", "/w/d"), t);
            var all = co.TakeAll().OrderBy(c => c.FullPath, StringComparer.Ordinal).ToArray();
            Check.Equal(2, all.Length);
            Check.True(all[0].Kind == FileChangeKind.Renamed && all[0].FullPath == "/w/a" && all[0].OldFullPath == "/w/c", "a ← c");
            Check.True(all[1].Kind == FileChangeKind.Renamed && all[1].FullPath == "/w/b" && all[1].OldFullPath == "/w/d", "b ← d");
        }

        // ---------------- FileWatcher ----------------

        public void Watcher_QuestionMarkFilterAgreesWithTheSnapshot()
        {
            // Directory.GetFiles reads "a?.txt" as matching a.txt too, the event filter did not: every rescan reported a.txt.
            using var tmp = new TempDirectory();
            using var w = new FileWatcher(tmp.Path, new FileWatcherOptions { Filter = "a?.txt" });
            w.Start();
            w.Stop();
            File.WriteAllText(tmp.Combine("a.txt"), "x");
            File.WriteAllText(tmp.Combine("ab.txt"), "x");
            Check.Equal(1, w.Rescan());
            Check.Equal("ab.txt", Path.GetFileName(w.Flush().Single().FullPath));
            Check.Equal(0, w.Rescan());
        }

        public void Watcher_OptionsChangedAfterConstructionAreIgnored()
        {
            // The watcher read the caller's options live: Filter = null after construction threw (from Start, or on the OS
            // watcher's thread, which ended the process).
            using var tmp = new TempDirectory();
            var options = new FileWatcherOptions { Filter = "*.txt", Debounce = TimeSpan.FromMilliseconds(10) };
            using var w = new FileWatcher(tmp.Path, options);
            var changes = new ConcurrentQueue<FileChange>();
            w.Changed += (_, e) => { foreach (var c in e.Changes) changes.Enqueue(c); };
            options.Filter = null!;
            w.Start();
            File.WriteAllText(tmp.Combine("a.txt"), "x");
            File.WriteAllText(tmp.Combine("b.log"), "x");
            var sw = Stopwatch.StartNew();
            while (changes.IsEmpty && sw.ElapsedMilliseconds < 6000) Thread.Sleep(20);
            Thread.Sleep(100);
            w.Flush();
            Check.Equal("a.txt", string.Join("|", changes.Select(c => Path.GetFileName(c.FullPath)).Distinct()));
        }

        public void Watcher_DisposeWhileAHandlerOutlastsTheWait()
        {
            // Dispose waits 5 s for an in-flight tick, then disposed the wait handle the timer signals when the tick ends:
            // the signal threw on the timer thread and ended the process.
            using var tmp = new TempDirectory();
            var w = new FileWatcher(tmp.Path, new FileWatcherOptions { Debounce = TimeSpan.FromMilliseconds(10) });
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            w.Changed += (_, __) => { entered.Set(); release.Wait(20000); };
            w.Start();
            File.WriteAllText(tmp.Combine("a.txt"), "x");
            Check.True(entered.Wait(10000), "handler never ran");
            var sw = Stopwatch.StartNew();
            w.Dispose();
            Check.True(sw.Elapsed >= TimeSpan.FromSeconds(4), "did not wait for the tick");
            release.Set();
            Thread.Sleep(500);   // the tick ends and the timer signals its handle
        }
    }
}
