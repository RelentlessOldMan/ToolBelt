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
    /// and the chunk layout is checked (IHDR first and once, PLTE and tRNS once and before the image data, consecutive
    /// IDAT chunks, a closing IEND). Image data is inflated one scanline at a time straight into the image (capped at
    /// <see cref="MaxPixels"/>), so memory is the image plus the compressed data. Deflate expands at most 1032-fold, so
    /// data too short to hold the image fails as truncated before the image is allocated: a tiny file claiming a huge
    /// image can't force a huge allocation. Decompressed data past the last scanline is checksummed and
    /// ignored, as libpng does. Ancillary chunks (gamma, text, ICC profiles) are ignored.
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
            bool sawHeader = false, sawEnd = false, sawData = false, dataEnded = false;
            int pos = 8;
            while (pos + 12 <= data.Length && !sawEnd)
            {
                int length = Be32(data, pos);
                if (length < 0 || pos + 12L + length > data.Length) throw new InvalidDataException("Truncated PNG chunk.");
                for (int k = pos + 4; k < pos + 8; k++)                                 // chunk types are ASCII letters only
                    if (!((data[k] >= 'A' && data[k] <= 'Z') || (data[k] >= 'a' && data[k] <= 'z')))
                        throw new InvalidDataException("Bad PNG chunk type.");
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                if (Crc32(data, pos + 4, length + 4) != unchecked((uint)Be32(data, pos + 8 + length))) throw new InvalidDataException($"CRC mismatch in PNG chunk {type}.");
                if (!sawHeader && type != "IHDR") throw new InvalidDataException($"PNG chunk {type} before IHDR.");
                if (sawData && type != "IDAT") dataEnded = true;
                int body = pos + 8;
                switch (type)
                {
                    case "IHDR":
                        // A second IHDR (after the image data) silently re-described the pixels already read.
                        if (sawHeader) throw new InvalidDataException("Duplicate IHDR.");
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
                        if (palette != null || sawData || trns != null) throw new InvalidDataException("PLTE out of order or repeated.");
                        if (colorType == 0 || colorType == 4) throw new InvalidDataException("PLTE in a greyscale PNG.");
                        if (length == 0 || length % 3 != 0 || length > 768) throw new InvalidDataException("Bad PLTE length.");
                        palette = new byte[length];
                        Array.Copy(data, body, palette, 0, length);
                        break;
                    case "tRNS":
                        if (trns != null || sawData || (colorType == 3 && palette == null)) throw new InvalidDataException("tRNS out of order or repeated.");
                        trns = new byte[length];
                        Array.Copy(data, body, trns, 0, length);
                        break;
                    case "IDAT":
                        if (dataEnded) throw new InvalidDataException("IDAT chunks are not consecutive.");
                        sawData = true;
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
            if (!sawEnd) throw new InvalidDataException("PNG is truncated (no IEND).");
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
            // A row of a wide 16-bit RGBA image (W × 64 bits) overflows int long before MaxPixels does.
            if ((long)width * bitsPerPixel + 7 > int.MaxValue || expected > int.MaxValue)
                throw new InvalidDataException($"PNG image {width}x{height} is too large.");
            using var zlib = new ZlibReader(idat.ToArray(), expected);

            var image = new ImageBuffer(width, height);
            byte[] px = image.Pixels;
            int filterBpp = Math.Max(1, bitsPerPixel / 8);
            var filterByte = new byte[1];
            foreach (var p in passes)
            {
                if (p.W == 0 || p.H == 0) continue;
                int rowBytes = (p.W * bitsPerPixel + 7) / 8;
                var prev = new byte[rowBytes];
                var cur = new byte[rowBytes];
                for (int r = 0; r < p.H; r++)
                {
                    zlib.ReadExactly(filterByte, 1);
                    zlib.ReadExactly(cur, rowBytes);
                    Unfilter(filterByte[0], cur, prev, filterBpp);
                    int y = p.Y0 + r * p.Dy;
                    for (int c = 0; c < p.W; c++)
                        WritePixel(px, (y * width + p.X0 + c * p.Dx) * 4, cur, c, colorType, bitDepth, channels, palette, trns);
                    (prev, cur) = (cur, prev);
                }
            }
            zlib.Finish();
            return image;
        }

        private static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
        {
            unchecked
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

        // The zlib stream of the image data, read a scanline at a time with a running Adler-32. Inflating it whole into a
        // growing buffer peaked at about three times the decoded size (2.2 GB for a 10000 x 10000 16-bit RGBA file).
        private sealed class ZlibReader : IDisposable
        {
            private readonly DeflateStream _deflate;
            private readonly uint _adler;
            private readonly long _expected;
            private long _read;
            private uint _a = 1, _b;

            public ZlibReader(byte[] zlib, long expected)
            {
                if (zlib.Length < 6) throw new InvalidDataException("PNG image data is empty.");
                int cmf = zlib[0], flg = zlib[1];
                // CINFO above 7 (a window past 32 KiB) is invalid zlib.
                if ((cmf & 0x0F) != 8 || (cmf >> 4) > 7 || ((cmf << 8) | flg) % 31 != 0) throw new InvalidDataException("Bad zlib header in PNG data.");
                if ((flg & 0x20) != 0) throw new InvalidDataException("Preset zlib dictionaries are not allowed in PNG.");
                // A deflate stream inflates to at most 1032 bytes per byte (a 258-byte match in 2 bits). Checking first
                // keeps the image from being allocated for data that can't fill it: streaming into a pre-allocated image
                // let a 65-byte file claiming 7000 x 7000 allocate 187 MB.
                if (expected > 1032L * (zlib.Length - 6) + 258)
                    throw new InvalidDataException($"PNG image data is truncated ({zlib.Length} bytes can't inflate to {expected}).");
                _adler = unchecked((uint)Be32(zlib, zlib.Length - 4));
                _expected = expected;
                _deflate = new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), CompressionMode.Decompress);
            }

            public void ReadExactly(byte[] buffer, int count)
            {
                for (int got = 0; got < count;)
                {
                    int n = Read(buffer, got, count - got);
                    if (n == 0) throw new InvalidDataException($"PNG image data is truncated ({_read} of {_expected} bytes).");
                    got += n;
                }
            }

            // Data past the last scanline is drained into the checksum and ignored (libpng and Pillow accept it); it used
            // to fail as a "checksum mismatch". Inflating is bounded by the compressed size, so this can't run away in memory.
            public void Finish()
            {
                var scratch = new byte[1 << 16];
                while (Read(scratch, 0, scratch.Length) > 0) { }
                if (((_b << 16) | _a) != _adler) throw new InvalidDataException("zlib checksum mismatch in PNG data.");
            }

            private int Read(byte[] buffer, int offset, int count)
            {
                int n;
                try { n = _deflate.Read(buffer, offset, count); }
                catch (InvalidDataException ex)                                 // net8 says "unsupported compression method"
                {
                    throw new InvalidDataException("PNG image data is corrupt (bad deflate stream).", ex);
                }
                for (int i = offset, stop = offset + n; i < stop;)
                {
                    int end = Math.Min(stop, i + 5552);
                    for (; i < end; i++) { _a += buffer[i]; _b += _a; }
                    _a %= 65521; _b %= 65521;
                }
                _read += n;
                return n;
            }

            public void Dispose() => _deflate.Dispose();
        }

        private static int Be32(byte[] b, int i) => unchecked((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]);

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

    }
}
