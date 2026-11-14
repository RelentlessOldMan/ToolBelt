using System;
using System.Collections.Generic;
using System.Globalization;
#if !NETFRAMEWORK
using System.Formats.Tar;
using System.IO.Enumeration;
#endif
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>
    /// Regressions from review round 75 (IO, fifth pass): each test failed before its fix (the ByteSize, DosMatch and
    /// IsLink ones on .NET Framework, where they broke; the tar backslash one on Unix only).
    /// </summary>
    public sealed class IOReview75Tests
    {
        // ---------------- CsvBinder ----------------

        public sealed class Nums { public double D { get; set; } public float F { get; set; } public decimal M { get; set; } }

        public sealed class Stamp { public DateTimeOffset At { get; set; } }

        public void CsvBinder_ADecimalCommaIsRefusedNotReadAsAThousandsSeparator()
        {
            var binder = new CsvBinder<Nums>(new[] { "D", "F", "M" });
            Check.Throws<FormatException>(() => binder.Bind(new[] { "1,5", "0", "0" }));
            Check.Throws<FormatException>(() => binder.Bind(new[] { "0", "1,5", "0" }));
            Check.Throws<FormatException>(() => binder.Bind(new[] { "0", "0", "1,000" }));
            Nums ok = binder.Bind(new[] { "1.5", "-2.25", "3.75" });
            Check.Equal(1.5, ok.D);
            Check.Equal(-2.25f, ok.F);
            Check.Equal(3.75m, ok.M);
        }

        public void CsvBinder_AFloatTooLargeForItsTypeIsASignedInfinityOnEveryRuntime()
        {
            var binder = new CsvBinder<Nums>(new[] { "D", "F", "M" });
            Nums r = binder.Bind(new[] { "1e400", "-1e40", "0" });
            Check.True(double.IsPositiveInfinity(r.D));
            Check.True(float.IsNegativeInfinity(r.F));
            Nums n = binder.Bind(new[] { "-1e400", "1e40", "0" });
            Check.True(double.IsNegativeInfinity(n.D));
            Check.True(float.IsPositiveInfinity(n.F));
            Check.Throws<FormatException>(() => binder.Bind(new[] { "0", "0", "1e40" }));
            // The written forms still read back.
            Nums back = binder.Bind(binder.ToFields(new Nums { D = double.MaxValue, F = float.MinValue, M = decimal.MaxValue }));
            Check.Equal(double.MaxValue, back.D);
            Check.Equal(float.MinValue, back.F);
            Check.Equal(decimal.MaxValue, back.M);
        }

        public void CsvBinder_ADateTimeOffsetWithoutAnOffsetIsUtcWhateverTheMachine()
        {
            var binder = new CsvBinder<Stamp>(new[] { "At" });
            Stamp s = binder.Bind(new[] { "2020-01-01T00:00:00" });
            Check.Equal(TimeSpan.Zero, s.At.Offset);
            Check.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), s.At.UtcDateTime);
            Stamp o = binder.Bind(new[] { "2020-01-01T00:00:00+05:30" });
            Check.Equal(TimeSpan.FromMinutes(330), o.At.Offset);
        }

#if !NETFRAMEWORK
        // ---------------- JsonFile ----------------

        public interface IShape { int Sides { get; } }

        public void JsonFile_TryLoad_LetsAnUnsupportedTypePropagate()
        {
            using var tmp = new TempDirectory();
            string path = tmp.Combine("x.json");
            File.WriteAllText(path, "{\"Sides\":3}");
            Check.Throws<NotSupportedException>(() => JsonFile.TryLoad<IShape>(path, out _, out _));
            File.WriteAllText(path, "{oops");
            Check.False(JsonFile.TryLoad<Dictionary<string, int>>(path, out _, out string? error));
            Check.True(error is not null);
        }
