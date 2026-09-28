using System;
using System.IO;
using System.Linq;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class FileSinkTests
    {
        private static LogEvent Ev(string message)
            => new LogEvent(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", message, null);

        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-filesink-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void WritesLinesToFile()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "app.log");
                using (var sink = new FileSink(path, LogFormatters.Compact))
                {
                    sink.Emit(Ev("alpha"));
                    sink.Emit(Ev("beta"));
                }
                string text = File.ReadAllText(path);
                Check.True(text.Contains("alpha") && text.Contains("beta"), text);
            }
            finally { Directory.Delete(dir, true); }
        }

        public void RotatesAndPrunes()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "app.log");
                // Short lines, tiny cap -> many rotations; keep 2 backups.
                using (var sink = new FileSink(path, e => e.Message, maxBytes: 40, maxFiles: 2))
                    for (int i = 0; i < 50; i++) sink.Emit(Ev(new string('x', 30)));

                Check.True(File.Exists(path), "primary log missing");
                Check.True(File.Exists(path + ".1"), "first backup missing");
                Check.False(File.Exists(path + ".3"), "retention exceeded (.3 should have been pruned)");
            }
            finally { Directory.Delete(dir, true); }
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new FileSink(null!));
            string dir = TempDir();
            try
            {
                Check.Throws<ArgumentOutOfRangeException>(() => new FileSink(Path.Combine(dir, "a.log"), maxBytes: 100, maxFiles: 0));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
