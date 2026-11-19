using System;
using System.IO;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>
    /// Round 159 review regressions (binary formats and images, ninth pass): each failed on the code before its fix.
    /// </summary>
    public sealed class BinaryImagesReview159Tests
    {
        private static int Painted(ImageBuffer image)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).A != 0) n++;
            return n;
        }

        // ---------- ImageBuffer.DrawLineClipped ----------

        // A backwards rectangle (left > right or top > bottom) is empty, but a vertical line threw DivideByZeroException
        // and random lines drew pixels.
        public void DrawLineClipped_ABackwardsRectangleDrawsNothing()
        {
            var image = new ImageBuffer(32, 32, Rgba.Transparent);
            image.DrawLineClipped(-0.0001, -20, -0.0001, 5, Rgba.Red, 0, 0, -5, 31);
            image.DrawLineClipped(-20, 3, 5, 3, Rgba.Red, 0, 10, 31, 2);
            Check.Equal(0, Painted(image));

            var rng = new Random(159);
            for (int trial = 0; trial < 20000; trial++)
            {
                int l = rng.Next(-8, 40), r = rng.Next(-8, 40), t = rng.Next(-8, 40), b = rng.Next(-8, 40);
                if (l <= r && t <= b) continue;
                double x0 = rng.NextDouble() * 60 - 14, y0 = rng.NextDouble() * 60 - 14;
                double x1 = rng.Next(4) == 0 ? x0 : rng.NextDouble() * 60 - 14;
                double y1 = rng.Next(4) == 0 ? y0 : rng.NextDouble() * 60 - 14;
                image.DrawLineClipped(x0, y0, x1, y1, Rgba.Red, l, t, r, b);
                Check.Equal(0, Painted(image), $"({x0:R},{y0:R})-({x1:R},{y1:R}) in [{l},{r}]x[{t},{b}]");
            }
        }

        // Inside the rectangle the clipped line is the line itself: drawing Bresenham between the rounded clip ends
        // shifted its runs ((37, 17)-(-1, 7) on a 20 x 15 canvas: every run a column off), 4,092 of 20,000 lines.
        public void DrawLineClipped_MatchesDrawLineInsideTheRectangle()
        {
            Check.True(SameInside(37, 17, -1, 7, 20, 15), "(37,17)-(-1,7)");
            Check.True(SameInside(-23, -24, 43, 51, 20, 15), "(-23,-24)-(43,51)");
            var rng = new Random(2159);
            int failures = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                int w = rng.Next(1, 30), h = rng.Next(1, 30);
                int x0 = rng.Next(-40, w + 40), y0 = rng.Next(-40, h + 40), x1 = rng.Next(-40, w + 40), y1 = rng.Next(-40, h + 40);
                if (!SameInside(x0, y0, x1, y1, w, h)) failures++;
            }
            Check.Equal(0, failures);
        }

        // DrawLine and DrawLineClipped (clipped to the canvas) agree on every pixel off the canvas's border, and on the
        // border too for a line that stays on the canvas.
        private static bool SameInside(int x0, int y0, int x1, int y1, int w, int h)
        {
            var whole = new ImageBuffer(w, h, Rgba.Transparent);
            whole.DrawLine(x0, y0, x1, y1, Rgba.Red);
            var clipped = new ImageBuffer(w, h, Rgba.Transparent);
            clipped.DrawLineClipped(x0, y0, x1, y1, Rgba.Red, 0, 0, w - 1, h - 1);
            bool on = Math.Min(x0, x1) >= 0 && Math.Max(x0, x1) < w && Math.Min(y0, y1) >= 0 && Math.Max(y0, y1) < h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((on || (x > 0 && x < w - 1 && y > 0 && y < h - 1)) && whole.GetPixel(x, y) != clipped.GetPixel(x, y)) return false;
            return true;
        }

        // ---------- Luhn ----------

        // A payload of the longest string had no room for the check digit: OutOfMemoryException.
        public void Luhn_AppendToTheLongestPayloadIsAnArgumentException()
        {
            string payload = new string('7', 0x3FFFFFDF);
            var e = Check.Throws<ArgumentException>(() => Luhn.AppendCheckDigit(payload));
            Check.Equal("payload", e.ParamName);
            Check.Equal("75", Luhn.AppendCheckDigit("7"));   // 7 doubled is 14, digits 5
        }

        // ---------- Base58 ----------

        // A text past the longest string threw OutOfMemoryException, where the other encoders throw ArgumentException.
        public void Base58_TooLongForOneStringIsAnArgumentException()
        {
            var e = Check.Throws<ArgumentException>(() => Base58.Encode(new byte[0x3FFFFFE0]));
            Check.Equal("data", e.ParamName);
        }

        // Guard: the length bound the encoder refuses by is an upper bound on the text it writes.
        public void Base58_TheLengthBoundHoldsEveryEncoding()
        {
            var rng = new Random(1159);
            for (int trial = 0; trial < 3000; trial++)
            {
                var data = new byte[rng.Next(1, 200)];
                rng.NextBytes(data);
                for (int z = rng.Next(0, Math.Min(4, data.Length + 1)), i = 0; i < z; i++) data[i] = 0;
                if (rng.Next(3) == 0 && data.Length > 0) data[Math.Min(data.Length - 1, 3)] = 255;
                int zeros = 0;
                while (zeros < data.Length && data[zeros] == 0) zeros++;
                int rest = data.Length - zeros;
                double bound = zeros + (rest == 0 ? 0 : Math.Ceiling(Math.Log(data[zeros] + 1, 58) + (rest - 1) * 1.3656582373098));
                int length = Base58.Encode(data).Length;
                Check.True(length <= bound, $"{length} > {bound} for {BitConverter.ToString(data)}");
                Check.True(length >= bound - 2, $"{length} far below {bound}");
            }
        }

        // ---------- Base32 and Crockford Base32 ----------

        // The result list was sized from the raw length, so separators the decoder skips allocated memory for bytes that
        // never came (16M separators: 10 MB for nothing). (.NET Framework has no per-thread allocation count.)
