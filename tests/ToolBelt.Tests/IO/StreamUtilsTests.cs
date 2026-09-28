using System;
using System.IO;
using System.Linq;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class StreamUtilsTests
    {
        public void CopyCopiesAllBytes()
        {
            var data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
            using var src = new MemoryStream(data);
            using var dst = new MemoryStream();
            long total = StreamUtils.CopyWithProgress(src, dst, bufferSize: 64);
            Check.Equal(1000L, total);
            Check.True(dst.ToArray().SequenceEqual(data));
        }

        public void ProgressReportsCumulative()
        {
            var data = new byte[500];
            using var src = new MemoryStream(data);
            using var dst = new MemoryStream();
            long last = 0;
            bool monotonic = true;
            StreamUtils.CopyWithProgress(src, dst, p => { if (p < last) monotonic = false; last = p; }, bufferSize: 100);
            Check.True(monotonic, "progress should be non-decreasing");
            Check.Equal(500L, last);
        }

        public void ReadExactlyFills()
        {
            using var src = new MemoryStream(new byte[] { 10, 20, 30, 40, 50 });
            var got = StreamUtils.ReadExactly(src, 3);
            Check.True(got.SequenceEqual(new byte[] { 10, 20, 30 }));
        }

        public void ReadExactlyThrowsWhenShort()
        {
            using var src = new MemoryStream(new byte[] { 1, 2 });
            Check.Throws<EndOfStreamException>(() => StreamUtils.ReadExactly(src, 5));
        }

        public void InvalidArguments_Throw()
        {
            using var ms = new MemoryStream();
            Check.Throws<ArgumentNullException>(() => StreamUtils.CopyWithProgress(null!, ms));
            Check.Throws<ArgumentOutOfRangeException>(() => StreamUtils.CopyWithProgress(ms, ms, bufferSize: 0));
            Check.Throws<ArgumentOutOfRangeException>(() => StreamUtils.ReadExactly(ms, -1));
        }
    }
}