#endif

        // ---------------- FileRetention ----------------

        public void FileRetention_QuestionMarkMatchesWhatTheListingDoes()
        {
            using var tmp = new TempDirectory();
            foreach (string n in new[] { "app.log", "app1.log", "app12.log", "README", "x.c" })
                File.WriteAllText(tmp.Combine(n), n);
            string Deleted(string pattern) => string.Join("|", FileRetention.WhatIf(tmp.Path, pattern, new RetentionPolicy { MaxCount = 0 })
                .Deleted.Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal));
            Check.Equal("app.log|app1.log", Deleted("app?.log"));
            Check.Equal("README", Deleted("*."));
            // Every file the listing returns is deleted when none may stay (.NET Framework's native listing differs on
            // some of these, "*.?" for one, so the reference is the listing itself).
            foreach (string pattern in new[] { "app?.log", "*.?", "*.??", "?", "????", "app??.log", "*.*", "*.", "*?.log", "x.?" })
            {
                string listed = string.Join("|", Directory.GetFiles(tmp.Path, pattern).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal));
                Check.Equal(listed, Deleted(pattern), pattern);
            }
        }

#if !NETFRAMEWORK
        public void FileRetention_Matches_AgreesWithTheBclWin32Rules()
        {
            MethodInfo m = typeof(FileRetention).GetMethod("Matches", BindingFlags.NonPublic | BindingFlags.Static)!;
            var rng = new Random(75);
            const string nameChars = "aB.x";
            const string patChars = "ab.*?X";
            for (int trial = 0; trial < 20000; trial++)
            {
                string name = new string(Enumerable.Range(0, rng.Next(1, 7)).Select(_ => nameChars[rng.Next(nameChars.Length)]).ToArray());
                string pattern = new string(Enumerable.Range(0, rng.Next(1, 6)).Select(_ => patChars[rng.Next(patChars.Length)]).ToArray());
                // What a directory listing does: translate the DOS wildcards, then match.
                bool expected = FileSystemName.MatchesWin32Expression(FileSystemName.TranslateWin32Expression(pattern), name, ignoreCase: true);
                bool actual = (bool)m.Invoke(null, new object[] { name, pattern })!;
                Check.Equal(expected, actual, $"'{pattern}' against '{name}'");
            }
        }
#endif

        // ---------------- ByteSize ----------------

        public void ByteSize_Format_NeverPrintsAFullUnitInsteadOfCarrying()
        {
            // .NET Framework printed "1024.00 TiB" and "1000.0000 MB" for these (its "F" rounds through 15 digits).
            Check.False(ByteSize.Format(1125894409284485, true, 2).StartsWith("1024", StringComparison.Ordinal));
            Check.False(ByteSize.Format(999999950, false, 4).StartsWith("1000", StringComparison.Ordinal));
            for (int dec = 0; dec <= 6; dec++)
                for (int unit = 1; unit <= 5; unit++)
                    foreach (bool binary in new[] { true, false })
                    {
                        double edge = Math.Pow(binary ? 1024 : 1000, unit + 1);
                        for (int k = 1; k <= 400; k++)
                        {
                            long bytes = (long)(edge * (1 - k * Math.Pow(10, -dec - 3) / 7));
                            string s = ByteSize.Format(bytes, binary, dec);
                            Check.False(s.StartsWith(binary ? "1024" : "1000", StringComparison.Ordinal), $"{bytes} dec={dec}: {s}");
                        }
                    }
        }

        public void ByteSize_Format_RefusesMoreThan99Decimals()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => ByteSize.Format(1536, true, 100));
            Check.Equal("decimals", ex.ParamName);
            Check.Equal("1." + new string('5', 1) + new string('0', 98) + " KiB", ByteSize.Format(1536, true, 99));
        }

        // ---------------- ChecksumManifest ----------------

        public void ChecksumManifest_RefusesAPathThatNamesNoFile()
        {
            string hash = new string('a', 64);
            var ex = Check.Throws<ArgumentException>(() => new ChecksumManifest(new[] { new KeyValuePair<string, string>("./", hash) }));
            Check.Equal("entries", ex.ParamName);
            var empty = Check.Throws<ArgumentException>(() => new ChecksumManifest(new[] { new KeyValuePair<string, string>("", hash) }));
            Check.Equal("entries", empty.ParamName);
            var ok = new ChecksumManifest(new[] { new KeyValuePair<string, string>("./a.txt", hash) });
            Check.Equal(1, ChecksumManifest.Parse(ok.ToText()).Entries.Count);
        }

        // ---------------- SafeFileName ----------------

        public void SafeFileName_KeepsItsByteCapAndReplacesLoneSurrogates()
        {
            var cap = Check.Throws<ArgumentOutOfRangeException>(() => SafeFileName.MakeSafe("...", '€', 1));
            Check.Equal("maxLength", cap.ParamName);
            Check.Equal("€", SafeFileName.MakeSafe("...", '€', 3));
            Check.Equal("a_b", SafeFileName.MakeSafe("a\uD800b"));
            Check.Equal("a_b", SafeFileName.MakeSafe("a\uDC00b"));
            Check.Equal("a😀b", SafeFileName.MakeSafe("a😀b"));
            Check.Equal("a__", SafeFileName.MakeSafe("a\uDC00\uD800"));
            var sur = Check.Throws<ArgumentException>(() => SafeFileName.MakeSafe("x", '\uD800'));
            Check.Equal("replacement", sur.ParamName);
        }

