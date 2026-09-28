// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.IO.Compression;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// Encodes an <see cref="ImageBuffer"/> as a PNG using only the framework's deflate stream — so portable
    /// core code can emit raster images (plots, heat maps) on any operating system without a platform
    /// assembly. Writes 8-bit truecolor-with-alpha (color type 6) with the no-op scanline filter, which is
    /// perfectly acceptable for plots and heat maps. The two required checksums (CRC-32 for chunks, Adler-32
    /// for the zlib stream) are computed here.
    /// </summary>
    public static class PngWriter
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static void Write(ImageBuffer image, Stream stream)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            stream.Write(Signature, 0, Signature.Length);

            var ihdr = new byte[13];
            WriteBe32(ihdr, 0, (uint)image.Width);
            WriteBe32(ihdr, 4, (uint)image.Height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 6;  // color type: truecolor + alpha
            ihdr[10] = 0; // compression: deflate
            ihdr[11] = 0; // filter: adaptive (we use per-line filter 0)
            ihdr[12] = 0; // interlace: none
            WriteChunk(stream, "IHDR", ihdr);

            byte[] filtered = BuildFilteredScanlines(image);
            byte[] zlib = ZlibCompress(filtered);
            WriteChunk(stream, "IDAT", zlib);

            WriteChunk(stream, "IEND", Array.Empty<byte>());
        }

        public static byte[] Encode(ImageBuffer image)
        {
            using var ms = new MemoryStream();
            Write(image, ms);
            return ms.ToArray();
        }

        public static void Save(ImageBuffer image, string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            Write(image, fs);
        }

        private static byte[] BuildFilteredScanlines(ImageBuffer image)
        {
            int rowBytes = image.Width * 4;
            byte[] pixels = image.Pixels;
            var output = new byte[image.Height * (rowBytes + 1)];
            for (int y = 0; y < image.Height; y++)
            {
                int dst = y * (rowBytes + 1);
                output[dst] = 0; // filter type: None
                Buffer.BlockCopy(pixels, y * rowBytes, output, dst + 1, rowBytes);
            }
            return output;
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            byte[] deflated;
            using (var ms = new MemoryStream())
            {
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    deflate.Write(data, 0, data.Length);
                deflated = ms.ToArray();
            }

            var result = new byte[2 + deflated.Length + 4];
            result[0] = 0x78; // zlib header (CMF)
            result[1] = 0x9C; // FLG (checkbits make 0x789C a multiple of 31)
            Buffer.BlockCopy(deflated, 0, result, 2, deflated.Length);
            WriteBe32(result, result.Length - 4, Adler32(data));
            return result;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBe32(length, 0, (uint)data.Length);
            stream.Write(length, 0, 4);

            var typeAndData = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) typeAndData[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, typeAndData, 4, data.Length);
            stream.Write(typeAndData, 0, typeAndData.Length);

            var crc = new byte[4];
            WriteBe32(crc, 0, Crc32(typeAndData));
            stream.Write(crc, 0, 4);
        }

        private static void WriteBe32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        // ---- checksums (inlined to keep this file drop-in self-contained) ----

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint Adler32(byte[] data)
        {
            const uint mod = 65521;
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % mod;
                b = (b + a) % mod;
            }
            return (b << 16) | a;
        }
    }
}
