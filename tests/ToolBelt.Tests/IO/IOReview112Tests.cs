using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;
#if NET8_0_OR_GREATER
using System.Formats.Tar;
#endif

namespace ToolBelt.Tests.IO
{
    // Round 112 (IO, seventh pass): each regression failed on the code before its fix.
    public sealed class IOReview112Tests
    {
        // ---- CsvDialect: a decimal separator that numbers already hold ----

        public void CsvDialect_ASeparatorNumbersHoldIsRejected()
        {
            // With '1', 1.5 formatted as "115"; with '-', -2.5 as "-2-5"; with 'E', 1.5e-7 as "1E5E-07": none read back.
            foreach (char c in new[] { '-', '+', 'E', 'e', '1', '5', 'I', 'N', 'a', ' ', '\t', '"', '\n', '\r', '\0', '\uD800' })
            {
                var ex = Check.Throws<ArgumentException>(() => new CsvDialect(';', c), ((int)c).ToString());
                Check.Equal("decimalSeparator", ex.ParamName);
            }
        }

        public void CsvDialect_AllowedSeparatorsRoundTrip()
        {
            // Guard: the separators still allowed write numbers that read back, for every kind of double.
            var rng = new Random(112);
            var values = new List<double> { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, -0.0, 1.5e-7, 1e300, -2.5, double.MaxValue };
            for (int i = 0; i < 2000; i++) values.Add(BitConverter.Int64BitsToDouble((long)(rng.NextDouble() * long.MaxValue) * (rng.Next(2) * 2 - 1)));
            foreach (char c in new[] { '.', ',', '\'', '_', '·', '/' })
            {
                var d = new CsvDialect(';', c);
                foreach (double v in values)
                {
                    string s = d.FormatNumber(v);
                    Check.True(d.TryParseNumber(s, out double back), $"'{c}' {s}");
                    if (double.IsNaN(v)) Check.True(double.IsNaN(back), $"'{c}' {s}");
                    else Check.Equal(BitConverter.DoubleToInt64Bits(v), BitConverter.DoubleToInt64Bits(back), $"'{c}' {s}");
                }
            }
        }

        // ---- DirectoryUtils.Copy: an undefined overwrite policy ----

        public void DirectoryCopy_AnUndefinedPolicyThrowsAndOverwritesNothing()
        {
            // (OverwritePolicy)42 fell to "replace" and overwrote every existing file.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), dst = tmp.Combine("dst");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(src, "a.txt"), "new");
            File.WriteAllText(Path.Combine(dst, "a.txt"), "old");
            foreach (int policy in new[] { 42, -1, 4 })
            {
                var ex = Check.Throws<ArgumentOutOfRangeException>(
                    () => DirectoryUtils.Copy(src, dst, new CopyOptions { Overwrite = (OverwritePolicy)policy }));
                Check.Equal("options", ex.ParamName);
                Check.Equal("old", File.ReadAllText(Path.Combine(dst, "a.txt")));
            }
        }

        // ---- ZipUtils.AddOrReplaceEntry: a valid zip holding an entry with an empty name ----

        public void Zip_AddOrReplaceKeepsAnEmptyNamedEntry()
        {
            // Copying the old entries called CreateEntry(""), which threw ArgumentException "entryName" for the caller's
            // "new.txt", and the in-memory fallback was never tried.
            using var tmp = new TempDirectory();
            string zip = tmp.Combine("e.zip");
            File.WriteAllBytes(zip, EmptyNamedZip());
            using (ZipArchive before = ZipFile.OpenRead(zip))
                Check.Equal("", before.Entries.Single().FullName);
            ZipUtils.AddOrReplaceEntry(zip, "new.txt", Encoding.ASCII.GetBytes("hi"));
            using ZipArchive after = ZipFile.OpenRead(zip);
            var names = after.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check.True(names.SequenceEqual(new[] { "", "new.txt" }), string.Join("|", names));
            using var r = new StreamReader(after.GetEntry("new.txt")!.Open());
            Check.Equal("hi", r.ReadToEnd());
            Check.Equal(1, Directory.GetFiles(tmp.Path).Length);   // no temp file left
        }