#if !NETFRAMEWORK
        // ---------------- TarUtils ----------------

        public void TarUtils_ABackslashIsANameCharacterOnUnix()
        {
            using var tmp = new TempDirectory();
            string archive = tmp.Combine("b.tar");
            using (var fs = File.Create(archive))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax))
                w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "a\\b") { DataStream = new MemoryStream(new byte[] { 1 }) });
            string dest = tmp.Combine("out");
            TarUtils.ExtractToDirectory(archive, dest);
            if (TestPlatform.IsWindows)
                Check.True(File.Exists(Path.Combine(dest, "a", "b")));   // a separator here, as Create writes it
            else
            {
                Check.True(File.Exists(Path.Combine(dest, "a\\b")));
                Check.False(Directory.Exists(Path.Combine(dest, "a")));
            }
        }
#endif

        // ---------------- robustness ----------------

        // The \\?\ form, which reaches a name ending in a dot or space that the plain form normalises away.
        private static string X(string path) => @"\\?\" + path;

        // The snapshot's per-event members are internal.
        private static bool Snap(DirectorySnapshot snap, string member, string path)
            => typeof(DirectorySnapshot).GetMethod(member, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(snap, new object[] { path }) is true;

        public void DirectorySnapshot_KnowsANameEndingInADotByItsListedPath()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string dotted = tmp.Combine("pre.");
            File.WriteAllText(X(dotted), "x");
            File.WriteAllText(tmp.Combine("pre"), "other file");
            var snap = DirectorySnapshot.Take(tmp.Path);
            Check.True(Snap(snap, "Contains", dotted), "the listed name was not found");
            File.Delete(X(dotted));
            Snap(snap, "Record", dotted);   // a deletion: the entry goes
            Check.False(Snap(snap, "Contains", dotted), "the deleted file was kept");
            Check.True(Snap(snap, "Contains", tmp.Combine("pre")), "the other file was dropped");
            File.WriteAllText(X(dotted), "back");
            Snap(snap, "Record", dotted);
            Check.True(Snap(snap, "Contains", dotted));
            Snap(snap, "Forget", dotted);
            Check.False(Snap(snap, "Contains", dotted));
            Check.True(Snap(snap, "Contains", tmp.Combine("pre")));
        }

        public void FileWatcher_StableForHoldsBackANameEndingInADot()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            var batches = new System.Collections.Concurrent.ConcurrentQueue<(DateTime At, FileChange Change)>();
            using var w = new FileWatcher(tmp.Path, new FileWatcherOptions { Debounce = TimeSpan.FromMilliseconds(50), StableFor = TimeSpan.FromMilliseconds(800) });
            w.Changed += (_, e) => { foreach (FileChange c in e.Changes) batches.Enqueue((DateTime.UtcNow, c)); };
            w.Start();
            File.WriteAllText(X(tmp.Combine("dotted.")), "x");
            DateTime written = DateTime.UtcNow;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!batches.Any(b => b.Change.FullPath.EndsWith("dotted.", StringComparison.Ordinal)) && sw.Elapsed < TimeSpan.FromSeconds(10))
                System.Threading.Thread.Sleep(20);
            var hit = batches.FirstOrDefault(b => b.Change.FullPath.EndsWith("dotted.", StringComparison.Ordinal));
            Check.True(hit.Change != null, "never reported");
            Check.True(hit.At - written >= TimeSpan.FromMilliseconds(600), $"reported after {(hit.At - written).TotalMilliseconds:F0} ms, before StableFor");
        }

        public void TempFile_RefusesAnExtensionThatIsAPath()
        {
            foreach (string bad in new[] { "/../../victim.txt", "\\..\\x", ".txt:stream", "a/b", "x\0" })
            {
                var ex = Check.Throws<ArgumentException>(() => new TempFile(bad), bad);
                Check.Equal("extension", ex.ParamName, bad);
            }
            using (var ok = new TempFile("txt")) Check.True(ok.Path.EndsWith(".txt", StringComparison.Ordinal));
            using (var ok = new TempFile(".tar.gz")) Check.True(ok.Path.EndsWith(".tar.gz", StringComparison.Ordinal));
        }

