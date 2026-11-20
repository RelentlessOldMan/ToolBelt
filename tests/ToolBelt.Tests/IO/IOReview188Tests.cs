using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>
    /// Round 188 review regressions (IO, eleventh pass): each failed on the code before its fix.
    /// </summary>
    public sealed class IOReview188Tests
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
        private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint size);

        // The 8.3 short form of an existing path, or null where the volume makes none.
        private static string? ShortName(string path)
        {
            var sb = new StringBuilder(1024);
            uint n = GetShortPathName(path, sb, (uint)sb.Capacity);
            if (n == 0 || n > sb.Capacity) return null;
            string s = sb.ToString();
            return string.Equals(s, path, StringComparison.OrdinalIgnoreCase) ? null : s;
        }

        private static void Junction(string junction, string target)
        {
            var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            mk!.WaitForExit(10000);
            Check.True(Directory.Exists(junction), "junction not made");
        }

        // ---------- DirectoryUtils ----------

        // Path.GetFullPath made the root "data." (as EnumerateFiles lists it) "data", a different folder: Delete removed
        // that one, and EnumerateFiles, Summarize, Copy and EnsureEmpty acted on it.
        public void DirectoryUtils_RootEndingInADot_IsThatFolder()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string plain = tmp.Combine("data"), dotted = plain + ".";
            Directory.CreateDirectory(plain);
            Directory.CreateDirectory(@"\\?\" + dotted);
            File.WriteAllText(Path.Combine(plain, "in-plain.txt"), "p");
            File.WriteAllText(@"\\?\" + dotted + @"\in-dotted.txt", "d");
            string listed = DirectoryUtils.EnumerateFiles(tmp.Path).Single(f => f.EndsWith("in-dotted.txt", StringComparison.Ordinal));
            Check.Equal(dotted + @"\in-dotted.txt", listed);

            Check.Equal("in-dotted.txt", string.Join(",", DirectoryUtils.EnumerateFiles(dotted).Select(Path.GetFileName)));
            Check.Equal(1, DirectoryUtils.Summarize(dotted).FileCount);
            Check.Equal("in-dotted.txt", DirectoryUtils.GetRelativePath(dotted, listed));
            string copy = tmp.Combine("copy");
            DirectoryUtils.Copy(dotted, copy);
            Check.Equal("in-dotted.txt", string.Join(",", Directory.GetFiles(copy).Select(Path.GetFileName)));

            DirectoryUtils.EnsureEmpty(dotted);
            Check.True(File.Exists(Path.Combine(plain, "in-plain.txt")), "EnsureEmpty emptied the other folder");
            Check.False(File.Exists(@"\\?\" + dotted + @"\in-dotted.txt"), "EnsureEmpty left the folder's file");
            File.WriteAllText(@"\\?\" + dotted + @"\again.txt", "d");

            Check.True(DirectoryUtils.Delete(dotted), "Delete found nothing");
            Check.True(File.Exists(Path.Combine(plain, "in-plain.txt")), "Delete removed the other folder");
            Check.False(Directory.Exists(@"\\?\" + dotted), "Delete left the folder named with the dot");
        }

        // .NET Framework's GetFullPath keeps short names, so a destination through the source's 8.3 name passed the check
        // and the source was copied into itself.
        public void DirectoryUtils_Copy_IntoTheSourceByItsShortName_Throws()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("source-folder-long-name");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            string? shortSrc = ShortName(src);
            if (shortSrc is null) return;   // no 8.3 names on this volume
            var ex = Check.Throws<ArgumentException>(() => DirectoryUtils.Copy(src, Path.Combine(shortSrc, "sub")));
            Check.Equal("destination", ex.ParamName);
            Check.False(Directory.Exists(Path.Combine(src, "sub")), "copied into the source");
        }

        // ---------- FileWatcher ----------

        // The non-recursive snapshot used .NET Framework's own listing, which also matched short names: "*.txt" took in
        // a.txtx (A~1.TXT), which the recursive one leaves out.
        public void DirectorySnapshot_NonRecursive_IgnoresShortNames()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("a.txtx"), "x");
            File.WriteAllText(tmp.Combine("b.txt"), "y");
            Check.Equal(1, DirectorySnapshot.Take(tmp.Path, "*.txt", recursive: true).Count);
            Check.Equal(1, DirectorySnapshot.Take(tmp.Path, "*.txt", recursive: false).Count);
        }

        // ---------- TempFile ----------

        // Windows drops a trailing dot or space, so Path named a file that did not exist ("x." for the "x" created).
        public void TempFile_ExtensionEndingInADotOrSpace_IsRefused()
        {
            if (!TestPlatform.IsWindows) return;
            foreach (string e in new[] { ".", "txt.", "txt ", " ", "..", ".txt " })
            {
                var ex = Check.Throws<ArgumentException>(() => { using var t = new TempFile(e); });
                Check.Equal("extension", ex.ParamName);
            }
            using var ok = new TempFile("txt");
            Check.True(File.Exists(ok.Path), ok.Path);
        }

        // ---------- AtomicFile ----------

#if NET8_0_OR_GREATER
        // With backup, a name of 252 characters or more failed with "Unable to remove the file to be replaced", naming
        // neither the backup nor the length.
        public void AtomicFile_BackupNamePastTheLimit_IsAClearError()
        {
            using var tmp = new TempDirectory();
            string ok = tmp.Combine(new string('m', 249) + ".x"), bad = tmp.Combine(new string('n', 250) + ".x");
            File.WriteAllText(ok, "1");
            AtomicFile.WriteAllText(ok, "2", backup: true);
            Check.Equal("2", File.ReadAllText(ok));
            Check.Equal("1", File.ReadAllText(ok + ".bak"));
            File.WriteAllText(bad, "1");
            Check.Throws<PathTooLongException>(() => AtomicFile.WriteAllText(bad, "2", backup: true));
            Check.Equal("1", File.ReadAllText(bad));
            Check.Equal(3, Directory.GetFiles(tmp.Path).Length);
            AtomicFile.WriteAllText(bad, "3");
            Check.Equal("3", File.ReadAllText(bad));
        }
#endif

        // ---------- ZipUtils ----------

        // The archive and its temp file were skipped by comparing strings, so through a junction the walk read the run's
        // own open temp file and failed with a sharing violation.
        public void Zip_CreateThroughAJunction_SkipsItsOwnOutput()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), j = tmp.Combine("j");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            Junction(j, src);
            for (int run = 0; run < 2; run++)   // the second with the first run's archive in the source
            {
                ZipUtils.CreateFromDirectory(src, Path.Combine(j, "out.zip"));
                Check.Equal("a.txt", string.Join(",", ZipUtils.ListEntries(Path.Combine(src, "out.zip"))));
            }
            Check.Equal(2, Directory.GetFiles(src).Length);
            Directory.Delete(j);
        }

        // Two entries, the second named as the first one's 8.3 short name, wrote the second into the first's file.
        public void Zip_Extract_AnEntryNamedAsAnotherEntrysShortName_IsRefused()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("alias.zip"), dst = tmp.Combine("dst");
            using (var a = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (var (name, text) in new[] { ("longfilename.txt", "first"), ("LONGFI~1.TXT", "second") })
                    using (var w = new StreamWriter(a.CreateEntry(name).Open())) w.Write(text);
            }
            foreach (bool overwrite in new[] { true, false })
            {
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                Exception? caught = null;
                try { ZipUtils.ExtractToDirectory(zip, dst, overwrite); }
                catch (Exception ex) { caught = ex; }
                string first = Path.Combine(dst, "longfilename.txt");
                if (ShortName(first) is null) return;   // no 8.3 names on this volume
                Check.True(caught is IOException && caught.Message.Contains("8.3"), "overwrite " + overwrite + ": " + (caught?.Message ?? "no error"));
                Check.Equal("first", File.ReadAllText(first));
            }
        }

        // Both rewrites stamped every entry with this machine's host: a zip made on Unix came back MS-DOS, so unzip there
        // dropped the exec bit and read a backslash in a name as a separator. Graded entry by entry against the original.
        public void Zip_AddOrReplaceEntry_KeepsEveryOtherEntrysHost()
        {
            var rng = new Random(188);
            using var tmp = new TempDirectory();
            for (int trial = 0; trial < 40; trial++)
            {
                string zip = tmp.Combine("t" + trial + ".zip");
                int n = 1 + rng.Next(6);
                var names = Enumerable.Range(0, n).Select(i => rng.Next(4) == 0 ? "dir" + i + "/" : "f" + i + (rng.Next(3) == 0 ? "\\x" : "") + ".txt").ToList();
                using (var a = ZipFile.Open(zip, ZipArchiveMode.Create))
                    foreach (string name in names)
                    {
                        var e = a.CreateEntry(name, rng.Next(2) == 0 ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        if (!name.EndsWith("/", StringComparison.Ordinal)) using (var w = new StreamWriter(e.Open())) w.Write("x" + rng.Next());
                    }
                var hosts = names.Select(_ => (ushort)((new[] { 0, 3, 7, 19 }[rng.Next(4)] << 8) | (20 + rng.Next(44)))).ToList();
                PatchMadeBy(zip, hosts);
                string target = rng.Next(2) == 0 ? names[rng.Next(n)] : "new.txt";
                if (target.EndsWith("/", StringComparison.Ordinal)) target = "new.txt";
                ZipUtils.AddOrReplaceEntry(zip, target, new byte[] { 1, 2, 3 });
                var want = new List<ushort>();
                for (int i = 0; i < n; i++) if (names[i] != target) want.Add(hosts[i]);
                List<ushort> got = PatchMadeBy(zip, null);
                Check.Equal(want.Count + 1, got.Count);
                Check.Equal(string.Join(",", want), string.Join(",", got.Take(want.Count)), "trial " + trial + " names " + string.Join("|", names) + " target " + target);
            }
        }

        // An unpaired surrogate has no UTF-8 form: such names were stored as U+FFFD, two files made two entries of one
        // name, and extracting them failed.
        public void Zip_LoneSurrogateNames_AreRefused()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a\uD800.txt"), "1");
            File.WriteAllText(Path.Combine(src, "a\uD801.txt"), "2");
            string zip = tmp.Combine("out.zip");
            Check.Throws<IOException>(() => ZipUtils.CreateFromDirectory(src, zip));
            Check.False(File.Exists(zip), "an archive was written");
            Check.Equal(1, Directory.GetFiles(tmp.Path).Length + Directory.GetDirectories(tmp.Path).Length);
            var ex = Check.Throws<ArgumentException>(() => ZipUtils.AddOrReplaceEntry(zip, "b\uDC00.txt", new byte[1]));
            Check.Equal("entryName", ex.ParamName);
        }

        // Reads every central-directory record's "version made by", writing patch's values first when given.
        private static List<ushort> PatchMadeBy(string zip, List<ushort>? patch)
        {
            byte[] b = File.ReadAllBytes(zip);
            int eocd = b.Length - 22;
            while (eocd >= 0 && !(b[eocd] == 0x50 && b[eocd + 1] == 0x4B && b[eocd + 2] == 5 && b[eocd + 3] == 6)) eocd--;
            int count = BitConverter.ToUInt16(b, eocd + 10), at = (int)BitConverter.ToUInt32(b, eocd + 16);
            var values = new List<ushort>();
            for (int i = 0; i < count; i++)
            {
                Check.Equal(0x02014B50u, BitConverter.ToUInt32(b, at));
                if (patch != null) { b[at + 4] = (byte)patch[i]; b[at + 5] = (byte)(patch[i] >> 8); }
                values.Add(BitConverter.ToUInt16(b, at + 4));
                at += 46 + BitConverter.ToUInt16(b, at + 28) + BitConverter.ToUInt16(b, at + 30) + BitConverter.ToUInt16(b, at + 32);
            }
            if (patch != null) File.WriteAllBytes(zip, b);
            return values;
        }