#if !NETFRAMEWORK
        public void Base32_SeparatorsDoNotSizeTheResult()
        {
            string spaces = new string(' ', 16_000_000) + "MZXW6===";
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] a = Base32.Decode(spaces);
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal("foo", System.Text.Encoding.ASCII.GetString(a));
            Check.True(used < 1_000_000, used.ToString(System.Globalization.CultureInfo.InvariantCulture));

            string dashes = new string('-', 16_000_000) + "CSQPY";
            before = GC.GetAllocatedBytesForCurrentThread();
            byte[] b = CrockfordBase32.Decode(dashes);
            used = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal("foo", System.Text.Encoding.ASCII.GetString(b));
            Check.True(used < 1_000_000, used.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
#endif

        // ---------- Bmp ----------

        private static byte[] BitfieldsBmp(int headerSize, int w, int h, bool masksInHeader)
        {
            int masks = masksInHeader ? 54 : 14 + headerSize;
            int offset = Math.Max(14 + headerSize, masks + 12);
            var data = new byte[offset + w * h * 4];
            data[0] = (byte)'B'; data[1] = (byte)'M';
            BitConverter.GetBytes(data.Length).CopyTo(data, 2);
            BitConverter.GetBytes(offset).CopyTo(data, 10);
            BitConverter.GetBytes(headerSize).CopyTo(data, 14);
            BitConverter.GetBytes(w).CopyTo(data, 18);
            BitConverter.GetBytes(-h).CopyTo(data, 22);                              // top-down
            BitConverter.GetBytes((short)1).CopyTo(data, 26);
            BitConverter.GetBytes((short)32).CopyTo(data, 28);
            BitConverter.GetBytes(3).CopyTo(data, 30);                               // BI_BITFIELDS
            BitConverter.GetBytes(0x00FF0000).CopyTo(data, masks);
            BitConverter.GetBytes(0x0000FF00).CopyTo(data, masks + 4);
            BitConverter.GetBytes(0x000000FF).CopyTo(data, masks + 8);
            for (int i = 0; i < w * h; i++)
            {
                data[offset + i * 4] = (byte)(i * 7);                                // blue
                data[offset + i * 4 + 1] = (byte)(i * 13);                           // green
                data[offset + i * 4 + 2] = (byte)(i * 29);                           // red
            }
            return data;
        }

        // A 32-bit BI_BITFIELDS BMP with a 44-byte header (masks after it) read its masks at 54, inside the header, and was
        // refused as "Unsupported BMP channel masks"; headers of 40, 52 and 56 bytes worked.
        public void Bmp_BitfieldsAfterAnyShortHeaderAreRead()
        {
            foreach (int size in new[] { 40, 41, 44, 48, 51 })
            {
                var image = Bmp.Decode(BitfieldsBmp(size, 3, 2, masksInHeader: false));
                for (int i = 0; i < 6; i++)
                    Check.Equal(new Rgba((byte)(i * 29), (byte)(i * 13), (byte)(i * 7)), image.GetPixel(i % 3, i / 3), $"header {size}, pixel {i}");
            }
            foreach (int size in new[] { 52, 56, 108 })
                Check.Equal(new Rgba(29, 13, 7), Bmp.Decode(BitfieldsBmp(size, 3, 2, masksInHeader: true)).GetPixel(1, 0), $"header {size}");
            // Masks after a 44-byte header that overlap the pixel data are refused, as after a 40-byte one.
            var overlap = BitfieldsBmp(44, 3, 2, masksInHeader: false);
            BitConverter.GetBytes(60).CopyTo(overlap, 10);
            Check.Throws<InvalidDataException>(() => Bmp.Decode(overlap));
        }

        // ---------- PngReader ----------

        // The IDAT data re-split into chunks of the given size (and led by empty ones), each with its CRC.
        private static byte[] Resplit(byte[] png, int chunk, int empties)
        {
            var idat = new MemoryStream();
            var head = new MemoryStream();
            var tail = new MemoryStream();
            head.Write(png, 0, 8);
            for (int pos = 8; pos < png.Length;)
            {
                int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                if (type == "IDAT") idat.Write(png, pos + 8, len);
                else (type == "IHDR" ? head : tail).Write(png, pos, len + 12);
                pos += len + 12;
            }
            var output = new MemoryStream();
            head.WriteTo(output);
            byte[] d = idat.ToArray();
            void Chunk(int off, int n)
            {
                var body = new byte[n + 4];
                body[0] = (byte)'I'; body[1] = (byte)'D'; body[2] = (byte)'A'; body[3] = (byte)'T';
                Array.Copy(d, off, body, 4, n);
                output.Write(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n }, 0, 4);
                output.Write(body, 0, body.Length);
                uint c = Crc32.Compute(body);
                output.Write(new[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c }, 0, 4);
            }
            for (int i = 0; i < empties; i++) Chunk(0, 0);
            for (int off = 0; off < d.Length; off += chunk) Chunk(off, Math.Min(chunk, d.Length - off));
            tail.WriteTo(output);
            return output.ToArray();
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // The stream overload's cap counted each IDAT's 12 bytes of framing as image data, so a legal file in small IDATs
        // (which Decode(byte[]) read) was refused as "far larger than the image".
        public void PngReader_TheStreamReadsSmallIdatChunksLikeTheBytes()
        {
            var rng = new Random(3159);
            var image = new ImageBuffer(300, 300);
            rng.NextBytes(image.Pixels);
            foreach (var (chunk, empties) in new[] { (1, 0), (4, 0), (1 << 16, 100000) })
            {
                byte[] png = Resplit(PngWriter.Encode(image), chunk, empties);
                Check.True(Same(PngReader.Decode(png).Pixels, image.Pixels), "bytes, chunk " + chunk);
                Check.True(Same(PngReader.Decode(new MemoryStream(png)).Pixels, image.Pixels), "stream, chunk " + chunk);
            }
            // Guard: a flood of empty chunks for a tiny image is still refused.
            var tiny = new ImageBuffer(1, 1);
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(Resplit(PngWriter.Encode(tiny), 1 << 16, 3_000_000))));
        }

        // ---------- SvgUtils and SvgDocument ----------

        // A control character or unpaired surrogate is written as U+FFFD but was measured as 0 (a lone high surrogate as an
        // em): the estimate must measure what EscapeText writes.
        public void Svg_EstimateMeasuresWhatEscapeTextWrites()
        {
            foreach (string s in new[] { "\u0001", "a\u001Fb", "\uDC00", "x\uD800", "\uD800\uD800\uDC00", "\uFFFE", "\u200D\u0001", "ok" })
                Check.Close(SvgUtils.EstimateTextWidth(SvgUtils.EscapeText(s), 1000), SvgUtils.EstimateTextWidth(s, 1000), 1e-9, s);
            var rng = new Random(4159);
            for (int trial = 0; trial < 5000; trial++)
            {
                var chars = new char[rng.Next(0, 8)];
                for (int i = 0; i < chars.Length; i++)
                    chars[i] = rng.Next(3) switch { 0 => (char)rng.Next(0, 0x20), 1 => (char)rng.Next(0xD800, 0xE000), _ => (char)rng.Next(0x20, 0x3000) };
                string s = new string(chars);
                string e = SvgUtils.EscapeText(s).Replace("&lt;", "<").Replace("&gt;", ">").Replace("&#13;", "\r").Replace("&amp;", "&");
                Check.Close(SvgUtils.EstimateTextWidth(e, 1000), SvgUtils.EstimateTextWidth(s, 1000), 1e-9, BitConverter.ToString(System.Text.Encoding.Unicode.GetBytes(s)));
            }
        }

        // A positive maxWidth written as "0" gave textLength="0", squeezing the text to nothing.
        public void Svg_FitWidthOfATinyWidthSetsNoLimit()
        {
            Check.Equal("", SvgUtils.FitWidth("abc", 10, 1e-7));
            Check.Equal("", SvgUtils.FitWidth("abc", 10, 4e-7));
            Check.Equal(" textLength=\"0.000001\" lengthAdjust=\"spacingAndGlyphs\"", SvgUtils.FitWidth("abc", 10, 6e-7));
        }

        // A positive font size, radius or stroke width written as "0" drew nothing; the constructor refuses the same for
        // the canvas.
        public void SvgDocument_ATinyPositiveSizeIsRefusedNotWrittenAsZero()
        {
            var doc = new SvgDocument(10, 10);
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => doc.Text(0, 0, "x", 1e-7)).ParamName);
            Check.Equal("radius", Check.Throws<ArgumentOutOfRangeException>(() => doc.Circle(0, 0, 1e-7)).ParamName);
            Check.Equal("strokeWidth", Check.Throws<ArgumentOutOfRangeException>(() => doc.Circle(0, 0, 1, "red", "blue", 1e-7)).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => doc.Rectangle(0, 0, 1e-7, 1)).ParamName);
            // Zero itself and the smallest size that writes are still drawn.
            doc.Text(0, 0, "x", 0).Circle(0, 0, 0).Circle(0, 0, 6e-7, "red", "blue", 6e-7).Rectangle(0, 0, 0, 0);
            Check.True(doc.ToString().Contains("r=\"0.000001\""), doc.ToString());
        }
    }
}
