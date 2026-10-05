// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs.
using System;
using System.IO;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// Windows bitmap (.bmp) read/write for <see cref="ImageBuffer"/> — the format every viewer, MCU tool and legacy
    /// instrument accepts, with no compression to get wrong. Writes 32-bit BGRA with a BITMAPV4 header (alpha preserved),
    /// or 24-bit BGR when <c>includeAlpha</c> is false (maximum compatibility). Reads uncompressed 24- and 32-bit files
    /// (bottom-up or top-down, BI_RGB or BI_BITFIELDS with the standard masks) and 8-bit palettised ones.
    /// </summary>
    public static class Bmp
    {
        public static byte[] Encode(ImageBuffer image, bool includeAlpha = true)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            int w = image.Width, h = image.Height;
            int bpp = includeAlpha ? 32 : 24;
            int stride = (w * bpp / 8 + 3) & ~3;
            int headerSize = includeAlpha ? 108 : 40;                       // BITMAPV4HEADER : BITMAPINFOHEADER
            int offset = 14 + headerSize;
            long size = offset + (long)stride * h;
            if (size > int.MaxValue) throw new ArgumentException("Image too large for BMP.", nameof(image));
            var data = new byte[size];

            // BITMAPFILEHEADER
            data[0] = (byte)'B'; data[1] = (byte)'M';
            Le32(data, 2, (int)size);
            Le32(data, 10, offset);
            // BITMAPINFOHEADER / V4
            Le32(data, 14, headerSize);
            Le32(data, 18, w);
            Le32(data, 22, h);                                               // positive: bottom-up (most compatible)
            Le16(data, 26, 1);
            Le16(data, 28, bpp);
            Le32(data, 30, includeAlpha ? 3 : 0);                            // BI_BITFIELDS : BI_RGB
            Le32(data, 34, stride * h);
            Le32(data, 38, 2835);                                            // 72 dpi
            Le32(data, 42, 2835);
            if (includeAlpha)
            {
                Le32(data, 54, 0x00FF0000);                                  // red mask
                Le32(data, 58, 0x0000FF00);
                Le32(data, 62, 0x000000FF);
                Le32(data, 66, unchecked((int)0xFF000000));                  // alpha mask
                Le32(data, 70, 0x73524742);                                  // 'sRGB'
            }

            byte[] px = image.Pixels;
            for (int y = 0; y < h; y++)
            {
                int row = offset + (h - 1 - y) * stride;
                for (int x = 0; x < w; x++)
                {
                    int s = (y * w + x) * 4, d = row + x * (bpp / 8);
                    data[d] = px[s + 2];
                    data[d + 1] = px[s + 1];
                    data[d + 2] = px[s];
                    if (includeAlpha) data[d + 3] = px[s + 3];
                }
            }
            return data;
        }

        public static void Save(ImageBuffer image, string path, bool includeAlpha = true)
            => File.WriteAllBytes(path ?? throw new ArgumentNullException(nameof(path)), Encode(image, includeAlpha));

        public static ImageBuffer Decode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length < 54 || data[0] != 'B' || data[1] != 'M') throw new InvalidDataException("Not a BMP file.");
            int offset = Le32(data, 10);
            int headerSize = Le32(data, 14);
            if (headerSize < 40) throw new InvalidDataException("Unsupported BMP header (OS/2 core headers aren't supported).");
            int w = Le32(data, 18), rawH = Le32(data, 22);
            int bpp = Le16(data, 28), compression = Le32(data, 30);
            if (w <= 0 || rawH == 0 || w > 1 << 15 || Math.Abs((long)rawH) > 1 << 15) throw new InvalidDataException($"Implausible BMP size {w}x{rawH}.");
            bool topDown = rawH < 0;
            int h = Math.Abs(rawH);
            if (bpp != 24 && bpp != 32 && bpp != 8) throw new InvalidDataException($"Unsupported BMP bit depth {bpp}.");
            if (!(compression == 0 || (compression == 3 && bpp == 32))) throw new InvalidDataException($"Unsupported BMP compression {compression}.");
            bool hasAlpha = false;
            if (bpp == 32 && compression == 3)
            {
                uint r = (uint)Le32(data, 54), g = (uint)Le32(data, 58), b = (uint)Le32(data, 62);
                if (r != 0x00FF0000 || g != 0x0000FF00 || b != 0x000000FF) throw new InvalidDataException("Unsupported BMP channel masks.");
                hasAlpha = headerSize >= 56 && (uint)Le32(data, 66) == 0xFF000000;
            }
            else if (bpp == 32) hasAlpha = false;                            // BI_RGB 32-bit: the 4th byte is unused by spec

            byte[]? palette = null;
            if (bpp == 8)
            {
                int colors = Le32(data, 46);
                if (colors == 0) colors = 256;
                int palStart = 14 + headerSize;
                if (palStart + colors * 4 > data.Length) throw new InvalidDataException("Truncated BMP palette.");
                palette = new byte[256 * 4];
                Array.Copy(data, palStart, palette, 0, Math.Min(colors, 256) * 4);
            }

            int stride = (w * bpp / 8 + 3) & ~3;
            if (offset + (long)stride * h > data.Length) throw new InvalidDataException("Truncated BMP pixel data.");
            var image = new ImageBuffer(w, h);
            byte[] px = image.Pixels;
            for (int y = 0; y < h; y++)
            {
                int row = offset + (topDown ? y : h - 1 - y) * stride;
                for (int x = 0; x < w; x++)
                {
                    int d = (y * w + x) * 4;
                    if (palette != null)
                    {
                        int p = data[row + x] * 4;
                        px[d] = palette[p + 2]; px[d + 1] = palette[p + 1]; px[d + 2] = palette[p]; px[d + 3] = 255;
                    }
                    else
                    {
                        int s = row + x * (bpp / 8);
                        px[d] = data[s + 2]; px[d + 1] = data[s + 1]; px[d + 2] = data[s];
                        px[d + 3] = hasAlpha ? data[s + 3] : (byte)255;
                    }
                }
            }
            return image;
        }

        public static ImageBuffer Load(string path) => Decode(File.ReadAllBytes(path ?? throw new ArgumentNullException(nameof(path))));

        private static void Le32(byte[] b, int i, int v) { b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); b[i + 2] = (byte)(v >> 16); b[i + 3] = (byte)(v >> 24); }
        private static void Le16(byte[] b, int i, int v) { b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); }
        private static int Le32(byte[] b, int i) => b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24);
        private static int Le16(byte[] b, int i) => b[i] | (b[i + 1] << 8);
    }
}
