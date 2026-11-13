using System;
using System.Collections.Generic;
using System.Diagnostics;
#if !NETFRAMEWORK
using System.Formats.Tar;
#endif
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>
    /// Regressions from review round 58 (IO, fourth pass): each test failed before its fix (the signed-zero, pattern and
    /// trailing-dot ones on .NET Framework, where they first broke).
    /// </summary>
    public sealed class IOReview58Tests
    {
        private static bool TryJunction(string link, string target)
        {
            if (!TestPlatform.IsWindows) return false;
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(10000);
            return p.ExitCode == 0 && Directory.Exists(link);
        }

        // The \\?\ form, which reaches a name ending in a dot or space that the plain form normalises away. The path is
        // already full (TempDirectory's), and GetFullPath would itself strip the dot.
        private static string X(string path) => @"\\?\" + path;

        // A folder holding "a." ("dot") beside "a" ("plain"): opened by its plain path, "a." is "a".
        private static string DotPair(TempDirectory tmp, string name)
        {
            string dir = tmp.Combine(name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(X(Path.Combine(dir, "a.")), "dot");
            File.WriteAllText(Path.Combine(dir, "a"), "plain");
            return dir;
        }

        // ---------------- DirectoryUtils: a folder swapped for a junction mid-operation ----------------

        public void Delete_AFolderSwappedForAJunctionLosesOnlyTheLink()
        {
            // While a retry waited on a locked file, root\y became a junction to a folder outside: its files were deleted.
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string root = tmp.Combine("root"), outside = tmp.Combine("outside"), moved = tmp.Combine("moved");
            Directory.CreateDirectory(Path.Combine(root, "x"));
            Directory.CreateDirectory(Path.Combine(root, "y"));
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(root, "x", "lock.txt"), "l");
            File.WriteAllText(Path.Combine(root, "y", "data.txt"), "y");
            File.WriteAllText(Path.Combine(outside, "data.txt"), "keep");
            var hold = new FileStream(Path.Combine(root, "x", "lock.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
            bool linked = false;
            var swapper = new Thread(() =>
            {
                try
                {
                    Thread.Sleep(300);
                    Directory.Move(Path.Combine(root, "y"), moved);
                    linked = TryJunction(Path.Combine(root, "y"), outside);
                }
                finally { hold.Dispose(); }
            }) { IsBackground = true };
            swapper.Start();
            try { DirectoryUtils.Delete(root, retries: 400, retryDelay: TimeSpan.FromMilliseconds(10)); }
            catch (IOException) { }
            Check.True(swapper.Join(15000));
            Check.True(linked, "could not make the junction");
            Check.True(File.Exists(Path.Combine(outside, "data.txt")), "a file outside was deleted through the junction");
        }

        public void Copy_ASourceFolderSwappedForAJunctionIsRefused()
        {
            // Once the copy had started, src\y became a junction to a folder outside,
            // and that folder's files were copied as if they were the source's.
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst"), outside = tmp.Combine("outside"), moved = tmp.Combine("moved");
            Directory.CreateDirectory(Path.Combine(src, "y"));
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(src, "trigger.txt"), "t");
            File.WriteAllText(Path.Combine(src, "y", "data.txt"), "y");
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
            bool linked = false, swapped = false;
            var options = new CopyOptions
            {
                FileFilter = f =>
                {
                    if (!swapped && f.EndsWith("trigger.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        swapped = true;
                        Directory.Move(Path.Combine(src, "y"), moved);
                        linked = TryJunction(Path.Combine(src, "y"), outside);
                    }
                    return true;
                },
            };
            Exception? error = null;
            try { DirectoryUtils.Copy(src, dst, options); } catch (Exception ex) { error = ex; }
            Check.True(linked, "could not make the junction");
            Check.False(File.Exists(Path.Combine(dst, "y", "secret.txt")), "the junction's target was copied");
            Check.True(error is IOException, $"expected IOException, got {error?.GetType().Name ?? "no exception"}");
        }

        public void Copy_ADestinationFolderSwappedForAJunctionIsRefused()
        {
            // dst\y was checked once, when it was made: swapped for a junction while its files were being copied, the
            // rest of them were written outside.
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst"), outside = tmp.Combine("outside"), moved = tmp.Combine("moved");
            Directory.CreateDirectory(Path.Combine(src, "y"));
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(src, "y", "trigger.txt"), "t");
            File.WriteAllText(Path.Combine(src, "y", "data.txt"), "y");
            bool linked = false, swapped = false;
            var options = new CopyOptions
            {
                FileFilter = f =>
                {
                    if (!swapped && f.EndsWith("trigger.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        swapped = true;
                        Directory.Move(Path.Combine(dst, "y"), moved);
                        linked = TryJunction(Path.Combine(dst, "y"), outside);
                    }
                    return true;
                },
            };
            Exception? error = null;
            try { DirectoryUtils.Copy(src, dst, options); } catch (Exception ex) { error = ex; }
            Check.True(linked, "could not make the junction");
            Check.False(File.Exists(Path.Combine(outside, "data.txt")) || File.Exists(Path.Combine(outside, "trigger.txt")),
                "the copy was written through the junction");
            Check.True(error is IOException, $"expected IOException, got {error?.GetType().Name ?? "no exception"}");
        }

        // ---------------- Names ending in a dot or a space ----------------

        public void Copy_KeepsANameEndingInADot()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = DotPair(tmp, "src"), dst = tmp.Combine("dst");
            DirectoryUtils.Copy(src, dst);
            Check.Equal("dot", File.ReadAllText(X(Path.Combine(dst, "a."))));
            Check.Equal("plain", File.ReadAllText(Path.Combine(dst, "a")));
        }

        public void Delete_RemovesANameEndingInADot()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string dir = DotPair(tmp, "d");
            Directory.CreateDirectory(X(Path.Combine(dir, "b ")));
            File.WriteAllText(X(Path.Combine(dir, "b ", "c.")), "c");
            Check.True(DirectoryUtils.Delete(dir));
            Check.False(Directory.Exists(dir));
        }

        public void WalkSummarizeAndRelativePath_KeepANameEndingInADot()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string dir = DotPair(tmp, "d");
            var names = DirectoryUtils.EnumerateFiles(dir).Select(f => DirectoryUtils.GetRelativePath(dir, f)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check.Equal("a|a.", string.Join("|", names));
            Check.Equal(8L, DirectoryUtils.Summarize(dir).TotalBytes);
            Check.Equal("a.", DirectoryUtils.GetRelativePath(dir, Path.Combine(dir, "a.")));
        }

        public void Zip_CreateFromDirectoryKeepsANameEndingInADot()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = DotPair(tmp, "src"), zip = tmp.Combine("z.zip");
            ZipUtils.CreateFromDirectory(src, zip);
            Check.Equal("a|a.", string.Join("|", ZipUtils.ListEntries(zip).OrderBy(n => n, StringComparer.Ordinal)));
            Check.Equal("dot", Encoding.UTF8.GetString(ZipUtils.ReadEntryBytes(zip, "a.")!));
            Check.Equal("plain", Encoding.UTF8.GetString(ZipUtils.ReadEntryBytes(zip, "a")!));
        }

