using System;
using System.IO;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    // Round 129 review regressions (binary formats & images, eighth pass): each failed on the code before its fix.
    public sealed class BinaryImagesReview129Tests
    {
        // ---- Size checks that overflowed long: the pixel count is compared, not the byte count ----

        public void ImageBuffer_HugeOnBothSidesIsRefused()
        {
            foreach (var (w, h) in new[] { (int.MaxValue, int.MaxValue), (int.MaxValue, 1 << 30), (0x40000000, 0x40000000), (0x7FFFFFC7, 0x7FFFFFC7) })
            {
                var ex = Check.Throws<ArgumentOutOfRangeException>(() => new ImageBuffer(w, h));
                Check.Equal("width", ex.ParamName);
            }
            // Just past the largest byte array is refused too, as before.
            Check.Throws<ArgumentOutOfRangeException>(() => new ImageBuffer(0x7FFFFFC7 / 4 + 1, 1));
            Check.Equal(4, new ImageBuffer(1, 1).Pixels.Length);
        }

        private static byte[] BmpHeader(int w, int h, int bpp)
        {
            var d = new byte[54 + 64];
            d[0] = (byte)'B'; d[1] = (byte)'M';
            void Le32(int at, int v) { d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); d[at + 2] = (byte)(v >> 16); d[at + 3] = (byte)(v >> 24); }
            Le32(2, d.Length); Le32(10, 54); Le32(14, 40); Le32(18, w); Le32(22, h);
            d[26] = 1; d[28] = (byte)bpp;
            return d;
        }

        public void Bmp_HugeOnBothSidesIsInvalidData()
        {
            foreach (int bpp in new[] { 8, 24, 32 })
                foreach (var (w, h) in new[] { (int.MaxValue, int.MaxValue), (int.MaxValue, -int.MaxValue), (int.MaxValue, 1 << 30), (0x7FFFFFF0, 0x7FFFFFF0) })
                {
                    var ex = Check.Throws<InvalidDataException>(() => Bmp.Decode(BmpHeader(w, h, bpp)));
                    Check.True(ex.Message.Contains("too large"), bpp + " " + w + "x" + h + ": " + ex.Message);
                }
        }

        public void Png_HugeOnBothSidesIsRefusedAtTheHeader()
        {
            var stream = new HugeIdat();
            var ex = Check.Throws<InvalidDataException>(() => PngReader.Decode(stream));
            Check.True(ex.Message.Contains("4294967295x4294967295"), ex.Message);
            Check.True(stream.Served < 1 << 20, stream.Served.ToString());
            // The byte[] decoder refused it already.
            Check.Throws<InvalidDataException>(() => PngReader.Decode(stream.Head));
        }

        // IHDR of 0xFFFFFFFF × 0xFFFFFFFF, then an IDAT whose data is served as zeros as it is read.
        private sealed class HugeIdat : Stream
        {
            public readonly byte[] Head;
            public long Served { get; private set; }

            public HugeIdat()
            {
                var ms = new MemoryStream();
                ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                EndianConverter.WriteUInt32BigEndian(ihdr, 0, uint.MaxValue);
                EndianConverter.WriteUInt32BigEndian(ihdr, 4, uint.MaxValue);
                ihdr[8] = 8; ihdr[9] = 6;
                Chunk(ms, "IHDR", ihdr);
                var len = new byte[4];
                EndianConverter.WriteUInt32BigEndian(len, 0, 0x7FFFFFFF);
                ms.Write(len, 0, 4);
                ms.Write(Encoding.ASCII.GetBytes("IDAT"), 0, 4);
                Head = ms.ToArray();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                // A stop well short of the 2 GB the old code read, so it fails here quickly.
                if (Served > 16 << 20) throw new InvalidOperationException("read far into the IDAT");
                for (int i = 0; i < count; i++, Served++)
                    buffer[offset + i] = Served < Head.Length ? Head[Served] : (byte)0;
                return count;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Served; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static void Chunk(Stream ms, string type, byte[] body)
        {
            var len = new byte[4];
            EndianConverter.WriteUInt32BigEndian(len, 0, (uint)body.Length);
            ms.Write(len, 0, 4);
            var td = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            ms.Write(td, 0, td.Length);
            var crc = new byte[4];
            EndianConverter.WriteUInt32BigEndian(crc, 0, Crc32.Compute(td));
            ms.Write(crc, 0, 4);
        }
    }
}