#if !NETFRAMEWORK
        public void TempDirectory_DeletesATreeTooDeepForARecursiveDelete()
        {
            // Directory.Delete(recursive) overflowed the stack, which ends the process: on a 1 MB stack somewhere past
            // 4,500 levels, on this 64 KB one at 1,500, so the test stays quick.
            var tmp = new TempDirectory();
            string deep = Path.Combine(tmp.Path, Path.Combine(Enumerable.Repeat("d", 1500).ToArray()));
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "leaf.txt"), "x");
            File.SetAttributes(Path.Combine(deep, "leaf.txt"), FileAttributes.ReadOnly);
            var thread = new System.Threading.Thread(tmp.Dispose, 64 * 1024);
            thread.Start();
            thread.Join();
            Check.False(Directory.Exists(tmp.Path), "the tree was left behind");
        }

        public void DirectoryUtils_DeleteAndCopyCostAboutWhatTheBclDoes()
        {
            // Every ancestor was checked for every item: a chain 800 deep took 40 s to delete, 80 times the BCL's
            // recursive delete, and a copy was slower still. Graded against the BCL on the same tree, not a clock.
            const int depth = 300;
            using var tmp = new TempDirectory();
            string Chain(string top)
            {
                string dir = top;
                for (int i = 0; i < depth; i++)
                {
                    dir = Path.Combine(dir, "d");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "f.txt"), "x");
                }
                return top;
            }
            string src = Chain(tmp.Combine("src"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string s = src, t = tmp.Combine("naive");
            for (int i = 0; i < depth; i++)
            {
                s = Path.Combine(s, "d");
                t = Path.Combine(t, "d");
                Directory.CreateDirectory(t);
                File.Copy(Path.Combine(s, "f.txt"), Path.Combine(t, "f.txt"));
            }
            TimeSpan naiveCopy = sw.Elapsed;
            sw.Restart();
            Check.Equal(depth, DirectoryUtils.Copy(src, tmp.Combine("dst")).FilesCopied);
            TimeSpan copy = sw.Elapsed;
            sw.Restart();
            Directory.Delete(tmp.Combine("naive"), recursive: true);
            TimeSpan naiveDelete = sw.Elapsed;
            sw.Restart();
            Check.True(DirectoryUtils.Delete(src));
            TimeSpan delete = sw.Elapsed;
            Check.False(Directory.Exists(src));
            Check.True(copy.Ticks < 40 * Math.Max(naiveCopy.Ticks, TimeSpan.TicksPerMillisecond * 20),
                $"copy took {copy.TotalMilliseconds:F0} ms; the BCL {naiveCopy.TotalMilliseconds:F0} ms");
            Check.True(delete.Ticks < 15 * Math.Max(naiveDelete.Ticks, TimeSpan.TicksPerMillisecond * 20),
                $"delete took {delete.TotalMilliseconds:F0} ms; the BCL {naiveDelete.TotalMilliseconds:F0} ms");
        }

        public void ZipUtils_AddOrReplaceEntry_DoesNotHoldTheArchiveInMemory()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("big.zip");
            var data = new byte[48 << 20];
            new Random(75).NextBytes(data);   // incompressible, so the archive is as big as the data
            using (var fs = File.Create(zip))
            using (var a = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (Stream s = a.CreateEntry("big.bin", System.IO.Compression.CompressionLevel.NoCompression).Open()) s.Write(data, 0, data.Length);
                using (Stream s = a.CreateEntry("small.txt").Open()) s.Write(new byte[] { 1, 2, 3 }, 0, 3);
                a.CreateEntry("empty/");
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            ZipUtils.AddOrReplaceEntry(zip, "small.txt", new byte[] { 9 });
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 16 << 20, $"allocated {allocated >> 20} MB for a 48 MB archive");
            Check.Equal("big.bin|empty/|small.txt", string.Join("|", ZipUtils.ListEntries(zip).OrderBy(x => x, StringComparer.Ordinal)));
            Check.Equal(9, ZipUtils.ReadEntryBytes(zip, "small.txt")![0]);
            Check.True(data.AsSpan().SequenceEqual(ZipUtils.ReadEntryBytes(zip, "big.bin")), "the kept entry changed");
        }

        public void StreamUtils_ReadExactly_DoesNotAllocateTheWholeCountUpFront()
        {
            var short4 = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            long before = GC.GetAllocatedBytesForCurrentThread();
            Check.Throws<EndOfStreamException>(() => StreamUtils.ReadExactly(short4, 0x7FFFFFC7));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 8 << 20, $"allocated {allocated >> 20} MB for a 4-byte stream");
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => StreamUtils.ReadExactly(new MemoryStream(), 0x7FFFFFC8));
            Check.Equal("count", ex.ParamName);
            var big = new byte[3 << 20];
            new Random(9).NextBytes(big);
            Check.True(big.AsSpan().SequenceEqual(StreamUtils.ReadExactly(new MemoryStream(big), big.Length)));
            Check.Equal(0, StreamUtils.ReadExactly(new MemoryStream(), 0).Length);
        }