#if !NETFRAMEWORK   // TarUtils is net8.0-only
        public void Tar_CreateFromDirectoryKeepsANameEndingInADot()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = DotPair(tmp, "src"), tar = tmp.Combine("t.tar");
            TarUtils.CreateFromDirectory(src, tar);
            var contents = new Dictionary<string, string>();
            using (var fs = File.OpenRead(tar))
            using (var r = new TarReader(fs))
                for (TarEntry? e; (e = r.GetNextEntry()) != null;)
                    contents[e.Name] = new StreamReader(e.DataStream!).ReadToEnd();
            Check.Equal("dot", contents["a."]);
            Check.Equal("plain", contents["a"]);
        }
#endif

        public void TempDirectory_RemovesAReadOnlyNameEndingInADot()
        {
            if (!TestPlatform.IsWindows) return;
            string path;
            using (var tmp = new TempDirectory())
            {
                path = tmp.Path;
                string f = X(tmp.Combine("r."));
                File.WriteAllText(f, "r");
                File.SetAttributes(f, FileAttributes.ReadOnly);
            }
            Check.False(Directory.Exists(path));
        }

        public void Prune_DeletesTheDottedFileNotItsPlainTwin()
        {
            // FileInfo.Delete of the listed "a." deleted "a", the file the policy kept.
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string dir = DotPair(tmp, "d");
            File.SetLastWriteTimeUtc(X(Path.Combine(dir, "a.")), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(Path.Combine(dir, "a"), new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            FileRetention.Prune(dir, "*", new RetentionPolicy { MaxCount = 1 });
            Check.Equal("plain", File.ReadAllText(Path.Combine(dir, "a")));
            Check.False(File.Exists(X(Path.Combine(dir, "a."))));
        }

        public void ChecksumManifest_HashesTheDottedFile()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string dir = DotPair(tmp, "d");
            var m = ChecksumManifest.Create(dir);
            using var sha = SHA256.Create();
            string Hex(string s) => string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Select(b => b.ToString("x2")));
            Check.Equal(Hex("dot"), m.Entries["a."]);
            Check.Equal(Hex("plain"), m.Entries["a"]);
        }

        public void DirectorySnapshot_SeesTheDottedFile()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string dir = DotPair(tmp, "d");
            DirectorySnapshot before = DirectorySnapshot.Take(dir);
            Check.Equal(2, before.Count);
            File.WriteAllText(X(Path.Combine(dir, "a.")), "longer dot");
            var changes = DirectorySnapshot.Diff(before, DirectorySnapshot.Take(dir));
            Check.Equal(1, changes.Count);
        }

        // ---------------- EnumerateFiles patterns on .NET Framework ----------------

        public void EnumerateFiles_PatternDoesNotMatchShortNames()
        {
            // .NET Framework matched 8.3 short names: "*.txt" listed a.txtx (short name A~1.TXT).
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("a.txt"), "");
            File.WriteAllText(tmp.Combine("a.txtx"), "");
            File.WriteAllText(tmp.Combine("b.TXT"), "");
            var names = DirectoryUtils.EnumerateFiles(tmp.Path, "*.txt").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
            Check.Equal("a.txt|b.TXT", string.Join("|", names));
        }

        public void EnumerateFiles_PatternEndingInTwoDotsIsTaken()
        {
            // .NET Framework threw ArgumentException for a pattern ending in "..", which .NET Core takes.
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("a"), "");
            File.WriteAllText(tmp.Combine("b.txt"), "");
            var names = DirectoryUtils.EnumerateFiles(tmp.Path, "*..").Select(Path.GetFileName).ToList();
            Check.False(names.Contains("b.txt"));
        }

        // ---------------- TarUtils: the metadata guard reads exactly what TarReader reads ----------------

