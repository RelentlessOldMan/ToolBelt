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
    // Round 94 (IO, sixth pass): each regression failed on the code before its fix.
    public sealed class IOReview94Tests
    {
        // ---- TempDirectory: one file that cannot go does not keep the rest ----

        public void TempDirectory_ALockedFileLeavesTheRestDeleted()
        {
            string root, locked;
            var tmp = new TempDirectory();
            root = tmp.Path;
            locked = Path.Combine(root, "a.txt");
            File.WriteAllText(locked, "a");
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "c.txt"), "c");
            try
            {
                using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                    tmp.Dispose();
                if (Path.DirectorySeparatorChar == '\\') Check.True(File.Exists(locked), "the open file was deleted");
                Check.False(File.Exists(Path.Combine(root, "b.txt")), "b.txt was left after the locked a.txt");
                Check.False(Directory.Exists(Path.Combine(root, "sub")), "sub was left after the locked a.txt");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        // ---- Zip and tar: a file entry over an existing folder ----

        public void Zip_AFileOverAFolderThrowsANamedIOException()
        {
            using var tmp = new TempDirectory();
            string zip = Path.Combine(tmp.Path, "a.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(archive.CreateEntry("d").Open()))
                w.Write("x");
            string dest = Path.Combine(tmp.Path, "out");
            Directory.CreateDirectory(Path.Combine(dest, "d"));
            foreach (bool overwrite in new[] { true, false })
            {
                var ex = Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(zip, dest, overwrite));
                Check.True(ex.Message.Contains("'d'"), ex.Message);
                Check.True(Directory.Exists(Path.Combine(dest, "d")));
                Check.Equal(1, Directory.GetFileSystemEntries(dest).Length);
            }
        }

#if NET8_0_OR_GREATER
        public void Tar_AFileOverAFolderThrowsANamedIOException()
        {
            using var tmp = new TempDirectory();
            string tar = Path.Combine(tmp.Path, "a.tar");
            File.WriteAllBytes(tar, Cat(Header("d", '0', 1), Pad(new[] { (byte)'x' }), new byte[1024]));
            string dest = Path.Combine(tmp.Path, "out");
            Directory.CreateDirectory(Path.Combine(dest, "d"));
            foreach (bool overwrite in new[] { true, false })
            {
                var ex = Check.Throws<IOException>(() => TarUtils.ExtractToDirectory(tar, dest, overwrite));
                Check.True(ex.Message.Contains("'d'"), ex.Message);
                Check.Equal(1, Directory.GetFileSystemEntries(dest).Length);
            }
        }

        // ---- Tar's metadata guard ----

        // A GNU long name, then a PAX header: TarReader reads that PAX header as an ordinary entry and ignores its size, and
        // the guard applied the size to the next header, so a later 1.5 GB "x" header got through and TarReader allocated
        // gigabytes for a 5 KB archive.
        public void Tar_GnuThenPaxMetadataCannotSlipABigHeaderPast()
        {
            using var tmp = new TempDirectory();
            string tar = Path.Combine(tmp.Path, "bomb.tar");
            byte[] name = Encoding.ASCII.GetBytes("longname.txt\0");
            byte[] pax = Record("size", "1024");
            File.WriteAllBytes(tar, Cat(Header("././@LongLink", 'L', name.Length), Pad(name), Header("x1", 'x', pax.Length), Pad(pax),
                Header("f.txt", '0', 0), Header("evil", 'x', 1_500_000_000L), new byte[2048]));
            long before = GC.GetAllocatedBytesForCurrentThread();
            Check.Throws<InvalidDataException>(() => TarUtils.ListEntries(tar));
            Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, Path.Combine(tmp.Path, "out")));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 64L << 20, $"allocated {allocated:N0} bytes");
        }

        public void Tar_GnuLongNamesStillRead()
        {
            using var tmp = new TempDirectory();
            string tar = Path.Combine(tmp.Path, "long.tar");
            string longName = new string('n', 150) + ".txt";
            byte[] name = Encoding.ASCII.GetBytes(longName + "\0");
            byte[] target = Encoding.ASCII.GetBytes(new string('t', 150) + "\0");
            File.WriteAllBytes(tar, Cat(Header("././@LongLink", 'L', name.Length), Pad(name), Header("short", '0', 2), Pad(new byte[] { 1, 2 }),
                Header("././@LongLink", 'K', target.Length), Pad(target), Header("././@LongLink", 'L', name.Length), Pad(name),
                Header("ln", '2', 0), new byte[1024]));
            IReadOnlyList<string> entries = TarUtils.ListEntries(tar);
            Check.Equal(2, entries.Count);
            Check.Equal(longName, entries[0]);
            Check.Equal(longName, entries[1]);
        }

        // TarReader reads "size=0000000000000000005" as 5; the guard counted the leading zeros and refused it.
        public void Tar_APaxSizeWithLeadingZerosReads()
        {
            using var tmp = new TempDirectory();
            string tar = Path.Combine(tmp.Path, "zeros.tar");
            byte[] pax = Record("size", "0000000000000000005");
            File.WriteAllBytes(tar, Cat(Header("pax", 'x', pax.Length), Pad(pax), Header("f.txt", '0', 0), Pad(Encoding.ASCII.GetBytes("hello")),
                new byte[1024]));
            Check.Equal("f.txt", string.Join(",", TarUtils.ListEntries(tar)));
            string dest = Path.Combine(tmp.Path, "out");
            TarUtils.ExtractToDirectory(tar, dest);
            Check.Equal("hello", File.ReadAllText(Path.Combine(dest, "f.txt")));
        }

        private static byte[] Header(string name, char type, long size)
        {
            var h = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(h, 0);
            Encoding.ASCII.GetBytes("0000644\0").CopyTo(h, 100);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 108);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 116);
            Encoding.ASCII.GetBytes(Convert.ToString(size, 8).PadLeft(11, '0') + "\0").CopyTo(h, 124);
            Encoding.ASCII.GetBytes("00000000000\0").CopyTo(h, 136);
            h[156] = (byte)type;
            Encoding.ASCII.GetBytes("ustar\0" + "00").CopyTo(h, 257);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            int sum = h.Sum(b => b);
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
            return h;
        }

        private static byte[] Pad(byte[] d)
        {
            var r = new byte[(d.Length + 511) / 512 * 512];
            d.CopyTo(r, 0);
            return r;
        }

        // A PAX record, "<length> <key>=<value>\n", whose length counts its own digits.
        private static byte[] Record(string key, string value)
        {
            string body = " " + key + "=" + value + "\n";
            int length = body.Length + 1;
            while (length != body.Length + length.ToString().Length) length++;
            return Encoding.ASCII.GetBytes(length + body);
        }

        private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
#endif
    }
}
