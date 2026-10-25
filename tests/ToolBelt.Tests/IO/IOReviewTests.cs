using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>Regressions from review round 8 (IO): each test failed before its fix.</summary>
    public sealed class IOReviewTests
    {
        // A junction needs no special privilege on Windows (unlike a directory symlink).
        private static bool TryLink(string link, string target)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    p.WaitForExit(10000);
                    return Directory.Exists(link);
                }
                Directory.CreateSymbolicLink(link, target);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private static void MakeZip(string path, params (string Name, string Content)[] entries)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                byte[] b = Encoding.UTF8.GetBytes(content);
                s.Write(b, 0, b.Length);
            }
        }

        // ---------------- ZipUtils ----------------

        public void Zip_ExtractDoesNotWriteThroughAJunctionInTheDestination()
        {
            using var tmp = new TempDirectory();
            string dst = tmp.Combine("dst"), outside = tmp.Combine("outside"), zip = tmp.Combine("a.zip");
            Directory.CreateDirectory(dst);
            Directory.CreateDirectory(outside);
            if (!TryLink(Path.Combine(dst, "sub"), outside)) return;
            MakeZip(zip, ("sub/evil.txt", "pwned"));
            Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(zip, dst));
            Check.False(File.Exists(Path.Combine(outside, "evil.txt")), "extraction escaped through the junction");
        }

        public void Zip_CreateDoesNotFollowAJunctionInTheSource()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), outside = tmp.Combine("outside"), zip = tmp.Combine("a.zip");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(src, "keep.txt"), "k");
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "s");
            if (!TryLink(Path.Combine(src, "link"), outside)) return;
            ZipUtils.CreateFromDirectory(src, zip);
            Check.Equal("keep.txt", string.Join("|", ZipUtils.ListEntries(zip)));
        }

        public void Zip_CreateInsideItsOwnSourceSkipsTheArchive()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            string zip = Path.Combine(src, "out.zip");
            ZipUtils.CreateFromDirectory(src, zip);
            Check.Equal("a.txt", string.Join("|", ZipUtils.ListEntries(zip)));
        }

        public void Zip_FailedCreateLeavesNoPartialArchive()
        {
            if (!OperatingSystem.IsWindows()) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), zip = tmp.Combine("a.zip");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            File.WriteAllText(Path.Combine(src, "b.txt"), "b");
            using (new FileStream(Path.Combine(src, "b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check.Throws<IOException>(() => ZipUtils.CreateFromDirectory(src, zip));
            Check.False(File.Exists(zip), "a partial zip was left behind");
        }

        public void Zip_AddOrReplaceRemovesEveryDuplicate()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("a.zip");
            MakeZip(zip, ("x.txt", "1"), ("x.txt", "2"));
            ZipUtils.AddOrReplaceEntry(zip, "x.txt", Encoding.UTF8.GetBytes("3"));
            Check.Equal("x.txt", string.Join("|", ZipUtils.ListEntries(zip)));
            Check.Equal("3", ZipUtils.ReadEntryText(zip, "x.txt"));
        }

        public void Zip_EntryResolvingToTheRootIsRejectedAsAnIOException()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("a.zip"), dst = tmp.Combine("dst");
            MakeZip(zip, ("a/..", "x"));
            Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(zip, dst));
        }

        public void Zip_ReadEntryBytesHonoursASizeCap()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("a.zip");
            MakeZip(zip, ("big.txt", new string('x', 1000)));
            Check.Throws<InvalidDataException>(() => ZipUtils.ReadEntryBytes(zip, "big.txt", maxBytes: 100));
            Check.Equal(1000, ZipUtils.ReadEntryBytes(zip, "big.txt", maxBytes: 1000)!.Length);
        }

        // ---------------- TarUtils ----------------

        public void Tar_ExtractDoesNotWriteThroughAJunctionInTheDestination()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst"), outside = tmp.Combine("outside"), tar = tmp.Combine("a.tar");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "sub", "evil.txt"), "pwned");
            TarUtils.CreateFromDirectory(src, tar);
            Directory.CreateDirectory(dst);
            Directory.CreateDirectory(outside);
            if (!TryLink(Path.Combine(dst, "sub"), outside)) return;
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, dst));
            Check.False(File.Exists(Path.Combine(outside, "evil.txt")), "extraction escaped through the junction");
        }

        public void Tar_KeepsEmptyDirectories()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst"), tar = tmp.Combine("a.tar");
            Directory.CreateDirectory(Path.Combine(src, "empty", "deeper"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            TarUtils.CreateFromDirectory(src, tar);
            TarUtils.ExtractToDirectory(tar, dst);
            Check.True(Directory.Exists(Path.Combine(dst, "empty", "deeper")));
            Check.Equal("a", File.ReadAllText(Path.Combine(dst, "a.txt")));
        }

        // ---------------- LineReader ----------------

        public void LineReader_RejectsEncodingsWhoseLineBreaksAreNotSingleBytes()
        {
            var ms = new MemoryStream(Encoding.Unicode.GetBytes("a\nb"));
            Check.Throws<ArgumentException>(() => LineReader.ReadLines(ms, Encoding.Unicode));
        }

        public void LineReader_StripsTheUtf8ByteOrderMark()
        {
            byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("key=1\nb")).ToArray();
            var lines = LineReader.ReadLines(new MemoryStream(bytes)).ToList();
            Check.Equal("key=1", lines[0].Text);
            Check.Equal(3L, lines[0].Offset);
            Check.Equal(9L, lines[1].Offset);
        }

        // ---------------- DotEnv ----------------

        public void DotEnv_QuotedValueWithInlineComment()
        {
            var d = DotEnv.Parse("A=\"x y\" # note\nB='lit' #c\nC=\"has # hash\"");
            Check.Equal("x y", d["A"]);
            Check.Equal("lit", d["B"]);
            Check.Equal("has # hash", d["C"]);
        }

        public void DotEnv_HashWithoutLeadingSpaceIsPartOfTheValue()
        {
            var d = DotEnv.Parse("URL=http://x/#frag\nP=abc#def # comment\nE=#only");
            Check.Equal("http://x/#frag", d["URL"]);
            Check.Equal("abc#def", d["P"]);
            Check.Equal("", d["E"]);
        }

        public void DotEnv_ByteOrderMarkAndExportTab()
        {
            var d = DotEnv.Parse("﻿FIRST=1\nexport\tSECOND=2");
            Check.Equal("1", d["FIRST"]);
            Check.Equal("2", d["SECOND"]);
        }

        // ---------------- ByteSize ----------------

        public void ByteSize_DecimalCommaIsNotAThousandsSeparator()
        {
            Check.False(ByteSize.TryParse("1,5 KB", out _));
            Check.Equal(1_000_000L, ByteSize.Parse("1,000 KB"));
            Check.Equal(1_234_567L, ByteSize.Parse("1,234,567"));
            Check.False(ByteSize.TryParse("12,34 MB", out _));
        }

        public void ByteSize_FormatCarriesIntoTheNextUnitAfterRounding()
        {
            Check.Equal("1.00 MiB", ByteSize.Format(1048575));
            Check.Equal("1.00 MB", ByteSize.Format(999_999, binary: false));
            Check.Equal("1023 B", ByteSize.Format(1023));
            Check.Equal("1 MiB", ByteSize.Format(1048575, decimals: 0));
        }

        // ---------------- CsvDialect ----------------

        public void CsvDialect_DetectsSemicolonsWithDecimalCommasWithoutAHeader()
        {
            var d = CsvDialect.Detect(new[] { "1,5;2,5", "3,5;4,5" });
            Check.Equal(';', d.Delimiter);
            Check.Equal(',', d.DecimalSeparator);
        }

        public void CsvDialect_IntegersAreNotEvidenceForADecimalComma()
        {
            var d = CsvDialect.Detect(new[] { "id;amount", "1;2.5", "2;3.5", "3;1,000" });
            Check.Equal(';', d.Delimiter);
            Check.Equal('.', d.DecimalSeparator);
        }

        // ---------------- CsvLine / CsvBinder ----------------

        public void CsvLine_UnterminatedQuoteThrows()
        {
            Check.Throws<FormatException>(() => CsvLine.Parse("\"abc,def"));
        }

        public sealed class Rec
        {
            public Guid Id { get; set; }
            public DateTime When { get; set; }
            public TimeSpan Span { get; set; }
            public int? Maybe { get; set; }
            public double X { get; set; }
            public DateTimeOffset At { get; set; }
        }

        public void CsvBinder_RoundTripsGuidDatesTimeSpanAndEmptyNullable()
        {
            var b = new CsvBinder<Rec>(new[] { "Id", "When", "Span", "Maybe", "X", "At" });
            var r = new Rec
            {
                Id = Guid.NewGuid(),
                When = new DateTime(2026, 3, 5, 14, 22, 33, 123, DateTimeKind.Utc),
                Span = TimeSpan.FromMilliseconds(1500),
                Maybe = null,
                X = 0.1 + 0.2,
                At = new DateTimeOffset(2026, 3, 5, 14, 22, 33, 456, TimeSpan.FromHours(2)),
            };
            Rec back = b.Bind(b.ToFields(r));
            Check.Equal(r.Id, back.Id);
            Check.Equal(r.When, back.When);
            Check.Equal(DateTimeKind.Utc, back.When.Kind);
            Check.Equal(r.Span, back.Span);
            Check.Null(back.Maybe);
            Check.Equal(r.X, back.X);
            Check.Equal(r.At, back.At);
            Check.Equal(r.At.Offset, back.At.Offset);
        }

        public void CsvBinder_HeaderNamesNoDependency()
        {
            string src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ToolBelt", "IO", "CsvBinder.cs");
            if (!File.Exists(src)) return;
            string text = File.ReadAllText(src);
            Check.False(text.Contains("using ToolBelt."), "CsvBinder depends on another ToolBelt file but claims to be self-contained");
        }

        // ---------------- AtomicFile / JsonFile ----------------

        public void AtomicFile_WritesTheEncodingsPreamble()
        {
            using var tmp = new TempDirectory();
            string p = tmp.Combine("u16.txt");
            AtomicFile.WriteAllText(p, "héllo", Encoding.Unicode);
            Check.Equal("héllo", File.ReadAllText(p));
            byte[] b = File.ReadAllBytes(p);
            Check.True(b[0] == 0xFF && b[1] == 0xFE);
        }

        public void AtomicFile_LongFileNameStillWorks()
        {
            using var tmp = new TempDirectory();
            string p = tmp.Combine(new string('n', 230) + ".txt");
            AtomicFile.WriteAllText(p, "x");
            Check.Equal("x", File.ReadAllText(p));
            AtomicFile.WriteAllText(p, "y", backup: true);
            Check.Equal("y", File.ReadAllText(p));
        }

        public void JsonFile_LongFileNameStillWorks()
        {
            using var tmp = new TempDirectory();
            string p = tmp.Combine(new string('j', 245) + ".json");
            JsonFile.Save(p, new[] { 1, 2 });
            Check.Equal(2, JsonFile.Load<int[]>(p).Length);
        }

        public void JsonFile_NullForANullableValueType()
        {
            Check.Null(JsonFile.Deserialize<int?>("null"));
            Check.Throws<System.Text.Json.JsonException>(() => JsonFile.Deserialize<int[]>("null"));
        }

        public void JsonFile_NullArgumentsAreCheckedEagerly()
        {
            Check.Throws<ArgumentNullException>(() => JsonLines.Read<int>((TextReader)null!));
            Check.Throws<ArgumentNullException>(() => JsonLines.ReadAsync<int>(null!));
            Check.Throws<ArgumentNullException>(() => JsonFile.TryLoad<int>(null!, out _, out _));
        }

        // ---------------- SafeFileName ----------------

        public void SafeFileName_ReplacesWindowsInvalidCharactersOnEveryPlatform()
        {
            Check.Equal("a_b_c_d_e_f_g_h_i", SafeFileName.MakeSafe("a<b>c:d\"e|f?g*h\\i"));
            Check.Equal("x_y", SafeFileName.MakeSafe("x\u0001y"));
        }

        public void SafeFileName_EscapesMoreReservedNames()
        {
            Check.Equal("_CON .txt", SafeFileName.MakeSafe("CON .txt"));
            Check.Equal("_COM¹", SafeFileName.MakeSafe("COM¹"));
            Check.Equal("_LPT².log", SafeFileName.MakeSafe("LPT².log"));
            Check.Equal("_CONIN$", SafeFileName.MakeSafe("CONIN$"));
            Check.Equal("_conout$.x", SafeFileName.MakeSafe("conout$.x"));
            Check.Equal("_COM0", SafeFileName.MakeSafe("COM0"));
            Check.Equal("COM10", SafeFileName.MakeSafe("COM10"));
        }

        public void SafeFileName_TruncationDoesNotSplitASurrogatePair()
        {
            string s = SafeFileName.MakeSafe("ab\U0001F600cd", maxLength: 3);
            Check.Equal("ab", s);
        }

        // ---------------- FileRetention ----------------

        public void FileRetention_TotalBytesCutoffDeletesEverythingOlder()
        {
            using var tmp = new TempDirectory();
            var now = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            void Make(string name, int size, int daysAgo)
            {
                string p = tmp.Combine(name);
                File.WriteAllBytes(p, new byte[size]);
                File.SetLastWriteTimeUtc(p, now.AddDays(-daysAgo));
            }
            Make("c.log", 40, 1);
            Make("b.log", 100, 2);
            Make("a.log", 10, 3);
            var r = FileRetention.WhatIf(tmp.Path, "*.log", new RetentionPolicy { MaxTotalBytes = 60 }, now);
            Check.Equal("c.log", string.Join("|", r.Kept.Select(Path.GetFileName)));
            Check.Equal("a.log|b.log", string.Join("|", r.Deleted.Select(Path.GetFileName).OrderBy(x => x)));
        }

        // ---------------- FixedWidth / HexDump ----------------

        public void FixedWidth_HugeWidthsDoNotOverflow()
        {
            var f = FixedWidth.Parse("abc", new[] { int.MaxValue, int.MaxValue, 1 });
            Check.Equal("abc||", string.Join("|", f));
        }

        public void HexDump_HugeBytesPerLineDoesNotPadToIt()
        {
            var sw = Stopwatch.StartNew();
            string s = HexDump.Format(new byte[] { 0x41, 0x42 }, int.MaxValue);
            Check.True(s.StartsWith("00000000  41 42 ", StringComparison.Ordinal) && s.EndsWith(" |AB|", StringComparison.Ordinal));
            Check.Equal(10 + 256 * 3 + 4, s.Length);              // padded to 256 byte columns, not 2^31
            Check.True(sw.ElapsedMilliseconds < 2000);
        }

        // ---------------- DirectoryUtils ----------------

        public void DirectoryUtils_RelativePathHasNoTrailingSeparator()
        {
            using var tmp = new TempDirectory();
            Check.Equal("sub", DirectoryUtils.GetRelativePath(tmp.Path, tmp.Combine("sub") + Path.DirectorySeparatorChar));
            Check.Equal("", DirectoryUtils.GetRelativePath(tmp.Path, tmp.Path + Path.DirectorySeparatorChar));
        }

        public void DirectoryUtils_EnsureEmptyOnALinkThrows()
        {
            using var tmp = new TempDirectory();
            string target = tmp.Combine("target"), link = tmp.Combine("link");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "f.txt"), "keep");
            if (!TryLink(link, target)) return;
            Check.Throws<IOException>(() => DirectoryUtils.EnsureEmpty(link));
            Check.True(File.Exists(Path.Combine(target, "f.txt")), "the link target was emptied");
        }

        // ---------------- ChecksumManifest ----------------

        public void ChecksumManifest_UnreadableDirectoryThrowsWithoutAnErrorCallback()
        {
            if (!OperatingSystem.IsWindows()) return;
            using var tmp = new TempDirectory();
            string root = tmp.Combine("root");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "b");
            var sub = new DirectoryInfo(Path.Combine(root, "sub"));
            var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
            DirectorySecurity acl = sub.GetAccessControl();
            acl.AddAccessRule(deny);
            sub.SetAccessControl(acl);
            try
            {
                Check.Throws<UnauthorizedAccessException>(() => ChecksumManifest.Create(root));
                var errors = new List<string>();
                Check.Equal(1, ChecksumManifest.Create(root, onError: (p, _) => errors.Add(p)).Count);
                Check.Equal(1, errors.Count);
            }
            finally
            {
                DirectorySecurity restore = sub.GetAccessControl();
                restore.RemoveAccessRule(deny);
                sub.SetAccessControl(restore);
            }
        }

        // ---------------- StreamUtils / TeeStream ----------------

        public void StreamUtils_PreCancelledCopyReadsNothing()
        {
            var src = new MemoryStream(new byte[] { 1, 2, 3 });
            var dst = new MemoryStream();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Check.Throws<OperationCanceledException>(() => StreamUtils.CopyWithProgress(src, dst, cancellationToken: cts.Token));
            Check.Equal(0L, src.Position);
        }

        private sealed class ThrowOnDispose : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) throw new IOException("dispose failed");
            }
        }

        public void TeeStream_DisposesTheCopyEvenIfThePrimaryThrows()
        {
            var copy = new MemoryStream();
            var tee = new TeeStream(new ThrowOnDispose(), copy);
            Check.Throws<IOException>(() => tee.Dispose());
            Check.False(copy.CanWrite, "the copy stream was not disposed");
        }

        // ---------------- FileWatcher ----------------

        public void Watcher_RescanDoesNotDoubleReportAPendingRename()
        {
            using var tmp = new TempDirectory();
            string a = tmp.Combine("a.txt"), b = tmp.Combine("b.txt");
            File.WriteAllText(a, "x");
            using var w = new FileWatcher(tmp.Path, new FileWatcherOptions { Debounce = TimeSpan.FromSeconds(30) });
            w.Start();
            File.Move(a, b);
            Thread.Sleep(1000);                                   // let the OS deliver the rename
            w.Rescan();
            var changes = w.Flush();
            Check.Equal("Renamed " + a + " -> " + b, string.Join(" | ", changes.Select(c => c.ToString())));
        }

        public void Watcher_DeletedEmptyDirectoryIsNotReported()
        {
            using var tmp = new TempDirectory();
            string dir = tmp.Combine("emptydir");
            Directory.CreateDirectory(dir);
            using var w = new FileWatcher(tmp.Path, new FileWatcherOptions { Debounce = TimeSpan.FromSeconds(30) });
            w.Start();
            Directory.Delete(dir);
            Thread.Sleep(1000);
            var changes = w.Flush();
            Check.Equal("", string.Join(" | ", changes.Select(c => c.ToString())));
        }
    }
}