#if NET8_0_OR_GREATER
        // ---------- TarUtils ----------

        // On .NET 8.0.3 TarReader skipped an unread entry's data without its padding on a stream that cannot seek (the
        // metadata guard), so ListEntries stopped after the first small file and a third entry failed "Unable to parse number".
        public void Tar_ListEntries_SeesEveryEntryAfterSmallFiles()
        {
            using var tmp = new TempDirectory();
            string one = tmp.Combine("one"), oneTar = tmp.Combine("one.tar");
            Directory.CreateDirectory(one);
            File.WriteAllText(Path.Combine(one, "a.txt"), "a");
            File.WriteAllText(Path.Combine(one, "z.txt"), "z");
            TarUtils.CreateFromDirectory(one, oneTar);
            Check.Equal("a.txt,z.txt", string.Join(",", TarUtils.ListEntries(oneTar)));
            var rng = new Random(1880);
            for (int trial = 0; trial < 20; trial++)
            {
                string src = tmp.Combine("s" + trial), tar = tmp.Combine("t" + trial + ".tar");
                Directory.CreateDirectory(src);
                var names = Enumerable.Range(0, 1 + rng.Next(6)).Select(i => "f" + i + ".txt").ToList();
                foreach (string name in names) File.WriteAllBytes(Path.Combine(src, name), new byte[rng.Next(4) == 0 ? 512 * rng.Next(3) : rng.Next(1500)]);
                TarUtils.CreateFromDirectory(src, tar);
                Check.Equal(string.Join(",", names), string.Join(",", TarUtils.ListEntries(tar).OrderBy(x => x, StringComparer.Ordinal)), "trial " + trial);
            }
        }

        public void Tar_CreateThroughAJunction_SkipsItsOwnOutput()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), j = tmp.Combine("j");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            Junction(j, src);
            for (int run = 0; run < 2; run++)
            {
                TarUtils.CreateFromDirectory(src, Path.Combine(j, "out.tar"));
                Check.Equal("a.txt", string.Join(",", TarUtils.ListEntries(Path.Combine(src, "out.tar"))));
            }
            Check.Equal(2, Directory.GetFiles(src).Length);
            Directory.Delete(j);
        }

        // File.Move(overwrite) dropped the old archive's Hidden attribute and refused a read-only one; ZipUtils kept both.
        public void Tar_CreateOverAnExistingArchive_KeepsHiddenAndReplacesReadOnly()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), tar = tmp.Combine("out.tar");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            TarUtils.CreateFromDirectory(src, tar);
            File.SetAttributes(tar, File.GetAttributes(tar) | FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(src, "b.txt"), "b");
            TarUtils.CreateFromDirectory(src, tar);
            Check.True((File.GetAttributes(tar) & FileAttributes.Hidden) != 0, "Hidden dropped");
            Check.Equal("a.txt,b.txt", string.Join(",", TarUtils.ListEntries(tar).OrderBy(s => s, StringComparer.Ordinal)));
            File.SetAttributes(tar, File.GetAttributes(tar) | FileAttributes.ReadOnly);
            File.WriteAllText(Path.Combine(src, "c.txt"), "c");
            TarUtils.CreateFromDirectory(src, tar);
            Check.Equal("a.txt,b.txt,c.txt", string.Join(",", TarUtils.ListEntries(tar).OrderBy(s => s, StringComparer.Ordinal)));
            File.SetAttributes(tar, FileAttributes.Normal);
        }

        public void Tar_Extract_AnEntryNamedAsAnotherEntrysShortName_IsRefused()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("alias.tar"), dst = tmp.Combine("dst");
            using (var fs = File.Create(tar))
            using (var w = new System.Formats.Tar.TarWriter(fs, System.Formats.Tar.TarEntryFormat.Pax))
                foreach (var (name, text) in new[] { ("longfilename.txt", "first"), ("LONGFI~1.TXT", "second") })
                    w.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.ASCII.GetBytes(text)) });
            foreach (bool overwrite in new[] { true, false })
            {
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                Exception? caught = null;
                try { TarUtils.ExtractToDirectory(tar, dst, overwrite); }
                catch (Exception ex) { caught = ex; }
                string first = Path.Combine(dst, "longfilename.txt");
                if (ShortName(first) is null) return;
                Check.True(caught is InvalidDataException && caught.Message.Contains("8.3"), "overwrite " + overwrite + ": " + (caught?.Message ?? "no error"));
                Check.Equal("first", File.ReadAllText(first));
            }
        }

        public void Tar_LoneSurrogateNames_AreRefused()
        {
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a\uD800.txt"), "1");
            File.WriteAllText(Path.Combine(src, "a\uD801.txt"), "2");
            string tar = tmp.Combine("out.tar");
            Check.Throws<IOException>(() => TarUtils.CreateFromDirectory(src, tar));
            Check.False(File.Exists(tar), "an archive was written");
        }
#endif
    }
}
