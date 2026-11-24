using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>
    /// Round 212 review regressions (IO, thirteenth pass): each failed on the code before its fix.
    /// </summary>
    public sealed class IOReview212Tests
    {
        private const string H = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

#if NET8_0_OR_GREATER
        // GNU tar's PAX sparse forms (--sparse --format=posix) are regular files with GNU.sparse.* records; the map and
        // the packed data were extracted as one file, named GNUSparseFile.0/real.bin, though sparse files are refused.
        public void Tar_PaxSparseFile_IsRejected()
        {
            var forms = new[]
            {
                new Dictionary<string, string> { ["GNU.sparse.major"] = "1", ["GNU.sparse.minor"] = "0", ["GNU.sparse.name"] = "real.bin", ["GNU.sparse.realsize"] = "4096" },
                new Dictionary<string, string> { ["GNU.sparse.size"] = "4096", ["GNU.sparse.numblocks"] = "1", ["GNU.sparse.map"] = "0,5" },
            };
            foreach (var attributes in forms)
            {
                using var tmp = new TempDirectory();
                string tar = tmp.Combine("s.tar"), dst = tmp.Combine("out");
                byte[] map = new byte[512];
                Encoding.ASCII.GetBytes("1\n0\n5\n").CopyTo(map, 0);
                byte[] data = attributes.ContainsKey("GNU.sparse.major") ? map.Concat(Encoding.ASCII.GetBytes("hello")).ToArray() : Encoding.ASCII.GetBytes("hello");
                using (var fs = File.Create(tar))
                using (var w = new System.Formats.Tar.TarWriter(fs, System.Formats.Tar.TarEntryFormat.Pax))
                {
                    string name = attributes.ContainsKey("GNU.sparse.major") ? "GNUSparseFile.0/real.bin" : "real.bin";
                    w.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name, attributes) { DataStream = new MemoryStream(data) });
                }
                Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, dst));
                Check.Equal(0, Directory.Exists(dst) ? Directory.GetFileSystemEntries(dst, "*", SearchOption.AllDirectories).Length : 0);
            }
            // Other PAX records are not sparse.
            using (var tmp = new TempDirectory())
            {
                string tar = tmp.Combine("p.tar"), dst = tmp.Combine("out");
                using (var fs = File.Create(tar))
                using (var w = new System.Formats.Tar.TarWriter(fs, System.Formats.Tar.TarEntryFormat.Pax))
                    w.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "a.txt",
                        new Dictionary<string, string> { ["comment"] = "GNU.sparse.no" }) { DataStream = new MemoryStream(Encoding.ASCII.GetBytes("hi")) });
                TarUtils.ExtractToDirectory(tar, dst);
                Check.Equal("hi", File.ReadAllText(Path.Combine(dst, "a.txt")));
            }
        }
