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
    /// for the zlib stream) are computed here. Rows are compressed as they are read and written out in 64 KiB IDAT
    /// chunks, so any <see cref="ImageBuffer"/> can be written and memory stays at the image plus one chunk.
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

            WriteImageData(image, stream);

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

        private const int ChunkSize = 1 << 16;

        // The zlib stream (header, deflate, Adler-32) of the filtered scanlines, written as it is produced. Building the
        // filtered scanlines and the compressed stream in memory first capped images near 2 GB with an exception thrown
        // after the header was already written (Save left a 33-byte .png), and OutOfMemoryException just below that.
        private static void WriteImageData(ImageBuffer image, Stream stream)
        {
            int rowBytes = image.Width * 4;
            byte[] pixels = image.Pixels;
            var sink = new MemoryStream();
            sink.WriteByte(0x78); // zlib header (CMF)
            sink.WriteByte(0x9C); // FLG (checkbits make 0x789C a multiple of 31)
            uint a = 1, b = 0;
            using (var deflate = new DeflateStream(sink, CompressionLevel.Optimal, leaveOpen: true))
            {
                void Put(byte[] data, int offset, int count)
                {
                    for (int done = 0; done < count;)
                    {
                        int n = Math.Min(ChunkSize, count - done);
                        Adler32(data, offset + done, n, ref a, ref b);
                        deflate.Write(data, offset + done, n);
                        Drain(sink, stream, all: false);
                        done += n;
                    }
                }

                // Each row is led by its filter byte (0: None). Narrow rows are batched to about one chunk per write.
                if (rowBytes + 1 <= ChunkSize)
                {
                    int rowsPerBatch = ChunkSize / (rowBytes + 1);
                    var batch = new byte[Math.Min(rowsPerBatch, image.Height) * (rowBytes + 1)];
                    for (int y = 0; y < image.Height;)
                    {
                        int rows = Math.Min(rowsPerBatch, image.Height - y), n = 0;
                        for (int r = 0; r < rows; r++, y++)
                        {
                            batch[n++] = 0;
                            Buffer.BlockCopy(pixels, y * rowBytes, batch, n, rowBytes);
                            n += rowBytes;
                        }
                        Put(batch, 0, n);
                    }
                }
                else
                {
                    var filter = new byte[1];
                    for (int y = 0; y < image.Height; y++)
                    {
                        Put(filter, 0, 1);
                        Put(pixels, y * rowBytes, rowBytes);
                    }
                }
            }
            var adler = new byte[4];
            WriteBe32(adler, 0, (b << 16) | a);
            sink.Write(adler, 0, 4);
            Drain(sink, stream, all: true);
        }

        private static void Drain(MemoryStream sink, Stream stream, bool all)
        {
            byte[] buffer = sink.GetBuffer();
            int length = (int)sink.Length, start = 0;
            for (; length - start >= ChunkSize; start += ChunkSize) WriteChunk(stream, "IDAT", buffer, start, ChunkSize);
            if (all && length > start) { WriteChunk(stream, "IDAT", buffer, start, length - start); start = length; }
            Buffer.BlockCopy(buffer, start, buffer, 0, length - start);
            sink.SetLength(length - start);
        }

        private static void WriteChunk(Stream stream, string type, byte[] data) => WriteChunk(stream, type, data, 0, data.Length);

        private static void WriteChunk(Stream stream, string type, byte[] data, int offset, int count)
        {
            var length = new byte[4];
            WriteBe32(length, 0, (uint)count);
            stream.Write(length, 0, 4);

            var typeAndData = new byte[4 + count];
            for (int i = 0; i < 4; i++) typeAndData[i] = (byte)type[i];
            Buffer.BlockCopy(data, offset, typeAndData, 4, count);
            stream.Write(typeAndData, 0, typeAndData.Length);

            var crc = new byte[4];
            WriteBe32(crc, 0, Crc32(typeAndData));
            stream.Write(crc, 0, 4);
        }

        private static void WriteBe32(byte[] buffer, int offset, uint value)
        {
            unchecked
            {
                buffer[offset] = (byte)(value >> 24);
                buffer[offset + 1] = (byte)(value >> 16);
                buffer[offset + 2] = (byte)(value >> 8);
                buffer[offset + 3] = (byte)value;
            }
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

        private static void Adler32(byte[] data, int offset, int count, ref uint a, ref uint b)
        {
            const uint mod = 65521;
            for (int i = offset, stop = offset + count; i < stop;)
            {
                int end = Math.Min(stop, i + 5552);                    // the most bytes before b can pass 2^32
                for (; i < end; i++) { a += data[i]; b += a; }
                a %= mod; b %= mod;
            }
        }
    }
}
