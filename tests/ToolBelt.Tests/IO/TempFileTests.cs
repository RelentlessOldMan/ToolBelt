using System;
using System.IO;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class TempFileTests
    {
        public void TempFile_CreatesAndDeletes()
        {
            string path;
            using (var temp = new TempFile())
            {
                path = temp.Path;
                Check.True(File.Exists(path), "file should exist within scope");
                Check.True(path.StartsWith(Path.GetTempPath(), StringComparison.Ordinal), "under temp dir");
            }
            Check.False(File.Exists(path), "file should be deleted after dispose");
        }

        public void TempFile_HonorsExtension()
        {
            using var temp = new TempFile(".log");
            Check.Equal(".log", Path.GetExtension(temp.Path));
        }

        public void TempFile_IsWritable()
        {
            using var temp = new TempFile();
            File.WriteAllText(temp.Path, "hello");
            Check.Equal("hello", File.ReadAllText(temp.Path));
        }

        public void TempFile_DoubleDispose_IsSafe()
        {
            var temp = new TempFile();
            temp.Dispose();
            temp.Dispose(); // must not throw
        }

        public void TempDirectory_CreatesAndDeletesRecursively()
        {
            string path;
            using (var dir = new TempDirectory())
            {
                path = dir.Path;
                Check.True(Directory.Exists(path));

                string nested = dir.Combine("sub");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(nested, "file.txt"), "data");
                Check.True(File.Exists(Path.Combine(nested, "file.txt")));
            }
            Check.False(Directory.Exists(path), "directory should be removed recursively after dispose");
        }

        public void TempDirectory_UniquePaths()
        {
            using var a = new TempDirectory();
            using var b = new TempDirectory();
            Check.False(a.Path == b.Path, "each temp directory is unique");
        }
    }
}