        // One stored, empty entry whose name has length 0 (CRC of no bytes is 0), as other zip tools can write.
        private static byte[] EmptyNamedZip()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(0x04034b50u); w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)0);   // local header, stored
            w.Write((ushort)0); w.Write((ushort)0x21); w.Write(0u); w.Write(0u); w.Write(0u);  // time, date, crc, sizes
            w.Write((ushort)0); w.Write((ushort)0);                                            // name and extra lengths
            long central = ms.Position;
            w.Write(0x02014b50u); w.Write((ushort)20); w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)0); w.Write((ushort)0x21); w.Write(0u); w.Write(0u); w.Write(0u);
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
            w.Write(0u); w.Write(0u);                                                          // attributes, offset 0
            long size = ms.Position - central;
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
            w.Write((uint)size); w.Write((uint)central); w.Write((ushort)0);
            w.Flush();
            return ms.ToArray();
        }

        // ---- CsvBinder: a field of spaces for a nullable property ----

        private sealed class Row
        {
            public int? Count { get; set; }
            public double? Value { get; set; }
            public Uri? Link { get; set; }
            public string? Name { get; set; }
        }

        public void CsvBinder_ABlankFieldIsNullForANullableProperty()
        {
            // " 5 " was read as 5 but "  " was an error, not null.
            var binder = new CsvBinder<Row>(new[] { "Count", "Value", "Link", "Name" });
            Row r = binder.Bind(new[] { "  ", " \t", "   ", "  " });
            Check.True(r.Count is null);
            Check.True(r.Value is null);
            Check.True(r.Link is null);
            Check.Equal("  ", r.Name);   // a string keeps its text
            Row s = binder.Bind(new[] { " 5 ", " 2.5 ", "", "" });
            Check.Equal(5, s.Count!.Value);
            Check.Equal(2.5, s.Value!.Value);
        }

#if NET8_0_OR_GREATER
        // ---- TarUtils: pre-POSIX directories and Windows file modes ----

        public void TarExtract_AV7DirectoryEntryIsADirectory()
        {
            // An old-style tar marks a directory as an empty file named "d/"; it was refused as "a file named like a
            // directory", so nothing extracted. GNU tar and Python read it as a directory.
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("v7.tar"), dst = tmp.Combine("out");
            var ms = new MemoryStream();
            ms.Write(V7Header("d/", 0));
            ms.Write(V7Header("d/f.txt", 2));
            var data = new byte[512];
            data[0] = (byte)'h'; data[1] = (byte)'i';
            ms.Write(data);
            ms.Write(new byte[1024]);
            File.WriteAllBytes(tar, ms.ToArray());
            TarUtils.ExtractToDirectory(tar, dst);
            Check.True(Directory.Exists(Path.Combine(dst, "d")));
            Check.Equal("hi", File.ReadAllText(Path.Combine(dst, "d", "f.txt")));
            Check.Equal(1, Directory.GetFileSystemEntries(dst).Length);
        }

        public void TarExtract_AV7FileWithDataNamedLikeADirectoryIsStillInvalid()
        {
            // Guard: only an empty old-style entry is a directory; one carrying data is still refused.
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("v7.tar"), dst = tmp.Combine("out");
            var ms = new MemoryStream();
            ms.Write(V7Header("d/", 2));
            ms.Write(new byte[512]);
            ms.Write(new byte[1024]);
            File.WriteAllBytes(tar, ms.ToArray());
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, dst));
        }

        // A V7 header: no ustar magic, typeflag '\0'.
        private static byte[] V7Header(string name, int size)
        {
            var h = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(h, 0);
            Encoding.ASCII.GetBytes("0000644\0").CopyTo(h, 100);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 108);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 116);
            Encoding.ASCII.GetBytes(Convert.ToString(size, 8).PadLeft(11, '0') + "\0").CopyTo(h, 124);
            Encoding.ASCII.GetBytes("14614574000\0").CopyTo(h, 136);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            int sum = h.Sum(b => b);
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
            return h;
        }

        public void TarCreate_WindowsFilesAreNotExecutable()
        {
            // TarWriter gave every Windows file mode 0755, so each came out executable on Unix.
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), tar = tmp.Combine("a.tar");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            Directory.CreateDirectory(Path.Combine(src, "empty"));
            File.WriteAllText(Path.Combine(src, "sub", "a.txt"), "abc");
            var when = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(src, "sub", "a.txt"), when);
            TarUtils.CreateFromDirectory(src, tar);
            var modes = new Dictionary<string, (UnixFileMode Mode, TarEntryType Type, DateTimeOffset Time, string Text)>();
            using (var fs = File.OpenRead(tar))
            using (var reader = new TarReader(fs))
            {
                TarEntry? e;
                while ((e = reader.GetNextEntry()) != null)
                    modes[e.Name] = (e.Mode, e.EntryType, e.ModificationTime, e.DataStream is null ? "" : new StreamReader(e.DataStream).ReadToEnd());
            }
            var file = modes["sub/a.txt"];
            Check.Equal(Convert.ToString(0b110_100_100, 8), Convert.ToString((int)file.Mode, 8));
            Check.Equal("abc", file.Text);
            Check.Equal(when, file.Time.UtcDateTime);
            Check.Equal(Convert.ToString(0b111_101_101, 8), Convert.ToString((int)modes["empty/"].Mode, 8));   // folders stay searchable
            string dst = tmp.Combine("out");
            TarUtils.ExtractToDirectory(tar, dst);
            Check.Equal("abc", File.ReadAllText(Path.Combine(dst, "sub", "a.txt")));
        }
#endif
    }
}
