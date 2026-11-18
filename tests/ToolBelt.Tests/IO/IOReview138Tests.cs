using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    // Round 138 (IO, eighth pass): each regression failed on the code before its fix.
    public sealed class IOReview138Tests
    {
        private static bool OnWindows => Path.DirectorySeparatorChar == '\\';

        // ---- FileWatcher.Rescan: a trailing-dot name is not its plain twin ----

        public void FileWatcher_RescanReportsTrailingDotChanges()
        {
            if (!OnWindows) return;
            using var tmp = new TempDirectory();
            using var w = new FileWatcher(tmp.Path);
            w.Start();
            w.Stop();
            File.WriteAllText(@"\\?\" + tmp.Combine("pre."), "x");
            File.WriteAllText(tmp.Combine("ok.txt"), "x");
            Check.Equal(2, w.Rescan());
            Check.Equal("Created ok.txt,Created pre.", Names(w.Flush()));

            // "pre." deleted while "pre" exists: the re-check saw "pre" and dropped the deletion.
            File.WriteAllText(tmp.Combine("pre"), "y");
            w.Rescan();
            w.Flush();
            File.Delete(@"\\?\" + tmp.Combine("pre."));
            Check.Equal(1, w.Rescan());
            Check.Equal("Deleted pre.", Names(w.Flush()));
        }

        private static string Names(IReadOnlyList<FileChange> changes)
            => string.Join(",", changes.Select(c => c.Kind + " " + Path.GetFileName(c.FullPath)).OrderBy(s => s, StringComparer.Ordinal));

        // ---- FileWatcher's filter: a '*' in the name is still matched by a '*' in the pattern ----

        public void FileWatcher_StarMatchesANameHoldingAStar()
        {
            MethodInfo matches = typeof(FileWatcher).GetMethod("Matches", BindingFlags.NonPublic | BindingFlags.Static)!;
            bool M(string name, string pattern) => (bool)matches.Invoke(null, new object[] { name, pattern })!;
            Check.True(M("a*xb", "a*b"), "a*xb");
            Check.True(M("a**b", "a*b"), "a**b");
            Check.True(M("x*y*z", "*z"), "x*y*z");
            // Differential: against a regex reading of the same rule, over random names from a small alphabet.
            var rng = new Random(138);
            const string alphabet = "ab*?";
            for (int i = 0; i < 4000; i++)
            {
                string name = new string(Enumerable.Range(0, rng.Next(1, 7)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
                string pattern = new string(Enumerable.Range(0, rng.Next(1, 5)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
                if (pattern == "*.*") continue;
                string regex = "^" + string.Concat(pattern.Select(c => c == '*' ? ".*" : c == '?' ? "." : System.Text.RegularExpressions.Regex.Escape(c.ToString()))) + "$";
                bool expected = System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                Check.Equal(expected, M(name, pattern), name + " ~ " + pattern);
            }
        }

#if NET8_0_OR_GREATER
        // ---- FileRetention: the pattern match no longer allocates a pattern × name table per file ----

        public void FileRetention_MatchUsesLinearMemory()
        {
            MethodInfo matches = typeof(FileRetention).GetMethod("Matches", BindingFlags.NonPublic | BindingFlags.Static)!;
            string pattern = "a" + new string('*', 20000) + ".log";
            string name = "a" + new string('b', 200) + ".log";
            Check.True((bool)matches.Invoke(null, new object[] { name, pattern })!);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) Check.True((bool)matches.Invoke(null, new object[] { name, pattern })!);
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            // The table was 20005 × 202 bools (4 MB) per call; two rows and the expression are under 100 KB.
            Check.True(used < 10 * 100_000, "allocated " + used + " bytes");
        }
#endif

        // ---- ZipUtils.AddOrReplaceEntry keeps what the streamed copy dropped ----

        public void Zip_RewriteKeepsLegacyCodePageNames()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("legacy.zip");
            byte[] n1 = { 0x82, (byte)'a', (byte)'.', (byte)'t', (byte)'x', (byte)'t' }, n2 = { 0x83, (byte)'a', (byte)'.', (byte)'t', (byte)'x', (byte)'t' };
            File.WriteAllBytes(zip, BuildZip(new[] { new RawEntry(n1, "one"), new RawEntry(n2, "two") }, null));
            ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("new"));
            List<RawEntry> back = ReadCentral(File.ReadAllBytes(zip), out _);
            Check.Equal(3, back.Count);
            Check.True(back.Any(e => e.Name.SequenceEqual(n1) && (e.Flags & 0x800) == 0), "the 0x82 name was rewritten");
            Check.True(back.Any(e => e.Name.SequenceEqual(n2) && (e.Flags & 0x800) == 0), "the 0x83 name was rewritten");
        }

        // A code-page name beside a UTF-8 one: kept both on .NET 8; .NET Framework's update mode can keep only one kind,
        // so the call is refused and the file left as it was rather than a name lost.
        public void Zip_RewriteOfMixedNameEncodingsLosesNoName()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("mixed.zip");
            byte[] cp = { 0x82, (byte)'a' }, utf8 = Encoding.UTF8.GetBytes("été");
            byte[] original = BuildZip(new[] { new RawEntry(cp, "one"), new RawEntry(utf8, "two") { Flags = 0x800 } }, null);
            File.WriteAllBytes(zip, original);
#if NETFRAMEWORK
            Check.Throws<NotSupportedException>(() => ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("new")));
            Check.True(File.ReadAllBytes(zip).SequenceEqual(original), "the zip was changed");
#else
            ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("new"));
            List<RawEntry> back = ReadCentral(File.ReadAllBytes(zip), out _);
            Check.True(back.Any(e => e.Name.SequenceEqual(cp) && (e.Flags & 0x800) == 0), "the code-page name was rewritten");
            Check.True(back.Any(e => e.Name.SequenceEqual(utf8) && (e.Flags & 0x800) != 0), "the UTF-8 name was rewritten");
#endif
        }

        public void Zip_RewriteKeepsComments()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("notes.zip");
            var entry = new RawEntry(Encoding.ASCII.GetBytes("run.sh"), "#!/bin/sh") { Comment = Encoding.ASCII.GetBytes("entry note") };
            File.WriteAllBytes(zip, BuildZip(new[] { entry }, Encoding.ASCII.GetBytes("archive note")));
            ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("new"));
            List<RawEntry> back = ReadCentral(File.ReadAllBytes(zip), out byte[] comment);
            Check.Equal("archive note", Encoding.ASCII.GetString(comment));
            RawEntry run = back.Single(e => Encoding.ASCII.GetString(e.Name) == "run.sh");
            Check.Equal("entry note", Encoding.ASCII.GetString(run.Comment));
        }

        public void Zip_RewriteKeepsUnixPermissions()
        {
            // netstandard2.0 could not copy ExternalAttributes, so on .NET Framework the mode 0755 was stripped.
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("perm.zip");
            var entry = new RawEntry(Encoding.ASCII.GetBytes("run.sh"), "#!/bin/sh") { External = 0x81ED0000u, MadeBy = 0x031E };
            File.WriteAllBytes(zip, BuildZip(new[] { entry }, null));
            ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("new"));
            RawEntry run = ReadCentral(File.ReadAllBytes(zip), out _).Single(e => Encoding.ASCII.GetString(e.Name) == "run.sh");
            Check.Equal(0x81ED0000u, run.External);
            using var a = ZipFile.OpenRead(zip);
            using var r = new StreamReader(a.GetEntry("run.sh")!.Open());
            Check.Equal("#!/bin/sh", r.ReadToEnd());
        }

        public void Zip_RewriteOfUtf8NamesKeepsThemAsTheyWere()
        {
            // Guard: names stored as UTF-8 (the common case) come back byte for byte.
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("utf8.zip");
            byte[] name = Encoding.UTF8.GetBytes("caf\u00E9-\u65E5\u672C.txt");
            File.WriteAllBytes(zip, BuildZip(new[] { new RawEntry(name, "x") { Flags = 0x800 } }, null));
            ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("new"));
            ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("newer"));
            List<RawEntry> back = ReadCentral(File.ReadAllBytes(zip), out _);
            Check.Equal(2, back.Count);
            Check.True(back.Any(e => e.Name.SequenceEqual(name)), "the UTF-8 name changed");
        }

        // ---- ZipUtils.ExtractToDirectory: a Unix-made "a\" is a folder on Windows ----

        public void Zip_UnixMadeBackslashFolderExtracts()
        {
            if (!OnWindows) return;
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("bs.zip");
            File.WriteAllBytes(zip, BuildZip(new[]
            {
                new RawEntry(Encoding.ASCII.GetBytes("a\\"), "") { MadeBy = 0x031E },
                new RawEntry(Encoding.ASCII.GetBytes("a\\b.txt"), "B") { MadeBy = 0x031E },
            }, null));
            string dest = tmp.Combine("out");
            ZipUtils.ExtractToDirectory(zip, dest);
            Check.Equal("B", File.ReadAllText(Path.Combine(dest, "a", "b.txt")));
        }

        // ---- CreateFromDirectory: a folder holding only the archive being written is kept, as an empty folder ----

        public void Zip_FolderHoldingOnlyTheArchiveIsKept()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("sub");
            Directory.CreateDirectory(Path.Combine(src, "out"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            string zip = Path.Combine(src, "out", "o.zip");
            ZipUtils.CreateFromDirectory(src, zip);
            using var a = ZipFile.OpenRead(zip);
            Check.Equal("a.txt,out/", string.Join(",", a.Entries.Select(e => e.FullName).OrderBy(s => s, StringComparer.Ordinal)));
        }

#if NET8_0_OR_GREATER
        public void Tar_FolderHoldingOnlyTheArchiveIsKept()
        {
            using var tmp = new TempDirectory();
            string src = tmp.Combine("sub");
            Directory.CreateDirectory(Path.Combine(src, "out"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            string tar = Path.Combine(src, "out", "o.tar");
            TarUtils.CreateFromDirectory(src, tar);
            var names = new List<string>();
            using (var r = new System.Formats.Tar.TarReader(File.OpenRead(tar)))
                for (System.Formats.Tar.TarEntry? e; (e = r.GetNextEntry()) != null;) names.Add(e.Name);
            Check.Equal("a.txt,out/", string.Join(",", names.OrderBy(s => s, StringComparer.Ordinal)));
        }
#endif

        // ---- A minimal stored-entry zip, written and read by hand so names, flags, attributes and comments are exact ----

        private sealed class RawEntry
        {
            public RawEntry(byte[] name, string data) { Name = name; Data = Encoding.ASCII.GetBytes(data); }
            public byte[] Name;
            public byte[] Data;
            public byte[] Comment = new byte[0];
            public ushort Flags;
            public ushort MadeBy = 0x0014;
            public uint External;
        }

        private static byte[] BuildZip(IEnumerable<RawEntry> entries, byte[]? archiveComment)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            var offsets = new List<(RawEntry E, uint Offset, uint Crc)>();
            foreach (RawEntry e in entries)
            {
                uint crc = Crc32(e.Data);
                offsets.Add((e, (uint)ms.Position, crc));
                w.Write(0x04034b50u); w.Write((ushort)20); w.Write(e.Flags); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0x5021);
                w.Write(crc); w.Write((uint)e.Data.Length); w.Write((uint)e.Data.Length); w.Write((ushort)e.Name.Length); w.Write((ushort)0);
                w.Write(e.Name); w.Write(e.Data);
            }
            uint cdStart = (uint)ms.Position;
            foreach ((RawEntry e, uint offset, uint crc) in offsets)
            {
                w.Write(0x02014b50u); w.Write(e.MadeBy); w.Write((ushort)20); w.Write(e.Flags); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0x5021);
                w.Write(crc); w.Write((uint)e.Data.Length); w.Write((uint)e.Data.Length); w.Write((ushort)e.Name.Length); w.Write((ushort)0);
                w.Write((ushort)e.Comment.Length); w.Write((ushort)0); w.Write((ushort)0); w.Write(e.External); w.Write(offset);
                w.Write(e.Name); w.Write(e.Comment);
            }
            uint cdSize = (uint)ms.Position - cdStart;
            byte[] comment = archiveComment ?? new byte[0];
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)offsets.Count); w.Write((ushort)offsets.Count);
            w.Write(cdSize); w.Write(cdStart); w.Write((ushort)comment.Length); w.Write(comment);
            w.Flush();
            return ms.ToArray();
        }

        private static List<RawEntry> ReadCentral(byte[] zip, out byte[] archiveComment)
        {
            int eocd = zip.Length - 22;
            while (BitConverter.ToUInt32(zip, eocd) != 0x06054b50u) eocd--;
            int count = BitConverter.ToUInt16(zip, eocd + 10);
            int at = (int)BitConverter.ToUInt32(zip, eocd + 16);
            archiveComment = zip.Skip(eocd + 22).Take(BitConverter.ToUInt16(zip, eocd + 20)).ToArray();
            var list = new List<RawEntry>();
            for (int i = 0; i < count; i++)
            {
                Check.Equal(0x02014b50u, BitConverter.ToUInt32(zip, at));
                int nameLength = BitConverter.ToUInt16(zip, at + 28), extra = BitConverter.ToUInt16(zip, at + 30), commentLength = BitConverter.ToUInt16(zip, at + 32);
                list.Add(new RawEntry(zip.Skip(at + 46).Take(nameLength).ToArray(), "")
                {
                    MadeBy = BitConverter.ToUInt16(zip, at + 4),
                    Flags = BitConverter.ToUInt16(zip, at + 8),
                    External = BitConverter.ToUInt32(zip, at + 38),
                    Comment = zip.Skip(at + 46 + nameLength + extra).Take(commentLength).ToArray(),
                });
                at += 46 + nameLength + extra + commentLength;
            }
            return list;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
