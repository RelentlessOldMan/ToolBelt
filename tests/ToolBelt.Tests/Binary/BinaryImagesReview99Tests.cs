using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    // Round 99 review regressions (binary formats & images, sixth pass): each failed on the code before its fix.
    public sealed class BinaryImagesReview99Tests
    {
        // ---- PngReader.Decode(Stream): tRNS ----

        // Random 8-bit greyscale, RGB and palette images with tRNS, decoded from a stream and from bytes, against pixels
        // worked out here.
        public void Png_TheStreamPathKeepsTransparency()
        {
            var rng = new Random(9901);
            foreach (int colorType in new[] { 0, 2, 3 })
                for (int trial = 0; trial < 60; trial++)
                {
                    int w = rng.Next(1, 9), h = rng.Next(1, 9);
                    int entries = rng.Next(1, 17);
                    var palette = new byte[entries * 3];
                    rng.NextBytes(palette);
                    var alphas = new byte[rng.Next(1, entries + 1)];
                    rng.NextBytes(alphas);
                    int channels = colorType == 2 ? 3 : 1;
                    var samples = new byte[w * h * channels];
                    for (int i = 0; i < samples.Length; i++) samples[i] = (byte)(colorType == 3 ? rng.Next(entries) : rng.Next(4));
                    byte[] key = colorType == 0 ? new byte[] { 0, (byte)rng.Next(4) }
                        : new byte[] { 0, (byte)rng.Next(4), 0, (byte)rng.Next(4), 0, (byte)rng.Next(4) };
                    byte[] png = Build(w, h, colorType, samples, colorType == 3 ? palette : null, colorType == 3 ? alphas : key);
                    ImageBuffer fromBytes = PngReader.Decode(png);
                    ImageBuffer fromStream = PngReader.Decode(new MemoryStream(png));
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int at = (y * w + x) * channels;
                            Rgba expected;
                            if (colorType == 3)
                            {
                                int idx = samples[at];
                                expected = new Rgba(palette[idx * 3], palette[idx * 3 + 1], palette[idx * 3 + 2], idx < alphas.Length ? alphas[idx] : (byte)255);
                            }
                            else if (colorType == 0)
                            {
                                byte v = samples[at];
                                expected = new Rgba(v, v, v, v == key[1] ? (byte)0 : (byte)255);
                            }
                            else
                            {
                                byte r = samples[at], g = samples[at + 1], b = samples[at + 2];
                                expected = new Rgba(r, g, b, r == key[1] && g == key[3] && b == key[5] ? (byte)0 : (byte)255);
                            }
                            Check.Equal(expected, fromBytes.GetPixel(x, y), $"type {colorType} trial {trial} bytes ({x},{y})");
                            Check.Equal(expected, fromStream.GetPixel(x, y), $"type {colorType} trial {trial} stream ({x},{y})");
                        }
                }
        }

        // The stream path refuses a tRNS too long for the image, as the byte path does (kept empty, it passed).
        public void Png_TheStreamPathRefusesABadTrns()
        {
            byte[] png = Build(1, 1, 0, new byte[] { 7 }, null, new byte[] { 0, 7, 0 });
            Check.Throws<InvalidDataException>(() => PngReader.Decode(png));
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(png)));
            byte[] huge = Build(1, 1, 3, new byte[] { 0 }, new byte[] { 1, 2, 3 }, new byte[300]);
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(huge)));
        }

        // ---- PngReader.Decode(Stream): oversized kept chunks refused at their header ----

        public void Png_AnOversizedKeptChunkIsRefusedBeforeItIsRead()
        {
            foreach (string type in new[] { "PLTE", "IEND", "ABCD", "tRNS" })
            {
                var head = new MemoryStream();
                byte[] start = Build(1, 1, 2, new byte[3], null, null);
                head.Write(start, 0, 8 + 25);                                   // signature and IHDR
                WriteBe32(head, 200 << 20);
                head.Write(Encoding.ASCII.GetBytes(type), 0, 4);
                var source = new ZerosAfter(head.ToArray());
                Check.Throws<InvalidDataException>(() => PngReader.Decode(source));
                Check.True(source.Served < 1 << 20, $"{type}: read {source.Served:N0} bytes of a chunk declaring 200 MB");
            }
        }

        // An IEND with data is refused by both paths alike (hardening: the byte path ignored the data).
        public void Png_ALongIendIsRefusedByBothPaths()
        {
            byte[] png = Build(1, 1, 2, new byte[3], null, null, iendData: new byte[] { 1, 2 });
            Check.Throws<InvalidDataException>(() => PngReader.Decode(png));
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(png)));
        }

        // ---- SvgDocument: the size message ----

        public void Svg_TheSizeMessageStatesABoundThatHolds()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(5e-7, 1));
            Check.Equal("width", ex.ParamName);
            Check.False(ex.Message.Contains("at least 0.0000005"), ex.Message);
            var ey = Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(1, 5e-7));
            Check.Equal("height", ey.ParamName);
            Check.False(ey.Message.Contains("at least 0.0000005"), ey.Message);
            new SvgDocument(5.000001e-7, 5.000001e-7);
        }

        // ---- helpers ----

        private static byte[] Build(int w, int h, int colorType, byte[] samples, byte[]? palette, byte[]? trns, byte[]? iendData = null)
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            Be32(ihdr, 0, w);
            Be32(ihdr, 4, h);
            ihdr[8] = 8;
            ihdr[9] = (byte)colorType;
            Chunk(ms, "IHDR", ihdr);
            if (palette != null) Chunk(ms, "PLTE", palette);
            if (trns != null) Chunk(ms, "tRNS", trns);
            int rowBytes = samples.Length / h;
            var raw = new byte[h * (rowBytes + 1)];
            for (int y = 0; y < h; y++) Array.Copy(samples, y * rowBytes, raw, y * (rowBytes + 1) + 1, rowBytes);
            Chunk(ms, "IDAT", Zlib(raw));
            Chunk(ms, "IEND", iendData ?? new byte[0]);
            return ms.ToArray();
        }

        private static byte[] Zlib(byte[] raw)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0x9C);
            using (var d = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true)) d.Write(raw, 0, raw.Length);
            uint a = 1, b = 0;
            foreach (byte x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
            WriteBe32(ms, (int)((b << 16) | a));
            return ms.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] body)
        {
            WriteBe32(s, body.Length);
            byte[] t = Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(body, 0, body.Length);
            uint crc = 0xFFFFFFFFu;
            foreach (byte x in t) crc = Crc(crc, x);
            foreach (byte x in body) crc = Crc(crc, x);
            WriteBe32(s, (int)(crc ^ 0xFFFFFFFFu));
        }

        private static uint Crc(uint crc, byte x)
        {
            crc ^= x;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            return crc;
        }

        private static void Be32(byte[] b, int at, int v)
        {
            b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
        }

        private static void WriteBe32(Stream s, int v)
        {
            var b = new byte[4];
            Be32(b, 0, v);
            s.Write(b, 0, 4);
        }

        // The given bytes, then zeros without end, counting what was read.
        private sealed class ZerosAfter : Stream
        {
            private readonly byte[] _head;
            public long Served;
            public ZerosAfter(byte[] head) { _head = head; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = 0;
                while (n < count && Served < _head.Length) buffer[offset + n++] = _head[Served++];
                if (n == 0) { Array.Clear(buffer, offset, count); n = count; Served += n; }
                return n;
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
    }
}