#endif

        public void LineReader_APartialBomStillCountsTowardTheLimit()
        {
            Check.Throws<InvalidDataException>(() => LineReader.ReadLines(new MemoryStream(new byte[] { 0xEF, 0x0A }), maxLineBytes: 0).ToList());
            Check.Throws<InvalidDataException>(() => LineReader.ReadLines(new MemoryStream(new byte[] { 0xEF }), maxLineBytes: 0).ToList());
            Check.Throws<InvalidDataException>(() => LineReader.ReadLines(new MemoryStream(new byte[] { 0xEF, 0xBB, (byte)'x' }), maxLineBytes: 2).ToList());
            Check.Equal("", LineReader.ReadLines(new MemoryStream(new byte[] { 0xEF, 0xBB, 0xBF, 0x0A }), maxLineBytes: 0).Single().Text);
            var limit = Check.Throws<ArgumentOutOfRangeException>(() => LineReader.ReadLines(new MemoryStream(), maxLineBytes: 0x3FFFFFE0));
            Check.Equal("maxLineBytes", limit.ParamName);
        }

        public void FixedWidth_RefusesWidthsTooLongForAString()
        {
            var one = Check.Throws<ArgumentOutOfRangeException>(() => FixedWidth.Format(new[] { "a" }, new[] { 0x40000000 }));
            Check.Equal("widths", one.ParamName);
            var two = Check.Throws<ArgumentOutOfRangeException>(() => FixedWidth.Format(new[] { "a", "b" }, new[] { int.MaxValue, 1 }));
            Check.Equal("widths", two.ParamName);
            Check.Equal("a  b", FixedWidth.Format(new[] { "a", "b" }, new[] { 3, 1 }));
        }

        public void DirectoryUtils_Copy_StopsWhenCancelled()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src");
            Directory.CreateDirectory(src);
            for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(src, i + ".txt"), "x");
            var options = new CopyOptions();
            // Through reflection, so the test builds against the code before the property existed.
            PropertyInfo? token = typeof(CopyOptions).GetProperty("CancellationToken");
            Check.True(token != null, "CopyOptions has no CancellationToken");
            using var cts = new System.Threading.CancellationTokenSource();
            int seen = 0;
            options.FileFilter = _ => { if (++seen == 2) cts.Cancel(); return true; };
            token!.SetValue(options, cts.Token);
            Check.Throws<OperationCanceledException>(() => DirectoryUtils.Copy(src, tmp.Combine("dst"), options));
            Check.True(Directory.GetFiles(tmp.Combine("dst")).Length <= 2, "the copy went on after the cancel");
        }

