using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ToolBelt.Visualization;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Visualization
{
    public sealed class PngWriterTests
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public void EncodesValidPngThatRoundTrips()
        {
            var img = new ImageBuffer(4, 3, Rgba.Blue);
            img.SetPixel(0, 0, Rgba.Red);
            img.SetPixel(3, 2, new Rgba(1, 2, 3, 4));

            byte[] png = PngWriter.Encode(img);
            var (w, h, rgba) = DecodeAndValidate(png);

            Check.Equal(4, w);
            Check.Equal(3, h);
            Check.True(rgba.SequenceEqual(img.Pixels), "decoded pixels must match the source");
        }

        public void SignaturePresent()
        {
            byte[] png = PngWriter.Encode(new ImageBuffer(2, 2, Rgba.White));
            Check.True(png.AsSpan(0, 8).SequenceEqual(Signature));
        }

        public void LargerImageWithDrawing()
        {
            var img = new ImageBuffer(32, 24, Rgba.White);
            img.DrawRectangle(4, 4, 20, 12, Rgba.Black, filled: true);
            img.DrawLine(0, 0, 31, 23, Rgba.Red);

            var (w, h, rgba) = DecodeAndValidate(PngWriter.Encode(img));
            Check.Equal(32, w);
            Check.Equal(24, h);
            Check.True(rgba.SequenceEqual(img.Pixels));
        }

        public void SaveWritesReadableFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "toolbelt-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                PngWriter.Save(new ImageBuffer(3, 3, Rgba.Green), path);
                var (w, h, _) = DecodeAndValidate(File.ReadAllBytes(path));
                Check.Equal(3, w);
                Check.Equal(3, h);
            }
            finally { File.Delete(path); }
        }

        public void Null_Throws()
        {
            using var ms = new MemoryStream();
            Check.Throws<ArgumentNullException>(() => PngWriter.Write(null!, ms));
        }

        // Independent PNG validator: verifies the signature, every chunk's CRC-32, the zlib Adler-32, and
        // reconstructs the pixels from the inflated scanlines. Uses its own checksum code (differential).
        private static (int Width, int Height, byte[] Rgba) DecodeAndValidate(byte[] png)
        {
            Check.True(png.AsSpan(0, 8).SequenceEqual(Signature), "PNG signature");

            int pos = 8;
            byte[]? ihdr = null;
            using var idat = new MemoryStream();
            while (pos < png.Length)
            {
                int len = ReadBe32(png, pos); pos += 4;
                byte[] typeAndData = new byte[4 + len];
                Array.Copy(png, pos, typeAndData, 0, 4 + len);
                string type = new string(new[] { (char)png[pos], (char)png[pos + 1], (char)png[pos + 2], (char)png[pos + 3] });
                pos += 4;
                byte[] data = new byte[len];
                Array.Copy(png, pos, data, 0, len); pos += len;
                uint storedCrc = (uint)ReadBe32(png, pos); pos += 4;

                Check.Equal(storedCrc, Crc32(typeAndData));
                if (type == "IHDR") ihdr = data;
                else if (type == "IDAT") idat.Write(data, 0, data.Length);
                else if (type == "IEND") break;
            }

            Check.NotNull(ihdr);
            int width = ReadBe32(ihdr!, 0), height = ReadBe32(ihdr!, 4);
            Check.Equal(8, ihdr![8]); // bit depth
            Check.Equal(6, ihdr![9]); // color type RGBA

            byte[] zlib = idat.ToArray();
            byte[] deflated = new byte[zlib.Length - 6]; // strip 2-byte header + 4-byte adler
            Array.Copy(zlib, 2, deflated, 0, deflated.Length);
            byte[] filtered = Inflate(deflated);
            Check.Equal((uint)ReadBe32(zlib, zlib.Length - 4), Adler32(filtered));

            int rowBytes = width * 4;
            byte[] rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                Check.Equal(0, filtered[y * (rowBytes + 1)]); // filter None
                Array.Copy(filtered, y * (rowBytes + 1) + 1, rgba, y * rowBytes, rowBytes);
            }
            return (width, height, rgba);
        }

        private static byte[] Inflate(byte[] deflated)
        {
            using var input = new MemoryStream(deflated);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        private static int ReadBe32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint Adler32(byte[] data)
        {
            const uint mod = 65521;
            uint a = 1, b = 0;
            foreach (byte d in data) { a = (a + d) % mod; b = (b + a) % mod; }
            return (b << 16) | a;
        }
    }
}
