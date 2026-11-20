using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>
    /// Round 169 review regressions (binary formats and images, tenth pass): each failed on the code before its fix.
    /// </summary>
    public sealed class BinaryImagesReview169Tests
    {
        private static byte[] Adler(byte[] raw)
        {
            var adler = new byte[4];
            EndianConverter.WriteUInt32BigEndian(adler, 0, Adler32.Compute(raw));
            return adler;
        }

        private static byte[] Deflate(byte[] raw, CompressionLevel level)
        {
            var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, level, leaveOpen: true)) d.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        private static byte[] Ihdr(uint w, uint h)
        {
            var ihdr = new byte[13];
            EndianConverter.WriteUInt32BigEndian(ihdr, 0, w);
            EndianConverter.WriteUInt32BigEndian(ihdr, 4, h);
            ihdr[8] = 8; ihdr[9] = 6;                                       // 8-bit RGBA
            return ihdr;
        }

        // The zlib data split over IDAT chunks of the given size.
        private static byte[] Png(byte[] ihdr, byte[] zlib, int split)
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            void Chunk(string type, byte[] body)
            {
                var len = new byte[4];
                EndianConverter.WriteUInt32BigEndian(len, 0, (uint)body.Length);
                ms.Write(len, 0, 4);
                var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
                ms.Write(td, 0, td.Length);
                var crc = new byte[4];
                EndianConverter.WriteUInt32BigEndian(crc, 0, Crc32.Compute(td));
                ms.Write(crc, 0, 4);
            }
            Chunk("IHDR", ihdr);
            for (int i = 0; i < zlib.Length; i += split) Chunk("IDAT", zlib.Skip(i).Take(split).ToArray());
            Chunk("IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        private static void Rejected(byte[] png, string what)
        {
            Check.Throws<InvalidDataException>(() => PngReader.Decode(png), what + ", byte[]");
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(png)), what + ", Stream");
        }

        // Random images, compressed by DeflateStream at every level and split at random: each decodes to its pixels;
        // with bytes put between the deflate data and the Adler-32 (the old reader took the Adler from the last four
        // bytes and never saw them), it is rejected.
        public void Png_DataAfterTheDeflateStream_IsRejected()
        {
            var rng = new Random(169);
            var levels = new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal };
            for (int trial = 0; trial < 300; trial++)
            {
                int w = rng.Next(1, 40), h = rng.Next(1, 40);
                var raw = new byte[h * (1 + 4 * w)];
                bool noisy = rng.Next(2) == 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < 4 * w; x++) raw[y * (1 + 4 * w) + 1 + x] = (byte)(noisy ? rng.Next(256) : (x / 4 + y) % 3 * 70);
                byte[] deflate = Deflate(raw, levels[trial % 3]);
                byte[] zlib = new byte[] { 0x78, 0x01 }.Concat(deflate).Concat(Adler(raw)).ToArray();
                int split = rng.Next(1, zlib.Length + 1);
                ImageBuffer image = PngReader.Decode(Png(Ihdr((uint)w, (uint)h), zlib, split));
                ImageBuffer streamed = PngReader.Decode(new MemoryStream(Png(Ihdr((uint)w, (uint)h), zlib, split)));
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < 4 * w; x++)
                    {
                        Check.Equal(raw[y * (1 + 4 * w) + 1 + x], image.Pixels[y * 4 * w + x], "trial " + trial);
                        Check.Equal(raw[y * (1 + 4 * w) + 1 + x], streamed.Pixels[y * 4 * w + x], "trial " + trial + ", Stream");
                    }
                var junk = new byte[rng.Next(1, 9)];
                rng.NextBytes(junk);
                byte[] bad = new byte[] { 0x78, 0x01 }.Concat(deflate).Concat(junk).Concat(Adler(raw)).ToArray();
                Rejected(Png(Ihdr((uint)w, (uint)h), bad, rng.Next(1, bad.Length + 1)), "trial " + trial + ", junk " + junk.Length);
            }
        }

        // A deflate stream must end with a block marked final (RFC 1951); one with none is truncated, and was accepted
        // when the Adler-32 matched.
        public void Png_DeflateStreamWithoutAFinalBlock_IsRejected()
        {
            var rng = new Random(1169);
            for (int trial = 0; trial < 200; trial++)
            {
                int w = rng.Next(1, 20), h = rng.Next(1, 20);
                var raw = new byte[h * (1 + 4 * w)];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < 4 * w; x++) raw[y * (1 + 4 * w) + 1 + x] = (byte)rng.Next(256);
                // Stored blocks of random sizes; the last is final on even trials only.
                var deflate = new MemoryStream();
                for (int at = 0; at < raw.Length;)
                {
                    int n = Math.Min(raw.Length - at, rng.Next(1, 200));
                    bool last = at + n == raw.Length;
                    deflate.WriteByte((byte)(last && trial % 2 == 0 ? 1 : 0));
                    deflate.WriteByte((byte)n); deflate.WriteByte((byte)(n >> 8));
                    deflate.WriteByte((byte)~n); deflate.WriteByte((byte)(~n >> 8));
                    deflate.Write(raw, at, n);
                    at += n;
                }
                byte[] zlib = new byte[] { 0x78, 0x01 }.Concat(deflate.ToArray()).Concat(Adler(raw)).ToArray();
                byte[] png = Png(Ihdr((uint)w, (uint)h), zlib, rng.Next(1, zlib.Length + 1));
                if (trial % 2 == 0)
                {
                    Check.Equal(raw[1], PngReader.Decode(png).Pixels[0], "final, trial " + trial);
                    Check.Equal(raw[1], PngReader.Decode(new MemoryStream(png)).Pixels[0], "final, trial " + trial + ", Stream");
                }
                else Rejected(png, "trial " + trial);
            }
        }

        // Decode(byte[]) read the width as a signed int: 2^31 was reported as "-2147483648x1", where Decode(Stream) said
        // "2147483648x1".
        public void Png_HugeWidth_IsReportedAsInTheFile()
        {
            byte[] raw = { 0, 1, 2, 3, 4 };
            byte[] zlib = new byte[] { 0x78, 0x01 }.Concat(Deflate(raw, CompressionLevel.Optimal)).Concat(Adler(raw)).ToArray();
            foreach (uint w in new[] { 0x80000000u, 0xFFFFFFFFu })
            {
                byte[] png = Png(Ihdr(w, 1), zlib, zlib.Length);
                string expected = w.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x1";
                var a = Check.Throws<InvalidDataException>(() => PngReader.Decode(png));
                var b = Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(png)));
                Check.True(a.Message.Contains(expected), a.Message);
                Check.True(b.Message.Contains(expected), b.Message);
            }
        }
    }
}
