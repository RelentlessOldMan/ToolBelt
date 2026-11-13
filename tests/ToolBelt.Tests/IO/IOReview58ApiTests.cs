using System;
using System.Collections.Generic;
#if !NETFRAMEWORK
using System.Formats.Tar;
#endif
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
    /// Regressions from review round 58 that needed a new parameter (so at the old code they fail to compile): a way to
    /// cancel a retrying delete, a cap on one line's bytes, and a cap on an archive's total extracted bytes.
    /// </summary>
    public sealed class IOReview58ApiTests
    {
        public void Delete_RetriesStopWhenCancelled()
        {
            // A locked file with many retries held the caller for retries x delay, with no way out.
            if (!TestPlatform.IsWindows) return;
            using var tmp = new TempDirectory();
            string dir = tmp.Combine("d");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "lock.txt"), "l");
            using var hold = new FileStream(Path.Combine(dir, "lock.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Check.Throws<OperationCanceledException>(() => DirectoryUtils.Delete(dir, retries: 1000, retryDelay: TimeSpan.FromSeconds(1), cancellationToken: cts.Token));
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
        }

        public void EnsureEmpty_ACancelledTokenThrowsFirst()
        {
            using var tmp = new TempDirectory();
            File.WriteAllText(tmp.Combine("a.txt"), "a");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Check.Throws<OperationCanceledException>(() => DirectoryUtils.EnsureEmpty(tmp.Path, cancellationToken: cts.Token));
            Check.True(File.Exists(tmp.Combine("a.txt")));
        }

        public void ReadLines_ALineLongerThanTheCapIsInvalidData()
        {
            // With no newline, a whole stream became one line: a multi-gigabyte input ran the process out of memory.
            byte[] data = Encoding.ASCII.GetBytes("short\n" + new string('x', 100) + "\nend");
            var lines = new List<string>();
            var ex = Check.Throws<InvalidDataException>(() =>
            {
                foreach (TextLine l in LineReader.ReadLines(new MemoryStream(data), maxLineBytes: 50)) lines.Add(l.Text);
            });
            Check.True(ex.Message.Contains("byte 6"), ex.Message);
            Check.Equal("short", string.Join("|", lines));
            Check.Equal("short|" + new string('x', 100) + "|end",
                string.Join("|", LineReader.ReadLines(new MemoryStream(data), maxLineBytes: 100).Select(l => l.Text)));
            Check.Throws<ArgumentOutOfRangeException>(() => LineReader.ReadLines(new MemoryStream(data), maxLineBytes: -1).ToList());
        }

        private static string ZipWith(TempDirectory tmp, params int[] sizes)
        {
            string zip = tmp.Combine("z-" + Guid.NewGuid().ToString("N") + ".zip");
            using (var fs = File.Create(zip))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                for (int i = 0; i < sizes.Length; i++)
                    using (var s = z.CreateEntry("f" + i + ".bin").Open())
                        s.Write(new byte[sizes[i]], 0, sizes[i]);
            return zip;
        }

        public void ZipExtract_TheTotalByteCapStopsADecompressionBomb()
        {
            // Every entry's size was checked, but not their sum: many small-looking entries filled the disk.
            using var tmp = new TempDirectory();
            string zip = ZipWith(tmp, 600, 600);
            ZipUtils.ExtractToDirectory(zip, tmp.Combine("ok"), maxTotalBytes: 1200);
            var ex = Check.Throws<InvalidDataException>(() => ZipUtils.ExtractToDirectory(zip, tmp.Combine("bomb"), maxTotalBytes: 1000));
            Check.True(ex.Message.Contains("1000"), ex.Message);
            Check.False(File.Exists(Path.Combine(tmp.Combine("bomb"), "f1.bin")));
            var arg = Check.Throws<ArgumentOutOfRangeException>(() => ZipUtils.ExtractToDirectory(zip, tmp.Combine("neg"), maxTotalBytes: -1));
            Check.Equal("maxTotalBytes", arg.ParamName);
        }

#if !NETFRAMEWORK
        public void TarExtract_TheTotalByteCapStopsAnOversizedArchive()
        {
            using var tmp = new TempDirectory();
            string tar = tmp.Combine("t.tar");
            using (var fs = File.Create(tar))
            using (var w = new TarWriter(fs, TarEntryFormat.Pax))
                for (int i = 0; i < 2; i++)
                    w.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "f" + i + ".bin") { DataStream = new MemoryStream(new byte[600]) });
            TarUtils.ExtractToDirectory(tar, tmp.Combine("ok"), maxTotalBytes: 1200);
            var ex = Check.Throws<InvalidDataException>(() => TarUtils.ExtractToDirectory(tar, tmp.Combine("big"), maxTotalBytes: 1000));
            Check.True(ex.Message.Contains("1000"), ex.Message);
            Check.False(File.Exists(Path.Combine(tmp.Combine("big"), "f1.bin")));
            var arg = Check.Throws<ArgumentOutOfRangeException>(() => TarUtils.ExtractToDirectory(tar, tmp.Combine("neg"), maxTotalBytes: -1));
            Check.Equal("maxTotalBytes", arg.ParamName);
        }
#endif
    }
}