#if !NETFRAMEWORK
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
        private static byte[] Octal(long v) => Ascii(Convert.ToString(v, 8).PadLeft(11, '0') + "\0");

        private static byte[] Header(string name, char type, byte[] sizeField)
        {
            var h = new byte[512];
            Ascii(name).CopyTo(h, 0);
            Ascii("0000644\0").CopyTo(h, 100);
            Ascii("0000000\0").CopyTo(h, 108);
            Ascii("0000000\0").CopyTo(h, 116);
            sizeField.CopyTo(h, 124);
            Ascii("00000000000\0").CopyTo(h, 136);
            h[156] = (byte)type;
            Ascii("ustar\0").CopyTo(h, 257);
            Ascii("00").CopyTo(h, 263);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            int sum = h.Sum(b => b);
            Ascii(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
            return h;
        }

        private static byte[] Padded(byte[] data) => data.Concat(new byte[(512 - data.Length % 512) % 512]).ToArray();

        private static string RawTar(TempDirectory tmp, params byte[][] parts)
        {
            string tar = tmp.Combine("raw-" + Guid.NewGuid().ToString("N") + ".tar");
            File.WriteAllBytes(tar, parts.SelectMany(p => p).Concat(new byte[1024]).ToArray());
            return tar;
        }

        private static string PaxThenFile(TempDirectory tmp, string records)
        {
            byte[] pax = Ascii(records);
            return RawTar(tmp, Header("pax", 'x', Octal(pax.Length)), Padded(pax), Header("a.txt", '0', Octal(1)), Padded(new byte[] { 65 }));
        }

        public void TarGuard_ALeadingNulInTheSizeFieldIsRead()
        {
            // "\0" then the digits: the guard read 0 and so never saw a 2 MB metadata entry, which TarReader read in full.
            using var tmp = new TempDirectory();
            const int size = 2 * 1024 * 1024;
            string record = size.ToString() + " comment=";
            byte[] pax = Ascii(record + new string('c', size - record.Length - 1) + "\n");
            byte[] field = Ascii("\0" + Convert.ToString(size, 8).PadLeft(10, '0') + "\0");
            string tar = RawTar(tmp, Header("pax", 'x', field), Padded(pax), Header("a.txt", '0', Octal(1)), Padded(new byte[] { 65 }));
            var ex = Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
            Check.True(ex.Message.Contains("metadata"), ex.Message);
        }

        public void TarGuard_APaxLengthSpanningANewlineIsRefused()
        {
            // The guard took the stated length (40) and skipped the "size" record inside it; TarReader splits at each
            // newline and applied it.
            using var tmp = new TempDirectory();
            string tar = PaxThenFile(tmp, "40 comment=x\n12 size=512\n15 comment=xyz\n");
            var ex = Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
            Check.True(ex.Message.Contains("PAX header"), ex.Message);
        }

        public void TarGuard_APaxSizeWithASpaceIsRefused()
        {
            // long.TryParse took " 512"; TarReader does not.
            using var tmp = new TempDirectory();
            string tar = PaxThenFile(tmp, "13 size= 512\n");
            var ex = Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
            Check.True(ex.Message.Contains("PAX size"), ex.Message);
        }

        public void TarGuard_APaxLengthPastTheDataIsRefused()
        {
            // "15 size=512\n" is 12 bytes: the guard gave up and used the header's size, while TarReader applied size=512.
            using var tmp = new TempDirectory();
            string tar = PaxThenFile(tmp, "15 size=512\n");
            var ex = Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
            Check.True(ex.Message.Contains("PAX header"), ex.Message);
        }

        // ---------------- TarUtils: names Windows does not allow ----------------

        private static string TarOf(TempDirectory tmp, string name)
        {
            string tar = tmp.Combine("n-" + Guid.NewGuid().ToString("N") + ".tar");
            using (var fs = File.Create(tar))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax))
                w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(new byte[] { 1 }) });
            return tar;
        }

        public void TarExtract_AWildcardOrTrailingDotNameIsInvalidData()
        {
            // "a?b" failed part way with a bare IOException; "a." was written as "a".
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            foreach (string name in new[] { "a?b.txt", "a*b", "a.", "d /x", "a\u0001b" })
            {
                string dst = tmp.Combine("dst-" + Guid.NewGuid().ToString("N"));
                Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(TarOf(tmp, name), dst), name);
                Check.False(File.Exists(Path.Combine(dst, "a")), name);
            }
        }

        public void TarExtract_AFileNamedLikeADirectoryIsInvalidData()
        {
            // A regular-file entry "dir/": the directory was made, then the write failed with a bare IOException.
            using var tmp = new TempDirectory();
            string dst = tmp.Combine("dst");
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(TarOf(tmp, "dir/"), dst));
            Check.False(Directory.Exists(Path.Combine(dst, "dir")));
        }

        public void TarExtract_OverwriteReplacesAReadOnlyFile()
        {
            using var tmp = new TempDirectory();
            string dst = tmp.Combine("dst"), target = Path.Combine(dst, "a.txt");
            Directory.CreateDirectory(dst);
            File.WriteAllText(target, "old");
            File.SetAttributes(target, FileAttributes.ReadOnly);
            TarUtils.ExtractToDirectory(TarOf(tmp, "a.txt"), dst, overwrite: true);
            Check.Equal(1L, new FileInfo(target).Length);
        }