#endif

        // Only one leading "./" was dropped: "././b" was kept as "./b", which its own text read back as "b", and
        // {"./a", "././a"} wrote two lines that Parse refused as duplicates. Differential: a manifest's text reads back
        // as the same manifest, over random names.
        public void Manifest_RepeatedDotSlash_RoundTrips()
        {
            var rng = new Random(212);
            string[] parts = { "./", "a", "b", ".", "/", "x/", "./a" };
            int built = 0;
            for (int trial = 0; trial < 3000; trial++)
            {
                var pairs = new List<KeyValuePair<string, string>>();
                int n = 1 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    var sb = new StringBuilder();
                    int len = 1 + rng.Next(5);
                    for (int j = 0; j < len; j++) sb.Append(parts[rng.Next(parts.Length)]);
                    pairs.Add(new KeyValuePair<string, string>(sb.ToString(), H));
                }
                ChecksumManifest m;
                try { m = new ChecksumManifest(pairs); }
                catch (ArgumentException) { continue; }
                built++;
                ChecksumManifest back = ChecksumManifest.Parse(m.ToText());
                Check.Equal(string.Join("|", m.Entries.Keys), string.Join("|", back.Entries.Keys), string.Join("|", pairs.Select(p => p.Key)));
                Check.True(ChecksumManifest.Compare(m, back).IsMatch);
            }
            Check.True(built > 1000, built.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Check.Equal("b", new ChecksumManifest(new[] { new KeyValuePair<string, string>("././b", H) }).Entries.Keys.Single());
            Check.Throws<ArgumentException>(() => new ChecksumManifest(new[] { new KeyValuePair<string, string>("./a", H), new KeyValuePair<string, string>("././a", H) }));
        }

        // A name with an unpaired surrogate was kept and saved as U+FFFD, so the manifest never verified its own tree.
        public void Manifest_LoneSurrogateName_IsRefused()
        {
            using var tmp = new TempDirectory();
            string dir = tmp.Combine("d");
            Directory.CreateDirectory(dir);
            string bad = Path.Combine(dir, "x\uD800y.txt");
            File.WriteAllText(bad, "1");
            File.WriteAllText(Path.Combine(dir, "ok.txt"), "2");
            var ex = Check.Throws<IOException>(() => ChecksumManifest.Create(dir));
            Check.True(ex.Message.Contains("surrogate"), ex.Message);
            var errors = new List<string>();
            ChecksumManifest m = ChecksumManifest.Create(dir, onError: (f, _) => errors.Add(f));
            Check.Equal("ok.txt", m.Entries.Keys.Single());
            Check.Equal(1, errors.Count);
            Check.Equal(bad, errors[0]);
            var arg = Check.Throws<ArgumentException>(() => new ChecksumManifest(new[] { new KeyValuePair<string, string>("x\uDC00", H) }));
            Check.Equal("entries", arg.ParamName);
            Check.Throws<FormatException>(() => ChecksumManifest.Parse(H + "  a\uD800b\n"));
            // A pair is a name, not two halves.
            Check.Equal(1, new ChecksumManifest(new[] { new KeyValuePair<string, string>("a😀", H) }).Count);
        }

        // "app.log." and "app.log " exist as app.log, but the copies were built from the raw text ("app.log..1"), so the
        // rotation threw FileNotFoundException and rotated nothing.
        public void ShiftNumbered_TrailingDotOrSpace_RotatesTheFileFound()
        {
            if (Path.DirectorySeparatorChar != '\\') return;   // Windows drops trailing dots and spaces
            foreach (string tail in new[] { ".", " ", ". ", ".." })
            {
                using var tmp = new TempDirectory();
                string log = tmp.Combine("app.log");
                File.WriteAllText(log, "now");
                File.WriteAllText(log + ".1", "one");
                Check.True(FileRetention.ShiftNumbered(log + tail, 3), tail);
                Check.False(File.Exists(log), tail);
                Check.Equal("now", File.ReadAllText(log + ".1"));
                Check.Equal("one", File.ReadAllText(log + ".2"));
                Check.Equal(2, Directory.GetFiles(tmp.Path).Length);
            }
        }

        // With two entries of one name, ReadEntryText returned the first while extraction left the last: a check read one
        // content and extraction wrote another.
        public void Zip_DuplicateNames_ReadAgreesWithExtraction()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("d.zip"), dst = tmp.Combine("out");
            using (var fs = File.Create(zip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string text in new[] { "first", "middle", "second" })
                    using (var w = new StreamWriter(archive.CreateEntry("a.txt").Open())) w.Write(text);
                using (var w = new StreamWriter(archive.CreateEntry("b.txt").Open())) w.Write("b");
            }
            ZipUtils.ExtractToDirectory(zip, dst, overwrite: true);
            string extracted = File.ReadAllText(Path.Combine(dst, "a.txt"));
            Check.Equal("second", extracted);
            Check.Equal(extracted, ZipUtils.ReadEntryText(zip, "a.txt"));
            Check.Equal("second", Encoding.UTF8.GetString(ZipUtils.ReadEntryBytes(zip, "a.txt")!));
            Check.Equal("b", ZipUtils.ReadEntryText(zip, "b.txt"));
            Check.True(ZipUtils.ReadEntryBytes(zip, "A.TXT") is null);
        }

        // Differential: a mirror that replays every released batch (renames move its copy, Created and Changed re-read the
        // disk, Deleted drops) ends equal to the disk, over random event sequences on four names with takes part way
        // through (some changes not ready). First-seen order put "Created a" before the "Renamed a -> c" that vacated a,
        // a swap could not be replayed in any order of renames, and "R a->c; M c" reported no change to c's content.
        public void Coalescer_ReleasedBatches_ReplayToTheDisk()
        {
            var rng = new Random(2121);
            string[] names = { "a", "b", "c", "d" };
            int id = 0, failures = 0;
            string? first = null;
            for (int trial = 0; trial < 60000; trial++)
            {
                var disk = new Dictionary<string, int>();
                foreach (string n in names) if (rng.Next(2) == 0) disk[n] = ++id;
                var mirror = new Dictionary<string, int>(disk);
                var co = new FileChangeCoalescer(ignoreCase: false);
                var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var log = new StringBuilder("[" + string.Join(",", disk.Keys.OrderBy(k => k, StringComparer.Ordinal)) + "]");
                bool ok = true;
                void Emit(FileChange c) { t = t.AddSeconds(1); co.Add(c, t); log.Append(' ').Append(c); }
                void Replay(IReadOnlyList<FileChange> batch)
                {
                    log.Append(" => {").Append(string.Join(" | ", batch)).Append('}');
                    foreach (FileChange c in batch)
                    {
                        switch (c.Kind)
                        {
                            case FileChangeKind.Created:
                            case FileChangeKind.Changed:
                                if (disk.TryGetValue(c.FullPath, out int v)) mirror[c.FullPath] = v; else ok = false;
                                break;
                            case FileChangeKind.Deleted: mirror.Remove(c.FullPath); break;
                            case FileChangeKind.Renamed:
                                if (mirror.TryGetValue(c.OldFullPath!, out int m)) { mirror.Remove(c.OldFullPath!); mirror[c.FullPath] = m; } else ok = false;
                                break;
                        }
                    }
                }
                int events = 1 + rng.Next(7);
                for (int e = 0; e < events; e++)
                {
                    string p1 = names[rng.Next(names.Length)], p2 = names[rng.Next(names.Length)];
                    switch (rng.Next(4))
                    {
                        case 0: if (!disk.ContainsKey(p1)) { disk[p1] = ++id; Emit(new FileChange(FileChangeKind.Created, p1)); } break;
                        case 1: if (disk.ContainsKey(p1)) { disk[p1] = ++id; Emit(new FileChange(FileChangeKind.Changed, p1)); } break;
                        case 2: if (disk.Remove(p1)) Emit(new FileChange(FileChangeKind.Deleted, p1)); break;
                        default:
                            if (p1 == p2 || !disk.ContainsKey(p1)) break;
                            if (disk.Remove(p2)) Emit(new FileChange(FileChangeKind.Deleted, p2));   // a rename over a file
                            disk[p2] = disk[p1];
                            disk.Remove(p1);
                            Emit(new FileChange(FileChangeKind.Renamed, p2, p1));
                            break;
                    }
                    if (rng.Next(4) == 0) Replay(co.TakeQuiet(t, TimeSpan.Zero, _ => rng.Next(3) != 0));
                }
                Replay(co.TakeAll());
                ok &= mirror.Count == disk.Count && mirror.All(kv => disk.TryGetValue(kv.Key, out int v) && v == kv.Value);
                if (!ok) { failures++; first ??= log.ToString(); }
            }
            Check.Equal(0, failures, first ?? "");
        }

        public void Coalescer_SaveWithBackupAndSwap_AreReplayable()
        {
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string Run(params FileChange[] events)
            {
                var co = new FileChangeCoalescer(ignoreCase: false);
                foreach (FileChange e in events) co.Add(e, t = t.AddSeconds(1));
                return string.Join(" | ", co.TakeAll());
            }
            // Write b, back a up to c, promote b to a.
            Check.Equal("Renamed a -> c | Created a", Run(new FileChange(FileChangeKind.Created, "b"),
                new FileChange(FileChangeKind.Renamed, "c", "a"), new FileChange(FileChangeKind.Renamed, "a", "b")));
            // A swap through a temporary name: no order of two renames replays it.
            Check.Equal("Deleted c | Renamed a -> c | Created a", Run(new FileChange(FileChangeKind.Renamed, "b", "c"),
                new FileChange(FileChangeKind.Renamed, "c", "a"), new FileChange(FileChangeKind.Renamed, "a", "b")));
            Check.Equal("Renamed a -> c | Changed c", Run(new FileChange(FileChangeKind.Renamed, "c", "a"), new FileChange(FileChangeKind.Changed, "c")));
            Check.Equal("Renamed a -> c | Changed c", Run(new FileChange(FileChangeKind.Changed, "a"), new FileChange(FileChangeKind.Renamed, "c", "a")));
            // A change that must follow a rename still pending stays pending with it.
            var co2 = new FileChangeCoalescer(ignoreCase: false);
            co2.Add(new FileChange(FileChangeKind.Renamed, "c", "a"), t = t.AddSeconds(1));
            co2.Add(new FileChange(FileChangeKind.Created, "a"), t = t.AddSeconds(1));
            Check.Equal(0, co2.TakeQuiet(t, TimeSpan.Zero, c => c.Kind != FileChangeKind.Renamed).Count);
            Check.Equal("Renamed a -> c | Created a", string.Join(" | ", co2.TakeAll()));
        }