#if NETFRAMEWORK
        // ---------------- .NET Framework only: the private DOS matcher and the reparse-tag check ----------------

        private static bool DosMatchOf(string pattern, string name)
        {
            Type t = typeof(DirectoryUtils);
            var expr = (string)t.GetMethod("DosExpression", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { pattern })!;
            return (bool)t.GetMethod("DosMatch", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { expr, name })!;
        }

        public void DirectoryUtils_DosMatch_SurvivesAHugePatternAndAgreesWithTheWin32Rules()
        {
            // Recursive, it overflowed the stack (ending the process) on 30,000 '*'.
            Check.True(DosMatchOf(new string('*', 30000), "abc.txt"));
            Check.False(DosMatchOf(new string('*', 30000) + "z", "abc.txt"));
            // FileRetention's matcher is checked against .NET Core's own on that runtime; the two must agree. The old
            // one refused "a." for "*." and "a?", which .NET Core lists.
            Check.True(DosMatchOf("*.", "a."), "'*.' against 'a.'");
            Check.True(DosMatchOf("a?", "a."), "'a?' against 'a.'");
            MethodInfo reference = typeof(FileRetention).GetMethod("Matches", BindingFlags.NonPublic | BindingFlags.Static)!;
            var rng = new Random(751);
            const string nameChars = "aB.x";
            const string patChars = "ab.*?X";
            for (int trial = 0; trial < 20000; trial++)
            {
                string name = new string(Enumerable.Range(0, rng.Next(1, 7)).Select(_ => nameChars[rng.Next(nameChars.Length)]).ToArray());
                string pattern = new string(Enumerable.Range(0, rng.Next(1, 6)).Select(_ => patChars[rng.Next(patChars.Length)]).ToArray());
                bool expected = (bool)reference.Invoke(null, new object[] { name, pattern })!;
                Check.Equal(expected, DosMatchOf(pattern, name), $"'{pattern}' against '{name}'");
            }
        }

        public void DirectoryUtils_IsLink_IsOnlyForLinksNotEveryReparsePoint()
        {
            // An app execution alias (a reparse point that is not a link) was taken for one on .NET Framework.
            if (!TestPlatform.IsWindows) return;
            MethodInfo isLink = typeof(DirectoryUtils).GetMethod("IsLink", BindingFlags.NonPublic | BindingFlags.Static)!;
            string aliases = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
            string? alias = Directory.Exists(aliases)
                ? Directory.GetFiles(aliases, "*.exe").FirstOrDefault(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0)
                : null;
            if (alias != null) Check.False((bool)isLink.Invoke(null, new object[] { alias })!, alias);
            using var tmp = new TempDirectory();
            string target = tmp.Combine("target"), junction = tmp.Combine("junction");
            Directory.CreateDirectory(target);
            var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            mk!.WaitForExit(10000);
            Check.True((bool)isLink.Invoke(null, new object[] { junction })!, "a junction was not a link");
            Check.False((bool)isLink.Invoke(null, new object[] { target })!, "a plain folder was a link");
        }
#endif
    }
}