#endif

        // ---------------- ZipUtils ----------------

        // .NET Framework's ZipArchive refuses some names, so a same-length placeholder is written and patched.
        private static string ZipOf(TempDirectory tmp, string name)
        {
            string zip = tmp.Combine("n-" + Guid.NewGuid().ToString("N") + ".zip");
            string placeholder = new string('q', name.Length);
            using (var fs = new FileStream(zip, FileMode.Create))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            using (var s = z.CreateEntry(placeholder).Open())
                s.Write(new byte[] { 1 }, 0, 1);
            byte[] bytes = File.ReadAllBytes(zip), from = Encoding.ASCII.GetBytes(placeholder), to = Encoding.ASCII.GetBytes(name);
            for (int i = 0; i + from.Length <= bytes.Length; i++)
                if (bytes.Skip(i).Take(from.Length).SequenceEqual(from)) Array.Copy(to, 0, bytes, i, to.Length);
            File.WriteAllBytes(zip, bytes);
            return zip;
        }

        public void ZipExtract_AWildcardOrTrailingDotNameIsRefused()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            foreach (string name in new[] { "a?b.txt", "a*b", "a.", "d /x", "a\u0001b" })
            {
                string dst = tmp.Combine("dst-" + Guid.NewGuid().ToString("N"));
                string zip = ZipOf(tmp, name);
                IOException? ex = null;
                try { ZipUtils.ExtractToDirectory(zip, dst); } catch (IOException e) { ex = e; }
                // .NET Framework's reader refuses a control character itself, and ZipUtils reports that with its own message.
                bool refused = ex != null && (ex.Message.Contains("Windows does not allow") || ex.Message.Contains(".NET Framework cannot read"));
                Check.True(refused, $"{name}: {ex?.Message ?? "no exception"}");
                Check.False(File.Exists(Path.Combine(dst, "a")), name);
            }
        }

        public void ZipExtract_OverwriteReplacesAReadOnlyFile()
        {
            using var tmp = new TempDirectory();
            string dst = tmp.Combine("dst"), target = Path.Combine(dst, "a.txt");
            Directory.CreateDirectory(dst);
            File.WriteAllText(target, "old");
            File.SetAttributes(target, FileAttributes.ReadOnly);
            ZipUtils.ExtractToDirectory(ZipOf(tmp, "a.txt"), dst, overwrite: true);
            Check.Equal(1L, new FileInfo(target).Length);
        }

        public void AddOrReplaceEntry_AFailureLeavesTheZipAsItWas()
        {
            // The update ran in place: an undefined level threw after the zip had been rewritten, losing every entry.
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("z.zip");
            ZipUtils.AddOrReplaceEntry(zip, "keep.txt", Encoding.UTF8.GetBytes("kept"));
            byte[] before = File.ReadAllBytes(zip);
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => ZipUtils.AddOrReplaceEntry(zip, "new.txt", new byte[] { 1 }, (CompressionLevel)42));
            Check.Equal("level", ex.ParamName);
            Check.True(before.SequenceEqual(File.ReadAllBytes(zip)), "the zip changed");
            Check.Equal("kept", Encoding.UTF8.GetString(ZipUtils.ReadEntryBytes(zip, "keep.txt")!));
            Check.Equal(0, Directory.GetFiles(tmp.Path, "*.tmp").Length);
        }

        public void AddOrReplaceEntry_StillReplacesAndCreates()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("new.zip");
            ZipUtils.AddOrReplaceEntry(zip, "a.txt", new byte[] { 1 });
            ZipUtils.AddOrReplaceEntry(zip, "b.txt", new byte[] { 2 });
            ZipUtils.AddOrReplaceEntry(zip, "a.txt", new byte[] { 3, 3 });
            Check.Equal("a.txt|b.txt", string.Join("|", ZipUtils.ListEntries(zip).OrderBy(n => n, StringComparer.Ordinal)));
            Check.True(ZipUtils.ReadEntryBytes(zip, "a.txt")!.SequenceEqual(new byte[] { 3, 3 }));
            Check.Equal(0, Directory.GetFiles(tmp.Path, "*.tmp").Length);
        }

        public void CreateFromDirectory_AnUndefinedLevelIsRefusedUpFront()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), zip = tmp.Combine("z.zip");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => ZipUtils.CreateFromDirectory(src, zip, level: (CompressionLevel)42));
            Check.Equal("level", ex.ParamName);
        }

        // ---------------- DotEnv, SafeFileName ----------------

        public void DotEnv_ALoneCarriageReturnEndsALine()
        {
            var env = DotEnv.Parse("A=1\rB=2\r\nC=3\nD=4");
            Check.Equal("1|2|3|4", string.Join("|", new[] { "A", "B", "C", "D" }.Select(k => env[k])));
        }

        public void SafeFileName_TheCapIsInUtf8Bytes()
        {
            // 255 CJK characters are 765 UTF-8 bytes: too long for a Linux or macOS name.
            string cjk = SafeFileName.MakeSafe(new string('\u4E2D', 300));
            Check.True(Encoding.UTF8.GetByteCount(cjk) <= 255, cjk.Length.ToString());
            Check.Equal(85, cjk.Length);
            string emoji = SafeFileName.MakeSafe(string.Concat(Enumerable.Repeat("\U0001F600", 100)));
            Check.Equal(63 * 2, emoji.Length);
            Check.False(char.IsHighSurrogate(emoji[emoji.Length - 1]));
            Check.Equal(new string('a', 255), SafeFileName.MakeSafe(new string('a', 300)));
            Check.Equal("ab", SafeFileName.MakeSafe("ab\u00E9", maxLength: 3));
        }

        // ---------------- FileRetention.ShiftNumbered ----------------

        public void ShiftNumbered_RemovesEveryCopyPastKeepAcrossAGap()
        {
            // Cleanup stopped at the first missing number: with .3 absent, .4 stayed for good.
            using var tmp = new TempDirectory();
            string log = tmp.Combine("app.log");
            foreach (string s in new[] { "", ".1", ".2", ".4", ".9" }) File.WriteAllText(log + s, s);
            Check.True(FileRetention.ShiftNumbered(log, 2));
            var names = Directory.GetFiles(tmp.Path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
            Check.Equal("app.log.1|app.log.2", string.Join("|", names));
            Check.Equal("", File.ReadAllText(log + ".1"));
            Check.Equal(".1", File.ReadAllText(log + ".2"));
        }

        public void ShiftNumbered_ADirectoryInTheWayFailsBeforeAnythingMoves()
        {
            // With path.2 a folder, the rotation failed after some copies had already been renamed.
            using var tmp = new TempDirectory();
            string log = tmp.Combine("app.log");
            File.WriteAllText(log, "0");
            File.WriteAllText(log + ".1", "1");
            File.WriteAllText(log + ".3", "3");
            Directory.CreateDirectory(log + ".2");
            Check.Throws<IOException>(() => FileRetention.ShiftNumbered(log, 5));
            Check.Equal("0", File.ReadAllText(log));
            Check.Equal("1", File.ReadAllText(log + ".1"));
            Check.Equal("3", File.ReadAllText(log + ".3"));
            Check.False(File.Exists(log + ".4"));
        }

        // ---------------- HexDump ----------------

        public void HexDump_InputTooBigForAStringIsRefusedUpFront()
        {
            // About 250 MB of data makes a dump longer than a string can be: it failed with OutOfMemoryException only after
            // gigabytes had been built.
            byte[] big = new byte[260_000_000];
            var sw = Stopwatch.StartNew();
            var ex = Check.Throws<ArgumentException>(() => HexDump.Format(big));
            Check.Equal("data", ex.ParamName);
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
        }

        public void HexDump_LengthIsExact()
        {
            var rng = new Random(58);
            for (int trial = 0; trial < 200; trial++)
            {
                byte[] data = new byte[rng.Next(1, 700)];
                rng.NextBytes(data);
                int per = rng.Next(1, 300);
                string dump = HexDump.Format(data, per);
                int width = Math.Min(per, Math.Max(data.Length, 256));
                int lines = (data.Length + per - 1) / per;
                Check.Equal(lines * (13 + 3 * width) - 1 + data.Length, dump.Length);
            }
        }

        // ---------------- CsvBinder, CsvDialect ----------------

        public class HiddenBase { public string Name { get; set; } = ""; }
        public sealed class HiddenDerived : HiddenBase { public new int Name { get; set; } }
        public sealed class Cased { public int Name { get; set; } public int NAME { get; set; } }
        public sealed class Zeros { public double D { get; set; } public float F { get; set; } }

        public void CsvBinder_AHiddenPropertyBindsTheDerivedOne()
        {
            var binder = new CsvBinder<HiddenDerived>(new[] { "Name" });
            HiddenDerived r = binder.Bind(new[] { "42" });
            Check.Equal(42, r.Name);
            Check.Equal("", ((HiddenBase)r).Name);
            Check.Equal("7", binder.ToFields(new HiddenDerived { Name = 7 })[0]);
        }

        public void CsvBinder_CaseCollisionsPreferExactAndRefuseAmbiguity()
        {
            Cased r = new CsvBinder<Cased>(new[] { "NAME", "Name" }).Bind(new[] { "5", "6" });
            Check.Equal(5, r.NAME);
            Check.Equal(6, r.Name);
            var ex = Check.Throws<ArgumentException>(() => new CsvBinder<Cased>(new[] { "name" }));
            Check.Equal("header", ex.ParamName);
        }

        private static bool NegativeZero(double d) => d == 0 && BitConverter.DoubleToInt64Bits(d) < 0;

        public void CsvBinder_NegativeZeroRoundTrips()
        {
            // .NET Framework formats -0 as "0" and parses "-0" as +0.
            var binder = new CsvBinder<Zeros>(new[] { "D", "F" });
            string[] fields = binder.ToFields(new Zeros { D = -0.0, F = -0.0f });
            Check.Equal("-0|-0", string.Join("|", fields));
            Zeros back = binder.Bind(fields);
            Check.True(NegativeZero(back.D));
            Check.True(1 / back.F < 0);
            Zeros plus = binder.Bind(new[] { "0", "0" });
            Check.False(NegativeZero(plus.D));
        }

        public void CsvDialect_NegativeZeroRoundTrips()
        {
            Check.Equal("-0", CsvDialect.Rfc4180.FormatNumber(-0.0));
            Check.Equal("0", CsvDialect.Rfc4180.FormatNumber(0.0));
            Check.True(CsvDialect.Rfc4180.TryParseNumber(" -0 ", out double z) && NegativeZero(z));
            Check.True(CsvDialect.Rfc4180.TryParseNumber("-0.0e5", out z) && NegativeZero(z));
        }

        // ---------------- FixedWidth ----------------

        public void FixedWidth_FormatDoesNotSplitASurrogatePair()
        {
            Check.Equal("a ", FixedWidth.Format(new[] { "a\U0001F600" }, new[] { 2 }));
            Check.Equal("a\U0001F600", FixedWidth.Format(new[] { "a\U0001F600b" }, new[] { 3 }));
        }

        public void FixedWidth_ParseRefusesABoundaryInsideASurrogatePair()
        {
            Check.Throws<FormatException>(() => FixedWidth.Parse("a\U0001F600b", new[] { 2, 2 }));
            Check.Equal("a|\U0001F600", string.Join("|", FixedWidth.Parse("a\U0001F600", new[] { 1, 2 })));
        }

        public void FixedWidth_ParseTrimsSpacesOnly()
        {
            Check.Equal("\tx\t", FixedWidth.Parse(" \tx\t ", new[] { 5 })[0]);
        }
    }
}
