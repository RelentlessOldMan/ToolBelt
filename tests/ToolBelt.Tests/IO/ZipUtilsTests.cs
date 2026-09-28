using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class ZipUtilsTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-zip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void CreateListAndExtractRoundTrip()
        {
            string work = TempDir();
            try
            {
                string src = Path.Combine(work, "src");
                Directory.CreateDirectory(Path.Combine(src, "sub"));
                File.WriteAllText(Path.Combine(src, "a.txt"), "alpha");
                File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "beta");

                string zip = Path.Combine(work, "out.zip");
                ZipUtils.CreateFromDirectory(src, zip);

                var entries = ZipUtils.ListEntries(zip);
                Check.True(entries.Contains("a.txt"));
                Check.True(entries.Contains("sub/b.txt"));

                string dest = Path.Combine(work, "dest");
                ZipUtils.ExtractToDirectory(zip, dest);
                Check.Equal("alpha", File.ReadAllText(Path.Combine(dest, "a.txt")));
                Check.Equal("beta", File.ReadAllText(Path.Combine(dest, "sub", "b.txt")));
            }
            finally { Directory.Delete(work, true); }
        }

        public void FilterExcludesEntries()
        {
            string work = TempDir();
            try
            {
                string src = Path.Combine(work, "src");
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "keep.txt"), "1");
                File.WriteAllText(Path.Combine(src, "skip.log"), "2");

                string zip = Path.Combine(work, "out.zip");
                ZipUtils.CreateFromDirectory(src, zip, filter: name => !name.EndsWith(".log"));
                var entries = ZipUtils.ListEntries(zip);
                Check.True(entries.Contains("keep.txt"));
                Check.False(entries.Contains("skip.log"));
            }
            finally { Directory.Delete(work, true); }
        }

        // The Zip Slip guard: an entry named "../escape.txt" must be rejected, writing nothing outside.
        public void RejectsPathTraversal()
        {
            string work = TempDir();
            try
            {
                string zip = Path.Combine(work, "evil.zip");
                using (var fs = File.Create(zip))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry e = archive.CreateEntry("../escape.txt");
                    using var s = e.Open();
                    using var w = new StreamWriter(s);
                    w.Write("pwned");
                }

                string dest = Path.Combine(work, "extract");
                Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(zip, dest));
                // The escape target (sibling of the extract dir) must not have been written.
                Check.False(File.Exists(Path.Combine(work, "escape.txt")));
            }
            finally { Directory.Delete(work, true); }
        }

        public void AddReplaceAndReadEntry()
        {
            string work = TempDir();
            try
            {
                string zip = Path.Combine(work, "a.zip");
                ZipUtils.AddOrReplaceEntry(zip, "note.txt", Encoding.UTF8.GetBytes("first"));
                Check.Equal("first", ZipUtils.ReadEntryText(zip, "note.txt"));

                ZipUtils.AddOrReplaceEntry(zip, "note.txt", Encoding.UTF8.GetBytes("second"));
                Check.Equal("second", ZipUtils.ReadEntryText(zip, "note.txt"));
                Check.Equal(1, ZipUtils.ListEntries(zip).Count(n => n == "note.txt")); // replaced, not duplicated
            }
            finally { Directory.Delete(work, true); }
        }

        public void MissingEntry()
        {
            string work = TempDir();
            try
            {
                string zip = Path.Combine(work, "a.zip");
                ZipUtils.AddOrReplaceEntry(zip, "x", new byte[] { 1 });
                Check.Equal(null, ZipUtils.ReadEntryBytes(zip, "missing"));
                Check.Throws<FileNotFoundException>(() => ZipUtils.ReadEntryText(zip, "missing"));
            }
            finally { Directory.Delete(work, true); }
        }

        public void OverwriteBehavior()
        {
            string work = TempDir();
            try
            {
                string src = Path.Combine(work, "src");
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "f.txt"), "v1");
                string zip = Path.Combine(work, "out.zip");
                ZipUtils.CreateFromDirectory(src, zip);

                string dest = Path.Combine(work, "dest");
                ZipUtils.ExtractToDirectory(zip, dest);
                Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(zip, dest, overwrite: false)); // exists
                ZipUtils.ExtractToDirectory(zip, dest, overwrite: true); // ok
            }
            finally { Directory.Delete(work, true); }
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => ZipUtils.ListEntries(null!));
            Check.Throws<DirectoryNotFoundException>(() => ZipUtils.CreateFromDirectory(Path.Combine(TempDir(), "nope"), "x.zip"));
        }
    }
}
