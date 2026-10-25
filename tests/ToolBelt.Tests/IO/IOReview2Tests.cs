using System;
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
    /// <summary>Regressions from review round 24 (IO, second pass): each test failed before its fix.</summary>
    public sealed class IOReview2Tests
    {
        // ---------------- TarUtils ----------------

        public void Tar_PaxGlobalHeaderIsSkipped()
        {
            // .NET's TarWriter (and GNU tar) name a global header "$TMPDIR/GlobalHead.%p.%n": an absolute path, which the
            // escape check rejected before the type switch could skip the entry.
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("g.tar"), dst = tmp.Combine("dst");
            using (var fs = File.Create(tar))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax))
            {
                w.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(new Dictionary<string, string> { ["comment"] = "x" }));
                var e = new PaxTarEntry(TarEntryType.RegularFile, "a.txt") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("hi")) };
                w.WriteEntry(e);
            }
            TarUtils.ExtractToDirectory(tar, dst);
            Check.Equal("hi", File.ReadAllText(Path.Combine(dst, "a.txt")));
            Check.Equal("a.txt", string.Join("|", TarUtils.ListEntries(tar)));
        }

        public void Tar_FileEntryResolvingToTheRootIsInvalidData()
        {
            // "x/.." is the destination itself: opening it as a file threw UnauthorizedAccessException.
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("r.tar"), dst = tmp.Combine("dst");
            using (var fs = File.Create(tar))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax))
                w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "x/..") { DataStream = new MemoryStream(new byte[] { 1 }) });
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, dst));
        }

        // ---------------- ZipUtils ----------------

        public void Zip_ReadEntryBytesDoesNotTrustTheDeclaredSize()
        {
            // A 10-byte entry whose headers claim 256 MB: the buffer was pre-sized from the claim.
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("lie.zip");
            using (var fs = new FileStream(zip, FileMode.Create))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            using (var s = z.CreateEntry("a").Open())
                s.Write(new byte[10], 0, 10);
            byte[] bytes = File.ReadAllBytes(zip);
            int patched = 0;
            for (int i = 0; i + 4 <= bytes.Length; i++)
            {
                uint sig = BitConverter.ToUInt32(bytes, i);
                if (sig == 0x04034b50) { BitConverter.GetBytes(0x10000000u).CopyTo(bytes, i + 22); patched++; }
                else if (sig == 0x02014b50) { BitConverter.GetBytes(0x10000000u).CopyTo(bytes, i + 24); patched++; }
            }
            Check.Equal(2, patched);
            File.WriteAllBytes(zip, bytes);

            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[]? data;
            try { data = ZipUtils.ReadEntryBytes(zip, "a"); }
            catch (InvalidDataException) { data = null; }               // refusing the lying entry is fine too
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 16 * 1024 * 1024, $"allocated {allocated:N0} bytes for a 10-byte entry");
            if (data != null) Check.Equal(10, data.Length);
        }

        // ---------------- FileWatcher ----------------

        public void Watcher_FilteredWatcherStillRescansDirectoryMoves()
        {
            // The OS watcher applied "*.csv" to directory names too, so a directory moved in or out never triggered the rescan.
            using var tmp = new TempDirectory();
            string watched = tmp.Combine("w"), outside = tmp.Combine("out");
            Directory.CreateDirectory(Path.Combine(watched, "old"));
            Directory.CreateDirectory(Path.Combine(outside, "incoming"));
            File.WriteAllText(Path.Combine(watched, "old", "b.csv"), "b");
            File.WriteAllText(Path.Combine(outside, "incoming", "a.csv"), "a");

            var seen = new List<string>();
            using var w = new FileWatcher(watched, new FileWatcherOptions { Filter = "*.csv", Debounce = TimeSpan.FromMilliseconds(100) });
            w.Changed += (_, e) => { lock (seen) seen.AddRange(e.Changes.Select(c => c.Kind + " " + Path.GetFileName(c.FullPath))); };
            w.Start();
            Directory.Move(Path.Combine(outside, "incoming"), Path.Combine(watched, "incoming"));
            Directory.Move(Path.Combine(watched, "old"), Path.Combine(outside, "old"));
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(100);
                lock (seen) if (seen.Count >= 2) break;
            }
            Thread.Sleep(300);
            lock (seen) Check.Equal("Created a.csv|Deleted b.csv", string.Join("|", seen.OrderBy(x => x)));
        }

        public void Coalescer_RenameOntoAPendingRenameReportsTheEarlierSourceDeleted()
        {
            // c → b, then a → b (overwriting): c is gone and a is now b. The deletion of c was dropped.
            var co = new FileChangeCoalescer(ignoreCase: false);
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            co.Add(new FileChange(FileChangeKind.Renamed, "/d/b", "/d/c"), t);
            co.Add(new FileChange(FileChangeKind.Renamed, "/d/b", "/d/a"), t);
            Check.Equal("Deleted /d/c|Renamed /d/a -> /d/b",
                string.Join("|", co.TakeAll().Select(c => c.ToString()).OrderBy(x => x, StringComparer.Ordinal)));
        }

        // ---------------- Streams ----------------

        private sealed class ThrowOnFlush : MemoryStream
        {
            public override void Flush() => throw new IOException("flush failed");
        }

        public void TeeStream_DisposesThePrimaryEvenIfTheCopyFlushThrows()
        {
            var primary = new MemoryStream();
            var tee = new TeeStream(primary, new ThrowOnFlush());
            try { tee.Dispose(); } catch (IOException) { }
            Check.False(primary.CanWrite, "the primary stream was not disposed");
        }

        // ---------------- FileRetention ----------------

        public void FileRetention_StarDotStarMatchesFilesWithoutAnExtension()
        {
            // The BCL treats "*.*" as "*"; the exact re-check then dropped README, so it was neither kept nor deleted.
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("a.log"), "a");
            File.WriteAllText(tmp.Combine("README"), "r");
            var r = FileRetention.WhatIf(tmp.Path, "*.*", new RetentionPolicy { MaxCount = 0 }, DateTime.UtcNow);
            Check.Equal("README|a.log", string.Join("|", r.Deleted.Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal)));
            var dots = FileRetention.WhatIf(tmp.Path, "*.", new RetentionPolicy { MaxCount = 0 }, DateTime.UtcNow);
            Check.Equal("README", string.Join("|", dots.Deleted.Select(Path.GetFileName)));
        }

        // ---------------- ByteSize ----------------

        public void ByteSize_ParsesLargeByteCountsExactly()
        {
            // Parsed through a double: 2^53 + 1 came back as 2^53, and long.MaxValue was rejected.
            Check.True(ByteSize.TryParse("9223372036854775807", out long max), "long.MaxValue");
            Check.Equal(long.MaxValue, max);
            Check.True(ByteSize.TryParse("9007199254740993", out long odd), "2^53 + 1");
            Check.Equal(9007199254740993L, odd);
            Check.True(ByteSize.TryParse("7 EiB", out long eib), "7 EiB");
            Check.Equal(7L << 60, eib);
            Check.False(ByteSize.TryParse("8 EiB", out _), "8 EiB overflows");
            Check.True(ByteSize.TryParse("1.5 KiB", out long k), "1.5 KiB");
            Check.Equal(1536L, k);
        }

        // ---------------- LineReader ----------------

        public void LineReader_OffsetsAreFromTheStartOfTheStream()
        {
            // A stream already past a header gave offsets relative to where reading began, not positions in the stream.
            var ms = new MemoryStream(Encoding.UTF8.GetBytes("hdr\nline1\nline2\n")) { Position = 4 };
            Check.Equal("@4: line1|@10: line2", string.Join("|", LineReader.ReadLines(ms).Select(l => l.ToString())));
        }

        // ---------------- robustness: links ----------------

        private static bool Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }

        // A junction needs no special privilege on Windows (unlike a directory symlink).
        private static bool TryJunction(string link, string target)
        {
            try
            {
                if (OperatingSystem.IsWindows()) return Run("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") && Directory.Exists(link);
                Directory.CreateSymbolicLink(link, target);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static bool TryHardLink(string link, string target)
        {
            try
            {
                return (OperatingSystem.IsWindows()
                    ? Run("cmd.exe", $"/c mklink /H \"{link}\" \"{target}\"")
                    : Run("ln", $"\"{target}\" \"{link}\"")) && File.Exists(link);
            }
            catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        // Runs body on a background thread; false if it is still running after the timeout (it is abandoned).
        private static bool Finishes(Action body, int milliseconds, out Exception? error)
        {
            Exception? caught = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { caught = ex; } }) { IsBackground = true };
            t.Start();
            bool done = t.Join(milliseconds);
            error = caught;
            return done;
        }

        public void Copy_IntoTheSourceThroughAJunctionIsRefusedNotEndless()
        {
            // Copy(src, j\c) with j → src: the string check passed and the copy nested c\c\c… forever.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), j = tmp.Combine("j");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            if (!TryJunction(j, src)) return;
            try
            {
                bool done = Finishes(() => DirectoryUtils.Copy(src, Path.Combine(j, "c")), 15000, out Exception? error);
                Check.True(done, "Copy into its own source through a junction never finished");
                Check.True(error is ArgumentException, $"expected ArgumentException, got {error?.GetType().Name ?? "no exception"}");
            }
            finally { Directory.Delete(j); }
        }

        public void Extract_ReplacesAHardLinkedFileInsteadOfWritingThroughIt()
        {
            using var tmp = new TempDirectory();
            string outside = tmp.Combine("outside.txt"), src = tmp.Combine("src");
            File.WriteAllText(outside, "safe");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "x.txt"), "archive");
            string tar = tmp.Combine("a.tar"), zip = tmp.Combine("a.zip");
            TarUtils.CreateFromDirectory(src, tar);
            ZipUtils.CreateFromDirectory(src, zip);

            string d1 = tmp.Combine("d1"), d2 = tmp.Combine("d2"), d3 = tmp.Combine("d3");
            foreach (string d in new[] { d1, d2, d3 }) Directory.CreateDirectory(d);
            if (!TryHardLink(Path.Combine(d1, "x.txt"), outside)) return;
            TryHardLink(Path.Combine(d2, "x.txt"), outside);
            TryHardLink(Path.Combine(d3, "x.txt"), outside);

            TarUtils.ExtractToDirectory(tar, d1, overwrite: true);
            Check.Equal("safe", File.ReadAllText(outside), "tar wrote through the hard link");
            Check.Equal("archive", File.ReadAllText(Path.Combine(d1, "x.txt")));
            ZipUtils.ExtractToDirectory(zip, d2, overwrite: true);
            Check.Equal("safe", File.ReadAllText(outside), "zip wrote through the hard link");
            Check.Equal("archive", File.ReadAllText(Path.Combine(d2, "x.txt")));
            DirectoryUtils.Copy(src, d3, new CopyOptions { Overwrite = OverwritePolicy.Overwrite });
            Check.Equal("safe", File.ReadAllText(outside), "copy wrote through the hard link");
            Check.Equal("archive", File.ReadAllText(Path.Combine(d3, "x.txt")));
        }

        public void Tar_TruncatedArchiveLeavesTheExistingFileIntact()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst"), tar = tmp.Combine("a.tar");
            Directory.CreateDirectory(src);
            File.WriteAllBytes(Path.Combine(src, "big.bin"), new byte[100_000]);
            TarUtils.CreateFromDirectory(src, tar);
            byte[] whole = File.ReadAllBytes(tar);
            File.WriteAllBytes(tar, whole.Take(50_000).ToArray());
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(dst, "big.bin"), "original");
            Check.Throws<Exception>(() => TarUtils.ExtractToDirectory(tar, dst, overwrite: true));
            Check.Equal("original", File.ReadAllText(Path.Combine(dst, "big.bin")));
            Check.Equal(1, Directory.GetFiles(dst).Length, "a temporary file was left behind");
        }

        // ---------------- robustness: entry names ----------------

        public void Zip_EntryNamesWithAStreamOrNulAreRefused()
        {
            using var tmp = new TempDirectory();
            string dst = tmp.Combine("dst");
            void Write(string zip, string name)
            {
                using var fs = new FileStream(zip, FileMode.Create);
                using var z = new ZipArchive(fs, ZipArchiveMode.Create);
                using var s = z.CreateEntry(name).Open();
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
            }
            if (OperatingSystem.IsWindows())
            {
                Write(tmp.Combine("ads.zip"), "x.txt:hidden");
                Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(tmp.Combine("ads.zip"), dst));
                Check.False(File.Exists(Path.Combine(dst, "x.txt")), "an alternate data stream was written");
            }
            Write(tmp.Combine("nul.zip"), "a\0b.txt");
            Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(tmp.Combine("nul.zip"), dst));
        }

        public void Tar_EntryNamesWithAStreamOrOnlyDotsAreInvalidData()
        {
            using var tmp = new TempDirectory();
            string dst = tmp.Combine("dst");
            void Write(string tar, string name)
            {
                using var fs = File.Create(tar);
                using var w = new TarWriter(fs, TarEntryFormat.Pax);
                w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(new byte[] { 1 }) });
            }
            Write(tmp.Combine("dots.tar"), "...");
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tmp.Combine("dots.tar"), dst));
            if (OperatingSystem.IsWindows())
            {
                Write(tmp.Combine("ads.tar"), "x.txt:hidden");
                Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tmp.Combine("ads.tar"), dst));
                Check.False(File.Exists(Path.Combine(dst, "x.txt")), "an alternate data stream was written");
            }
        }

        // ---------------- robustness: everything else ----------------

        public void TempFileAndDirectory_DeleteReadOnlyContents()
        {
            string dirPath;
            using (var dir = new TempDirectory())
            {
                dirPath = dir.Path;
                Directory.CreateDirectory(dir.Combine("sub"));
                string ro = dir.Combine(Path.Combine("sub", "ro.txt"));
                File.WriteAllText(ro, "x");
                File.SetAttributes(ro, FileAttributes.ReadOnly);
            }
            Check.False(Directory.Exists(dirPath), "TempDirectory left a read-only file behind");

            string filePath;
            using (var f = new TempFile())
            {
                filePath = f.Path;
                File.WriteAllText(filePath, "x");
                File.SetAttributes(filePath, FileAttributes.ReadOnly);
            }
            Check.False(File.Exists(filePath), "TempFile left its read-only file behind");
        }

        public void DotEnv_UnknownEscapeKeepsItsBackslash()
        {
            var env = DotEnv.Parse("P=\"C:\\Users\\bob\\data\"\nQ=\"a\\tb\\\\c\"");
            Check.Equal(@"C:\Users\bob\data", env["P"]);
            Check.Equal("a\tb\\c", env["Q"]);
        }

        public struct PointRecord
        {
            public int A { get; set; }
            public string B { get; set; }
        }

        public void CsvBinder_BindsAStruct()
        {
            var binder = new CsvBinder<PointRecord>(new[] { "A", "B" });
            PointRecord r = binder.Bind(new[] { "1", "x" });
            Check.Equal(1, r.A);
            Check.Equal("x", r.B);
        }

        public void FileRetention_ShiftNumberedDoesNotProbeEveryNumberUpToKeep()
        {
            using var tmp = new TempDirectory();
            string log = tmp.Combine("app.log");
            File.WriteAllText(log, "now");
            File.WriteAllText(log + ".1", "older");
            bool done = Finishes(() => FileRetention.ShiftNumbered(log, int.MaxValue), 5000, out Exception? error);
            Check.True(done, "ShiftNumbered with a huge keep did not finish");
            Check.True(error is null, error?.ToString() ?? "");
            Check.Equal("now", File.ReadAllText(log + ".1"));
            Check.Equal("older", File.ReadAllText(log + ".2"));
        }

        public void CsvLine_RejectsADelimiterThatCannotRoundTrip()
        {
            Check.Throws<ArgumentException>(() => CsvLine.Format(new[] { "a\"b", "c" }, '"'));
            Check.Throws<ArgumentException>(() => CsvLine.Parse("a\nb", '\n'));
        }

        public void JsonFile_SaveKeepsTheTargetsHiddenAttribute()
        {
            if (!OperatingSystem.IsWindows()) return;
            using var tmp = new TempDirectory();
            string p = tmp.Combine("settings.json");
            JsonFile.Save(p, new { A = 1 });
            File.SetAttributes(p, FileAttributes.Hidden);
            JsonFile.Save(p, new { A = 2 });
            Check.True((File.GetAttributes(p) & FileAttributes.Hidden) != 0, "the Hidden attribute was dropped");
        }
    }
}
