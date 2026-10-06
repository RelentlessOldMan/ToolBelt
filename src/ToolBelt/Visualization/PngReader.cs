// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// Decodes PNG files to an <see cref="ImageBuffer"/> (8-bit RGBA) with only the framework's deflate stream — the reading
    /// half of <see cref="PngWriter"/>, for loading reference images, masks and test fixtures on any OS. Supports every
    /// standard PNG: greyscale, RGB, palette, grey+alpha and RGBA; bit depths 1, 2, 4, 8 and 16 (16-bit is reduced to 8);
    /// all five scanline filters; Adam7 interlacing; and tRNS transparency. Chunk CRCs and the zlib checksum are verified,
    /// and decompression is bounded by the header (itself capped at <see cref="MaxPixels"/>) and grows its buffer only as data
    /// actually inflates, so a tiny file claiming a huge image fails without a huge allocation. Ancillary chunks
    /// (gamma, text, ICC profiles) are ignored.
    /// </summary>
    public static class PngReader
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Largest width × height accepted (default 100 megapixels).</summary>
        public const long MaxPixels = 100_000_000;

        public static ImageBuffer Load(string path) => Decode(File.ReadAllBytes(path ?? throw new ArgumentNullException(nameof(path))));

        public static ImageBuffer Decode(Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Decode(ms.ToArray());
        }

        public static ImageBuffer Decode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length < 8) throw new InvalidDataException("Not a PNG file.");
            for (int i = 0; i < 8; i++) if (data[i] != Signature[i]) throw new InvalidDataException("Not a PNG file (bad signature).");

            int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
            byte[]? palette = null, trns = null;
            var idat = new MemoryStream();
            bool sawHeader = false, sawEnd = false;
            int pos = 8;
            while (pos + 12 <= data.Length && !sawEnd)
            {
                int length = Be32(data, pos);
                if (length < 0 || pos + 12L + length > data.Length) throw new InvalidDataException("Truncated PNG chunk.");
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                if (Crc32(data, pos + 4, length + 4) != (uint)Be32(data, pos + 8 + length)) throw new InvalidDataException($"CRC mismatch in PNG chunk {type}.");
                int body = pos + 8;
                switch (type)
                {
                    case "IHDR":
                        if (length != 13) throw new InvalidDataException("Bad IHDR length.");
                        width = Be32(data, body);
                        height = Be32(data, body + 4);
                        bitDepth = data[body + 8];
                        colorType = data[body + 9];
                        if (data[body + 10] != 0 || data[body + 11] != 0) throw new InvalidDataException("Unsupported PNG compression or filter method.");
                        interlace = data[body + 12];
                        sawHeader = true;
                        break;
                    case "PLTE":
                        palette = new byte[length];
                        Array.Copy(data, body, palette, 0, length);
                        break;
                    case "tRNS":
                        trns = new byte[length];
                        Array.Copy(data, body, trns, 0, length);
                        break;
                    case "IDAT":
                        if (!sawHeader) throw new InvalidDataException("IDAT before IHDR.");
                        idat.Write(data, body, length);
                        break;
                    case "IEND":
                        sawEnd = true;
                        break;
                    default:
                        if ((data[pos + 4] & 0x20) == 0) throw new InvalidDataException($"Unknown critical PNG chunk {type}.");
                        break;                                                          // ancillary: ignore
                }
                pos += 12 + length;
            }
            if (!sawHeader) throw new InvalidDataException("PNG has no IHDR.");
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) throw new InvalidDataException($"Unsupported PNG size {width}x{height}.");
            if (interlace > 1) throw new InvalidDataException("Unknown PNG interlace method.");
            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException($"Bad PNG colour type {colorType}.") };
            bool depthOk = colorType switch
            {
                0 => bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8 || bitDepth == 16,
                3 => bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8,
                _ => bitDepth == 8 || bitDepth == 16,
            };
            if (!depthOk) throw new InvalidDataException($"Bad bit depth {bitDepth} for colour type {colorType}.");
            if (colorType == 3 && palette == null) throw new InvalidDataException("Palette image without PLTE.");

            // Pass geometry (Adam7 or a single pass) and the exact number of filtered bytes expected.
            var passes = new List<(int X0, int Y0, int Dx, int Dy, int W, int H)>();
            if (interlace == 0) passes.Add((0, 0, 1, 1, width, height));
            else
            {
                int[,] adam = { { 0, 0, 8, 8 }, { 4, 0, 8, 8 }, { 0, 4, 4, 8 }, { 2, 0, 4, 4 }, { 0, 2, 2, 4 }, { 1, 0, 2, 2 }, { 0, 1, 1, 2 } };
                for (int p = 0; p < 7; p++)
                {
                    int x0 = adam[p, 0], y0 = adam[p, 1], dx = adam[p, 2], dy = adam[p, 3];
                    int pw = (width - x0 + dx - 1) / dx, ph = (height - y0 + dy - 1) / dy;
                    passes.Add((x0, y0, dx, dy, Math.Max(0, pw), Math.Max(0, ph)));
                }
            }
            int bitsPerPixel = channels * bitDepth;
            long expected = 0;
            foreach (var p in passes) if (p.W > 0 && p.H > 0) expected += (long)p.H * (1 + ((long)p.W * bitsPerPixel + 7) / 8);
            byte[] raw = Inflate(idat.ToArray(), expected);

            var image = new ImageBuffer(width, height);
            byte[] px = image.Pixels;
            int filterBpp = Math.Max(1, bitsPerPixel / 8);
            int offset = 0;
            foreach (var p in passes)
            {
                if (p.W == 0 || p.H == 0) continue;
                int rowBytes = (p.W * bitsPerPixel + 7) / 8;
                var prev = new byte[rowBytes];
                var cur = new byte[rowBytes];
                for (int r = 0; r < p.H; r++)
                {
                    byte filter = raw[offset];
                    Array.Copy(raw, offset + 1, cur, 0, rowBytes);
                    offset += 1 + rowBytes;
                    Unfilter(filter, cur, prev, filterBpp);
                    int y = p.Y0 + r * p.Dy;
                    for (int c = 0; c < p.W; c++)
                        WritePixel(px, (y * width + p.X0 + c * p.Dx) * 4, cur, c, colorType, bitDepth, channels, palette, trns);
                    (prev, cur) = (cur, prev);
                }
            }
            return image;
        }

        private static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
        {
            int n = cur.Length;
            switch (filter)
            {
                case 0: return;
                case 1: for (int i = bpp; i < n; i++) cur[i] += cur[i - bpp]; return;
                case 2: for (int i = 0; i < n; i++) cur[i] += prev[i]; return;
                case 3:
                    for (int i = 0; i < n; i++) cur[i] += (byte)(((i >= bpp ? cur[i - bpp] : 0) + prev[i]) >> 1);
                    return;
                case 4:
                    for (int i = 0; i < n; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                        cur[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                    }
                    return;
                default: throw new InvalidDataException($"Bad PNG filter type {filter}.");
            }
        }

        private static void WritePixel(byte[] px, int d, byte[] row, int col, int colorType, int depth, int channels,
            byte[]? palette, byte[]? trns)
        {
            if (depth < 8)
            {
                int perByte = 8 / depth, shift = 8 - depth * (col % perByte + 1);
                int v = (row[col / perByte] >> shift) & ((1 << depth) - 1);
                if (colorType == 3) { Palette(px, d, v, palette!, trns); return; }
                byte g = (byte)(v * 255 / ((1 << depth) - 1));
                px[d] = px[d + 1] = px[d + 2] = g;
                px[d + 3] = trns != null && trns.Length >= 2 && ((trns[0] << 8) | trns[1]) == v ? (byte)0 : (byte)255;
                return;
            }
            int bytes = depth / 8, i = col * channels * bytes;
            int Sample(int ch) => bytes == 1 ? row[i + ch] : (row[i + ch * 2] << 8) | row[i + ch * 2 + 1];
            byte Eight(int ch) => row[i + ch * bytes];                                // high byte for 16-bit
            switch (colorType)
            {
                case 3: Palette(px, d, row[col], palette!, trns); return;
                case 0:
                    px[d] = px[d + 1] = px[d + 2] = Eight(0);
                    px[d + 3] = trns != null && trns.Length >= 2 && ((trns[0] << 8) | trns[1]) == Sample(0) ? (byte)0 : (byte)255;
                    return;
                case 2:
                    px[d] = Eight(0); px[d + 1] = Eight(1); px[d + 2] = Eight(2);
                    px[d + 3] = trns != null && trns.Length >= 6
                        && ((trns[0] << 8) | trns[1]) == Sample(0) && ((trns[2] << 8) | trns[3]) == Sample(1) && ((trns[4] << 8) | trns[5]) == Sample(2)
                        ? (byte)0 : (byte)255;
                    return;
                case 4:
                    px[d] = px[d + 1] = px[d + 2] = Eight(0);
                    px[d + 3] = Eight(1);
                    return;
                default:
                    px[d] = Eight(0); px[d + 1] = Eight(1); px[d + 2] = Eight(2); px[d + 3] = Eight(3);
                    return;
            }
        }

        private static void Palette(byte[] px, int d, int index, byte[] palette, byte[]? trns)
        {
            if (index * 3 + 2 >= palette.Length) throw new InvalidDataException($"Palette index {index} out of range.");
            px[d] = palette[index * 3];
            px[d + 1] = palette[index * 3 + 1];
            px[d + 2] = palette[index * 3 + 2];
            px[d + 3] = trns != null && index < trns.Length ? trns[index] : (byte)255;
        }

        private static byte[] Inflate(byte[] zlib, long expected)
        {
            if (zlib.Length < 6) throw new InvalidDataException("PNG image data is empty.");
            int cmf = zlib[0], flg = zlib[1];
            if ((cmf & 0x0F) != 8 || ((cmf << 8) | flg) % 31 != 0) throw new InvalidDataException("Bad zlib header in PNG data.");
            if ((flg & 0x20) != 0) throw new InvalidDataException("Preset zlib dictionaries are not allowed in PNG.");
            if (expected > int.MaxValue) throw new InvalidDataException("PNG image too large.");
            byte[] output = InflateExactly(new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), CompressionMode.Decompress), (int)expected);
            uint adler = (uint)Be32(zlib, zlib.Length - 4);
            if (Adler32(output) != adler) throw new InvalidDataException("zlib checksum mismatch in PNG data.");
            return output;
        }

        // Reads exactly `expected` bytes, growing the buffer only as data actually inflates — a tiny file whose header claims
        // a huge image fails as truncated without first allocating the full claimed size.
        private static byte[] InflateExactly(Stream deflate, int expected)
        {
            using (deflate)
            {
                var buffer = new byte[Math.Min(expected, 1 << 16)];
                int read = 0;
                while (read < expected)
                {
                    if (read == buffer.Length) Array.Resize(ref buffer, (int)Math.Min(expected, (long)buffer.Length * 2));
                    int n = deflate.Read(buffer, read, buffer.Length - read);
                    if (n == 0) throw new InvalidDataException($"PNG image data is truncated ({read} of {expected} bytes).");
                    read += n;
                }
                return buffer;
            }
        }

        private static int Be32(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static uint Crc32(byte[] data, int start, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = start; i < start + count; i++) crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            int i = 0;
            while (i < data.Length)
            {
                int end = Math.Min(data.Length, i + 5552);
                for (; i < end; i++) { a += data[i]; b += a; }
                a %= 65521; b %= 65521;
            }
            return (b << 16) | a;
        }
    }
}
