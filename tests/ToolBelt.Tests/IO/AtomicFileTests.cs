using System;
using System.IO;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class AtomicFileTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-atomic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void WritesNewFile()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "config.txt");
                AtomicFile.WriteAllText(path, "hello");
                Check.Equal("hello", File.ReadAllText(path));
            }
            finally { Directory.Delete(dir, true); }
        }

        public void OverwritesExistingFile()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "config.txt");
                File.WriteAllText(path, "old contents that are longer");
                AtomicFile.WriteAllText(path, "new");
                Check.Equal("new", File.ReadAllText(path));
            }
            finally { Directory.Delete(dir, true); }
        }

        public void KeepsBackupWhenRequested()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "config.txt");
                File.WriteAllText(path, "original");
                AtomicFile.WriteAllText(path, "updated", backup: true);
                Check.Equal("updated", File.ReadAllText(path));
                Check.True(File.Exists(path + ".bak"));
                Check.Equal("original", File.ReadAllText(path + ".bak"));
            }
            finally { Directory.Delete(dir, true); }
        }

        public void WritesBytes()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "data.bin");
                var bytes = new byte[] { 1, 2, 3, 4, 5 };
                AtomicFile.WriteAllBytes(path, bytes);
                Check.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes));
            }
            finally { Directory.Delete(dir, true); }
        }

        public void NoTempLeftBehind()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "config.txt");
                AtomicFile.WriteAllText(path, "x");
                // Only the target file should remain — no stray .tmp- files.
                Check.Equal(1, Directory.GetFiles(dir).Length);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => AtomicFile.WriteAllText(null!, "x"));
            Check.Throws<ArgumentNullException>(() => AtomicFile.WriteAllBytes("p", null!));
        }
    }
}