#if NET8_0_OR_GREATER
        // Two tar entries for one directory kept the first one's time; files, and GNU tar, take the last.
        public void Tar_RepeatedDirectoryEntry_LastTimeWins()
        {
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("d.tar"), dst = tmp.Combine("out");
            var t1 = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var t2 = new DateTimeOffset(2002, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (var fs = File.Create(tar))
            using (var w = new System.Formats.Tar.TarWriter(fs, System.Formats.Tar.TarEntryFormat.Pax))
            {
                w.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Directory, "d/") { ModificationTime = t1 });
                w.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Directory, "e/") { ModificationTime = t1 });
                w.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Directory, "d") { ModificationTime = t2 });
            }
            TarUtils.ExtractToDirectory(tar, dst);
            Check.Equal(t2.UtcDateTime, Directory.GetLastWriteTimeUtc(Path.Combine(dst, "d")));
            Check.Equal(t1.UtcDateTime, Directory.GetLastWriteTimeUtc(Path.Combine(dst, "e")));
        }
#endif

        // The same in a zip.
        public void Zip_RepeatedDirectoryEntry_LastTimeWins()
        {
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("d.zip"), dst = tmp.Combine("out");
            var t1 = new DateTime(2001, 1, 1, 12, 0, 0);
            var t2 = new DateTime(2002, 1, 1, 12, 0, 0);
            using (var fs = File.Create(zip))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                archive.CreateEntry("d/").LastWriteTime = t1;
                archive.CreateEntry("e/").LastWriteTime = t1;
                archive.CreateEntry("d/").LastWriteTime = t2;
            }
            ZipUtils.ExtractToDirectory(zip, dst, overwrite: true);
            Check.Equal(t2, Directory.GetLastWriteTime(Path.Combine(dst, "d")));
            Check.Equal(t1, Directory.GetLastWriteTime(Path.Combine(dst, "e")));
        }

        // A read-only copy falling off the end threw UnauthorizedAccessException, and nothing rotated.
        public void ShiftNumbered_ReadOnlyOldestCopy_IsDeleted()
        {
            using var tmp = new TempDirectory();
            string log = tmp.Combine("app.log");
            File.WriteAllText(log, "now");
            File.WriteAllText(log + ".1", "one");
            File.WriteAllText(log + ".2", "two");
            File.SetAttributes(log + ".2", FileAttributes.ReadOnly);
            Check.True(FileRetention.ShiftNumbered(log, 2));
            Check.Equal("now", File.ReadAllText(log + ".1"));
            Check.Equal("one", File.ReadAllText(log + ".2"));
            Check.False(File.Exists(log));
            Check.Equal(2, Directory.GetFiles(tmp.Path).Length);
        }
    }
}
