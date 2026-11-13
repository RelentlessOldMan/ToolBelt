using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>
    /// Regressions from review round 57 (the whole suite on .NET Framework 4.8): each test failed there before its fix.
    /// On .NET 8 they hold too.
    /// </summary>
    public sealed class IONetFrameworkReview57Tests
    {
        // ---------------- ZipUtils: names .NET Framework's ZipArchive cannot read ----------------

        // .NET Framework's ZipArchive refuses to create these names, so a same-length placeholder is written and the real
        // name patched over it in both the local and the central header.
        private static void WriteZip(string zip, string name)
        {
            string placeholder = new string('q', name.Length);
            using (var fs = new FileStream(zip, FileMode.Create))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            using (var s = z.CreateEntry(placeholder).Open())
                s.Write(new byte[] { 1, 2, 3 }, 0, 3);
            byte[] bytes = File.ReadAllBytes(zip), from = Encoding.ASCII.GetBytes(placeholder), to = Encoding.ASCII.GetBytes(name);
            int patched = 0;
            for (int i = 0; i + from.Length <= bytes.Length; i++)
                if (bytes.Skip(i).Take(from.Length).SequenceEqual(from)) { Array.Copy(to, 0, bytes, i, to.Length); patched++; }
            Check.Equal(2, patched);
            File.WriteAllBytes(zip, bytes);
        }

        private static readonly string[] BadNames = { "x.txt:hidden", "a\0b.txt", "a|b.txt" };

        // Either the name is read (.NET Core) or the archive is refused with IOException, never ArgumentException.
        private static void ReadsOrIOException(Action read)
        {
            try { read(); }
            catch (IOException) { }
        }

        public void ListEntries_ABadNameIsReadOrIOException()
        {
            using var tmp = new TempDirectory();
            foreach (string name in BadNames)
            {
                string zip = tmp.Combine("z.zip");
                WriteZip(zip, name);
                ReadsOrIOException(() => Check.Equal(name, ZipUtils.ListEntries(zip).Single()));
            }
        }

        public void ReadEntryBytes_ABadNameElsewhereIsReadOrIOException()
        {
            using var tmp = new TempDirectory();
            foreach (string name in BadNames)
            {
                string zip = tmp.Combine("z.zip");
                WriteZip(zip, name);
                ReadsOrIOException(() => Check.True(ZipUtils.ReadEntryBytes(zip, name)!.SequenceEqual(new byte[] { 1, 2, 3 })));
                ReadsOrIOException(() => Check.True(ZipUtils.ReadEntryBytes(zip, "other.txt") is null));
            }
        }

        public void AddOrReplaceEntry_ABadNameElsewhereIsKeptOrIOException()
        {
            using var tmp = new TempDirectory();
            foreach (string name in BadNames)
            {
                string zip = tmp.Combine("z.zip");
                WriteZip(zip, name);
                ReadsOrIOException(() => ZipUtils.AddOrReplaceEntry(zip, "ok.txt", new byte[] { 9 }));
            }
        }

        public void Extract_ABadNameIsIOException()
        {
            using var tmp = new TempDirectory();
            foreach (string name in BadNames)
            {
                string zip = tmp.Combine("z.zip");
                WriteZip(zip, name);
                Check.Throws<IOException>(() => ZipUtils.ExtractToDirectory(zip, tmp.Combine("dst")));
            }
        }

        // ---------------- DirectoryUtils.Copy: into the source through a junction ----------------

        private static bool TryJunction(string link, string target)
        {
            if (!TestPlatform.IsWindows) return false;
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(10000);
            return p.ExitCode == 0 && Directory.Exists(link);
        }

        private static Exception? CopyError(string source, string destination)
        {
            Exception? caught = null;
            var t = new Thread(() => { try { DirectoryUtils.Copy(source, destination); } catch (Exception ex) { caught = ex; } }) { IsBackground = true };
            t.Start();
            Check.True(t.Join(15000), "Copy never finished");
            return caught;
        }

        public void Copy_IntoANestedNewFolderThroughAJunctionIsRefused()
        {
            // On netstandard2.0 links were not resolved: Copy(src, j\new\deeper) with j → src copied the tree into itself.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), j = tmp.Combine("j");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "sub", "a.txt"), "a");
            if (!TryJunction(j, src)) return;
            try
            {
                Exception? error = CopyError(src, Path.Combine(j, "sub", "new", "deeper"));
                Check.True(error is ArgumentException, $"expected ArgumentException, got {error?.GetType().Name ?? "no exception"}");
                Check.False(Directory.Exists(Path.Combine(src, "sub", "new")), "the copy was written into its own source");
            }
            finally { Directory.Delete(j); }
        }

        public void Copy_FromAJunctionIntoItsTargetIsRefused()
        {
            // The source named through the junction, the destination by its real path inside the target.
            using var tmp = new TempDirectory();
            string real = tmp.Combine("real"), j = tmp.Combine("j");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "a.txt"), "a");
            if (!TryJunction(j, real)) return;
            try
            {
                Exception? error = CopyError(j, Path.Combine(real, "c"));
                Check.True(error is ArgumentException, $"expected ArgumentException, got {error?.GetType().Name ?? "no exception"}");
                Check.False(Directory.Exists(Path.Combine(real, "c")), "the copy was written into its own source");
            }
            finally { Directory.Delete(j); }
        }

        public void Copy_BesideTheSourceThroughAJunctionStillWorks()
        {
            // The mark check must not refuse a destination that is only near the source.
            using var tmp = new TempDirectory();
            string src = tmp.Combine("src"), other = tmp.Combine("other"), j = tmp.Combine("j");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            if (!TryJunction(j, other)) return;
            try
            {
                Check.True(CopyError(src, Path.Combine(j, "c")) is null, "a copy beside the source was refused");
                Check.Equal("a", File.ReadAllText(Path.Combine(other, "c", "a.txt")));
                Check.Equal("c", string.Join("|", Directory.GetFileSystemEntries(other).Select(Path.GetFileName)));
                Check.Equal("a.txt", string.Join("|", Directory.GetFileSystemEntries(src).Select(Path.GetFileName)));
            }
            finally { Directory.Delete(j); }
        }
    }
}
