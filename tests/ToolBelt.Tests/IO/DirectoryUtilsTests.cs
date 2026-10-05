using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class DirectoryUtilsTests
    {
        // src/ a.txt(3)  sub/b.bin(10)  sub/deep/c.txt(5)  empty/
        private static string BuildTree(TempDirectory tmp, string name = "src")
        {
            string root = tmp.Combine(name);
            Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            File.WriteAllText(Path.Combine(root, "a.txt"), "abc");
            File.WriteAllBytes(Path.Combine(root, "sub", "b.bin"), new byte[10]);
            File.WriteAllText(Path.Combine(root, "sub", "deep", "c.txt"), "hello");
            return root;
        }

        private static string Rel(string root, string path) => DirectoryUtils.GetRelativePath(root, path);

        // ---------- copy ----------

        public void Copy_FullTreeWithTimestamps()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp), dst = tmp.Combine("dst");
            var old = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(src, "a.txt"), old);

            CopyResult r = DirectoryUtils.Copy(src, dst);
            Check.Equal(3, r.FilesCopied);
            Check.Equal(18L, r.BytesCopied);
            Check.Equal(4, r.DirectoriesCreated);                       // dst, sub, sub/deep, empty
            Check.Equal(0, r.Errors);
            Check.Equal("hello", File.ReadAllText(Path.Combine(dst, "sub", "deep", "c.txt")));
            Check.True(Directory.Exists(Path.Combine(dst, "empty")));
            Check.Equal(old, File.GetLastWriteTimeUtc(Path.Combine(dst, "a.txt")));
        }

        public void Copy_FiltersFilesAndPrunesDirectories()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp), dst = tmp.Combine("dst");
            CopyResult r = DirectoryUtils.Copy(src, dst, new CopyOptions
            {
                FileFilter = rel => rel.EndsWith(".txt", StringComparison.Ordinal),
                DirectoryFilter = rel => rel != "sub/deep",
            });
            Check.Equal(1, r.FilesCopied);
            Check.True(File.Exists(Path.Combine(dst, "a.txt")));
            Check.False(File.Exists(Path.Combine(dst, "sub", "b.bin")));
            Check.False(Directory.Exists(Path.Combine(dst, "sub", "deep")));
        }

        public void Copy_OverwritePolicies()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp), dst = tmp.Combine("dst");
            string target = Path.Combine(dst, "a.txt");
            void Reset(DateTime when)
            {
                Directory.CreateDirectory(dst);
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                File.WriteAllText(target, "OLD");
                File.SetLastWriteTimeUtc(target, when);
            }
            DateTime srcTime = File.GetLastWriteTimeUtc(Path.Combine(src, "a.txt"));

            Reset(srcTime.AddDays(-1));
            CopyResult skip = DirectoryUtils.Copy(src, dst, new CopyOptions { Overwrite = OverwritePolicy.Skip });
            Check.Equal("OLD", File.ReadAllText(target));
            Check.Equal(1, skip.FilesSkipped);

            Reset(srcTime.AddDays(1));                                  // destination newer: keep it
            DirectoryUtils.Copy(src, dst, new CopyOptions { Overwrite = OverwritePolicy.IfNewer });
            Check.Equal("OLD", File.ReadAllText(target));
            Reset(srcTime.AddDays(-1));                                 // destination older: replace it
            DirectoryUtils.Copy(src, dst, new CopyOptions { Overwrite = OverwritePolicy.IfNewer });
            Check.Equal("abc", File.ReadAllText(target));

            // Fail: a fresh destination where only a.txt already exists — one conflict, the other files still copy.
            string dst2 = tmp.Combine("dst2");
            Directory.CreateDirectory(dst2);
            File.WriteAllText(Path.Combine(dst2, "a.txt"), "OLD");
            Check.Throws<IOException>(() => DirectoryUtils.Copy(src, dst2, new CopyOptions { Overwrite = OverwritePolicy.Fail }));
            DirectoryUtils.Delete(dst2);
            Directory.CreateDirectory(dst2);
            File.WriteAllText(Path.Combine(dst2, "a.txt"), "OLD");
            var errors = new List<string>();
            CopyResult reported = DirectoryUtils.Copy(src, dst2, new CopyOptions { Overwrite = OverwritePolicy.Fail, OnError = (p, _) => errors.Add(p) });
            Check.Equal(1, reported.Errors);
            Check.Equal(2, reported.FilesCopied);
            Check.True(errors[0].EndsWith("a.txt", StringComparison.Ordinal));
            Check.Equal("OLD", File.ReadAllText(Path.Combine(dst2, "a.txt")));

            Reset(srcTime);
            File.SetAttributes(target, FileAttributes.ReadOnly);       // Overwrite replaces even a read-only file
            DirectoryUtils.Copy(src, dst);
            Check.Equal("abc", File.ReadAllText(target));
        }

        public void Copy_RejectsDestinationInsideSource()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp);
            Check.Throws<ArgumentException>(() => DirectoryUtils.Copy(src, Path.Combine(src, "sub", "copy")));
            Check.Throws<ArgumentException>(() => DirectoryUtils.Copy(src, src));
            Check.Throws<DirectoryNotFoundException>(() => DirectoryUtils.Copy(tmp.Combine("missing"), tmp.Combine("x")));
        }

        // ---------- delete / empty ----------

        public void Delete_ReadOnlyTreeAndSingleFiles()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp);
            foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.ReadOnly);
            new DirectoryInfo(Path.Combine(src, "sub")).Attributes |= FileAttributes.ReadOnly;
            Check.True(DirectoryUtils.Delete(src));
            Check.False(Directory.Exists(src));
            Check.False(DirectoryUtils.Delete(src));                    // nothing left

            string lone = tmp.Combine("lone.txt");
            File.WriteAllText(lone, "x");
            File.SetAttributes(lone, FileAttributes.ReadOnly);
            Check.True(DirectoryUtils.Delete(lone));
            Check.False(File.Exists(lone));
        }

        public void Delete_RetriesATransientLock()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp);
            var locker = new FileStream(Path.Combine(src, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
            using var release = new Timer(_ => locker.Dispose(), null, 250, Timeout.Infinite);
            Check.True(DirectoryUtils.Delete(src, retries: 40, retryDelay: TimeSpan.FromMilliseconds(25)));
            Check.False(Directory.Exists(src));
        }

        public void Delete_GivesUpWhenTheLockPersists()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp);
            using (new FileStream(Path.Combine(src, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                Check.Throws<IOException>(() => DirectoryUtils.Delete(src, retries: 2, retryDelay: TimeSpan.FromMilliseconds(5)));
            Check.True(DirectoryUtils.Delete(src));
        }

        public void Links_AreNeverFollowed()
        {
            using var tmp = new TempDirectory();
            string outside = tmp.Combine("outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "precious");
            string src = BuildTree(tmp);
            string link = Path.Combine(src, "link");
            if (!TryCreateDirectoryLink(link, outside)) return;        // no link support here: nothing to test

            Check.Equal(3, DirectoryUtils.Summarize(src).FileCount);    // keep.txt not counted through the link
            Check.False(DirectoryUtils.EnumerateFiles(src).Any(f => f.EndsWith("keep.txt", StringComparison.Ordinal)));
            DirectoryUtils.Copy(src, tmp.Combine("dst"));
            Check.False(File.Exists(Path.Combine(tmp.Combine("dst"), "link", "keep.txt")));

            Check.True(DirectoryUtils.Delete(src));
            Check.False(Directory.Exists(src));
            Check.Equal("precious", File.ReadAllText(Path.Combine(outside, "keep.txt"))); // the target survived
        }

        public void EnsureEmpty_CreatesOrClears()
        {
            using var tmp = new TempDirectory();
            string fresh = tmp.Combine("fresh");
            DirectoryUtils.EnsureEmpty(fresh);
            Check.True(Directory.Exists(fresh));
            string src = BuildTree(tmp);
            DirectoryUtils.EnsureEmpty(src);
            Check.True(Directory.Exists(src));
            Check.Equal(0, Directory.GetFileSystemEntries(src).Length);
            Check.Throws<IOException>(() => DirectoryUtils.EnsureEmpty(Path.Combine(tmp.Path, "f.txt").Also(p => File.WriteAllText(p, "x"))));
        }

        // ---------- walk / summary ----------

        public void EnumerateFiles_PatternAndPruning()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp);
            var txt = DirectoryUtils.EnumerateFiles(src, "*.txt").Select(f => Rel(src, f)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Check.Equal("a.txt|sub/deep/c.txt", string.Join("|", txt));
            var pruned = DirectoryUtils.EnumerateFiles(src, directoryFilter: rel => !rel.StartsWith("sub", StringComparison.Ordinal)).ToArray();
            Check.Equal(1, pruned.Length);
            Check.Throws<DirectoryNotFoundException>(() => DirectoryUtils.EnumerateFiles(tmp.Combine("nope")));   // eager validation
        }

        public void Walk_SkipsAndReportsAnUnreadableSubtree()
        {
            if (!OperatingSystem.IsWindows()) return;
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp);
            var sub = new DirectoryInfo(Path.Combine(src, "sub"));
            var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
            DirectorySecurity acl = sub.GetAccessControl();
            acl.AddAccessRule(deny);
            sub.SetAccessControl(acl);
            try
            {
                var reported = new List<string>();
                var files = DirectoryUtils.EnumerateFiles(src, onError: (p, _) => reported.Add(p)).Select(f => Rel(src, f)).ToArray();
                Check.Equal("a.txt", string.Join("|", files));          // the walk carried on
                Check.Equal(1, reported.Count);
                Check.True(reported[0].EndsWith("sub", StringComparison.Ordinal));
                DirectorySummary s = DirectoryUtils.Summarize(src);
                Check.Equal(1, s.InaccessibleCount);
                Check.Equal(1, s.FileCount);
            }
            finally
            {
                DirectorySecurity restore = sub.GetAccessControl();
                restore.RemoveAccessRule(deny);
                sub.SetAccessControl(restore);
            }
        }

        public void Summarize_CountsTheTree()
        {
            using var tmp = new TempDirectory();
            DirectorySummary s = DirectoryUtils.Summarize(BuildTree(tmp));
            Check.Equal(18L, s.TotalBytes);
            Check.Equal(3, s.FileCount);
            Check.Equal(3, s.DirectoryCount);                          // sub, sub/deep, empty
            Check.Equal(0, s.InaccessibleCount);
        }

        public void GetRelativePath_InsideRootAndOutside()
        {
            using var tmp = new TempDirectory();
            string src = BuildTree(tmp);
            Check.Equal("sub/deep/c.txt", DirectoryUtils.GetRelativePath(src, Path.Combine(src, "sub", "deep", "c.txt")));
            Check.Equal("", DirectoryUtils.GetRelativePath(src, src + Path.DirectorySeparatorChar));
            Check.Throws<ArgumentException>(() => DirectoryUtils.GetRelativePath(src, tmp.Path));
            Check.Throws<ArgumentException>(() => DirectoryUtils.GetRelativePath(src, src + "-sibling"));  // prefix is not containment
        }

        private static bool TryCreateDirectoryLink(string link, string target)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // A junction needs no special privilege (unlike a directory symlink).
                    var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using var p = System.Diagnostics.Process.Start(psi)!;
                    p.WaitForExit(10000);
                    return Directory.Exists(link);
                }
                Directory.CreateSymbolicLink(link, target);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    internal static class TestExtensions
    {
        public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
    }
}
