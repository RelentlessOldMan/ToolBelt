using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    // Round 143 (IO, ninth pass): each regression failed on the code before its fix.
    public sealed class IOReview143Tests
    {
        private static bool OnWindows => Path.DirectorySeparatorChar == '\\';

        // ---- CsvBinder: whitespace that is the value ----

        public sealed class Row
        {
            public char? C { get; set; }
            public Uri? U { get; set; }
        }

        // A char? holding ' ' or a tab was written as itself but read back as null (the blank-means-null trim ran first).
        public void CsvBinder_WhitespaceThatIsTheValueRoundTrips()
        {
            var b = new CsvBinder<Row>(new[] { "C", "U" });
            foreach (char c in new[] { ' ', '\t' })
            {
                Row back = b.Bind(b.ToFields(new Row { C = c }));
                Check.Equal((char?)c, back.C);
            }
        }

        // A relative Uri " rel " came back as "rel".
        public void CsvBinder_ARelativeUriKeepsItsSpaces()
        {
            var b = new CsvBinder<Row>(new[] { "C", "U" });
            var rel = new Uri(" rel ", UriKind.Relative);
            Row r = b.Bind(b.ToFields(new Row { U = rel }));
            Check.Equal(rel, r.U);
            Check.Equal(" rel ", r.U!.OriginalString);
            // Guards: an empty field is still null, a blank Uri field too, and an absolute Uri trims itself.
            Row empty = b.Bind(new[] { "", "  " });
            Check.True(empty.C == null && empty.U == null);
            Check.Equal(new Uri("http://x/"), b.Bind(new[] { "", " http://x/ " }).U);
        }

        // ---- Extraction: no folder for an archive that cannot be read ----

        public void Extract_AMissingOrCorruptArchiveCreatesNoFolder()
        {
            using var tmp = new TempDirectory();
            string bad = tmp.Combine("bad.bin");
            File.WriteAllText(bad, "not an archive");
            Check.Throws<FileNotFoundException>(() => ZipUtils.ExtractToDirectory(tmp.Combine("nope.zip"), tmp.Combine("z1")));
            Check.False(Directory.Exists(tmp.Combine("z1")), "zip, missing");
            Check.Throws<InvalidDataException>(() => ZipUtils.ExtractToDirectory(bad, tmp.Combine(Path.Combine("z2", "deep"))));
            Check.False(Directory.Exists(tmp.Combine("z2")), "zip, corrupt");
#if NET8_0_OR_GREATER
            Check.Throws<FileNotFoundException>(() => TarUtils.ExtractToDirectory(tmp.Combine("nope.tar"), tmp.Combine("t1")));
            Check.False(Directory.Exists(tmp.Combine("t1")), "tar, missing");
            File.WriteAllBytes(bad, Encoding.ASCII.GetBytes(new string('x', 1024)));   // a header with a bad checksum
            Check.Throws<Exception>(() => TarUtils.ExtractToDirectory(bad, tmp.Combine(Path.Combine("t2", "deep")), gzip: false));
            Check.False(Directory.Exists(tmp.Combine("t2")), "tar, corrupt");
#endif
        }

        // ---- Overwrites: the target's attributes, and a failed replace ----

        private static string Zip(TempDirectory tmp, string content)
        {
            string zip = tmp.Combine("in.zip");
            using (var fs = File.Create(zip))
            using (var a = new ZipArchive(fs, ZipArchiveMode.Create))
            using (var w = new StreamWriter(a.CreateEntry("a.txt").Open()))
                w.Write(content);
            return zip;
        }

        private static string Source(TempDirectory tmp, string content)
        {
            string src = tmp.Combine("src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), content);
            return src;
        }

#if NET8_0_OR_GREATER
        private static string Tar(TempDirectory tmp, string src)
        {
            string tar = tmp.Combine("in.tar");
            TarUtils.CreateFromDirectory(src, tar, gzip: false);
            return tar;
        }
#endif

        // The overwrites one by one: zip extraction, DirectoryUtils.Copy, and tar extraction (net8).
        private static void EachOverwrite(TempDirectory tmp, string dest, Action<string, Action> run)
        {
            string src = Source(tmp, "new");
            string zip = Zip(tmp, "new");
            run("zip", () => ZipUtils.ExtractToDirectory(zip, dest, overwrite: true));
            run("copy", () =>
            {
                CopyResult r = DirectoryUtils.Copy(src, dest, new CopyOptions { OnError = (p, e) => throw e });
                Check.Equal(0, r.Errors);
            });
#if NET8_0_OR_GREATER
            string tar = Tar(tmp, src);
            run("tar", () => TarUtils.ExtractToDirectory(tar, dest, overwrite: true, gzip: false));
#endif
        }

        // Clearing ReadOnly came before the replace, so a replace that failed (the target held open) left the old file
        // unprotected.
        public void Overwrite_AFailedReplaceKeepsReadOnly()
        {
            if (!OnWindows) return;   // an open file does not block a replace elsewhere
            using var tmp = new TempDirectory();
            string dest = tmp.Combine("dest");
            Directory.CreateDirectory(dest);
            string target = Path.Combine(dest, "a.txt");
            try
            {
                EachOverwrite(tmp, dest, (what, run) =>
                {
                    if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                    File.WriteAllText(target, "old");
                    File.SetAttributes(target, FileAttributes.ReadOnly);
                    using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        Check.Throws<Exception>(run);
                    }
                    Check.Equal("old", File.ReadAllText(target));
                    Check.True((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0, what + ": ReadOnly was cleared");
                });
            }
            finally { if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal); }
        }

        // File.Move(overwrite) dropped the target's Hidden attribute, where File.Replace (netstandard2.0) kept it, and
        // where AtomicFile and JsonFile keep it.
        public void Overwrite_KeepsTheTargetsHiddenAttribute()
        {
            if (!OnWindows) return;
            using var tmp = new TempDirectory();
            string dest = tmp.Combine("dest");
            Directory.CreateDirectory(dest);
            string target = Path.Combine(dest, "a.txt");
            EachOverwrite(tmp, dest, (what, run) =>
            {
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                File.WriteAllText(target, "old");
                File.SetAttributes(target, FileAttributes.Hidden);
                run();
                Check.Equal("new", File.ReadAllText(target));
                Check.True((File.GetAttributes(target) & FileAttributes.Hidden) != 0, what + ": Hidden was dropped");
            });
        }

        // A target another process has open with FileShare.Delete was replaced on netstandard2.0 but refused on net8.
        public void Overwrite_ReplacesATargetOpenWithShareDelete()
        {
            if (!OnWindows) return;
            using var tmp = new TempDirectory();
            string dest = tmp.Combine("dest");
            Directory.CreateDirectory(dest);
            string target = Path.Combine(dest, "a.txt");
            EachOverwrite(tmp, dest, (what, run) =>
            {
                File.WriteAllText(target, "old");
                using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    run();
                }
                Check.Equal("new", File.ReadAllText(target));
            });
        }
    }
}
