using System;
using System.IO;
using System.Linq;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>Round 84 review regressions (binary formats and images, fifth pass).</summary>
    public sealed class BinaryImagesReview84Tests
    {
        private static int CountRed(ImageBuffer image)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).Equals(Rgba.Red)) n++;
            return n;
        }

        // ---------- ImageBuffer.DrawLineClipped ----------

        // The edge crossings (inexact below 2^40) were compared with the exact ends: a segment reaching the rectangle only
        // at an endpoint on an edge drew nothing.
        public void ClippedSegmentTouchingAnEdgeAtItsEndpointDrawsIt()
        {
            var image = new ImageBuffer(1, 14);
            image.DrawLineClipped(0.512680403079223, 2.2230085702487306, 0, -0.24299620347702697, Rgba.Red, -2, -2, 0, 8);
            Check.Equal(Rgba.Red, image.GetPixel(0, 0));

            image = new ImageBuffer(8, 10);
            image.DrawLineClipped(52.09870829810328, -9, 1.4043588663471684, 6, Rgba.Red, -1, 6, 5, 13);
            Check.Equal(Rgba.Red, image.GetPixel(1, 6));

            image = new ImageBuffer(10, 9);
            image.DrawLineClipped(58.73390406311206, -10.245150397645846, 4.334349457330231, 3, Rgba.Red, 0, 3, 9, 3);
            Check.Equal(Rgba.Red, image.GetPixel(4, 3));
        }

        // Randomized: a segment from outside to a point on an edge draws exactly that point's pixel; the same segment
        // stopped an ulp short of the edge draws nothing.
        public void ClippedSegmentsDecideTouchAndMissExactly()
        {
            var rng = new Random(8401);
            int touchFailures = 0, missFailures = 0;
            for (int trial = 0; trial < 4000; trial++)
            {
                int left = rng.Next(0, 10), top = rng.Next(0, 10), right = left + rng.Next(0, 10), bottom = top + rng.Next(0, 10);
                var image = new ImageBuffer(24, 24);
                int edge = rng.Next(4);
                double along = (edge < 2 ? left + (right - left) * rng.NextDouble() : top + (bottom - top) * rng.NextDouble());
                double far = 0.25 + 60 * rng.NextDouble(), drift = (rng.NextDouble() - 0.5) * 120;
                // (px, py) on the edge; (qx, qy) outside, beyond that edge.
                double px, py, qx, qy;
                switch (edge)
                {
                    case 0: px = along; py = top; qx = px + drift; qy = top - far; break;
                    case 1: px = along; py = bottom; qx = px + drift; qy = bottom + far; break;
                    case 2: px = left; py = along; qx = left - far; qy = py + drift; break;
                    default: px = right; py = along; qx = right + far; qy = py + drift; break;
                }
                image.DrawLineClipped(qx, qy, px, py, Rgba.Red, left, top, right, bottom);
                int ex = (int)Math.Max(left, Math.Min(right, Math.Round(px))), ey = (int)Math.Max(top, Math.Min(bottom, Math.Round(py)));
                if (!(CountRed(image) == 1 && image.GetPixel(ex, ey).Equals(Rgba.Red))) touchFailures++;

                // An ulp outside the edge.
                double sx = px, sy = py;
                switch (edge)
                {
                    case 0: sy = BitDecrement(py); break;
                    case 1: sy = BitIncrement(py); break;
                    case 2: sx = BitDecrement(px); break;
                    default: sx = BitIncrement(px); break;
                }
                var missed = new ImageBuffer(24, 24);
                missed.DrawLineClipped(qx, qy, sx, sy, Rgba.Red, left, top, right, bottom);
                if (CountRed(missed) != 0) missFailures++;
            }
            Check.Equal(0, touchFailures, "touching segments not drawn as their point");
            Check.Equal(0, missFailures, "missing segments drawn");
        }

        // A segment through the rectangle is still drawn end to end, and one parallel to an edge outside is not.
        public void ClippedSegmentsThroughAndBesideTheRectangle()
        {
            var image = new ImageBuffer(10, 10);
            image.DrawLineClipped(-100, 5, 100, 5, Rgba.Red, 2, 2, 7, 7);
            Check.Equal(6, CountRed(image));
            image = new ImageBuffer(10, 10);
            image.DrawLineClipped(-100, 1.5, 100, 1.5, Rgba.Red, 2, 2, 7, 7);
            Check.Equal(0, CountRed(image));
            image = new ImageBuffer(10, 10);
            image.DrawLineClipped(-100, -100, 100, 100, Rgba.Red, 2, 2, 7, 7);
            Check.Equal(6, CountRed(image));
            image = new ImageBuffer(10, 10);
            image.DrawLineClipped(0, 9, 9, 18, Rgba.Red, 2, 2, 7, 7);    // y = x + 9 runs below the rectangle: a miss
            Check.Equal(0, CountRed(image));
        }

        private static double BitIncrement(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            if (x == 0) return double.Epsilon;
            return BitConverter.Int64BitsToDouble(x > 0 ? bits + 1 : bits - 1);
        }

        private static double BitDecrement(double x) => -BitIncrement(-x);

        // ---------- Encoders past the longest string ----------

        // Each ran out of memory (after up to 80 s, some pre-allocating 2 GB) instead of refusing the input as Hex does.
        public void EncodersRefuseOutputPastTheLongestString()
        {
            var data = new byte[858993436];                     // 5 Base85 chars per 4 bytes: 1,073,741,795, just past the limit
            foreach (var (name, encode) in new (string, Func<byte[], string>)[]
            {
                ("Base32", d => Base32.Encode(d)), ("Base32 unpadded", d => Base32.Encode(d, padding: false)),
                ("Crockford", CrockfordBase32.Encode), ("Base85", Base85.Encode), ("Base64Url", Base64Url.Encode),
                ("QuotedPrintable", QuotedPrintable.Encode),
            })
            {
                var ex = Check.Throws<ArgumentException>(() => encode(data), name);
                Check.Equal("data", ex.ParamName, name);
            }
        }

        // Base64Url now encodes a slice at a time; QuotedPrintable's length check follows its line breaking exactly.
        public void SlicedBase64UrlAndQuotedPrintableLengthAgreeWithReferences()
        {
            var rng = new Random(8402);
            foreach (int n in new[] { 0, 1, 2, 3, (3 << 20) - 1, 3 << 20, (3 << 20) + 1, (3 << 21) + 2 })
            {
                var data = new byte[n];
                rng.NextBytes(data);
                string expected = Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                Check.Equal(expected, Base64Url.Encode(data), "n = " + n);
            }
            var length = typeof(QuotedPrintable).GetMethod("EncodedLength", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            byte[] alphabet = { (byte)'a', (byte)' ', (byte)'\t', (byte)'=', 0, 200, (byte)'\r', (byte)'\n' };
            for (int trial = 0; trial < 2000; trial++)
            {
                var data = new byte[rng.Next(0, 400)];
                for (int i = 0; i < data.Length; i++) data[i] = alphabet[rng.Next(alphabet.Length)];
                Check.Equal((long)QuotedPrintable.Encode(data).Length, (long)length.Invoke(null, new object[] { data }), "trial " + trial);
            }
        }

        // ---------- Base58 ----------

        private const string Alphabet58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        // The textbook conversion, one BigInteger digit at a time.
        private static string NaiveBase58(byte[] data)
        {
            var number = new System.Numerics.BigInteger(Enumerable.Reverse(data).Concat(new byte[] { 0 }).ToArray());
            var sb = new System.Text.StringBuilder();
            while (number > 0) { sb.Insert(0, Alphabet58[(int)(number % 58)]); number /= 58; }
            return new string('1', data.TakeWhile(b => b == 0).Count()) + sb;
        }

        // Rewritten over 32-bit limbs; graded against the textbook version (this one passes on the old code too).
        public void Base58AgreesWithTheTextbookConversion()
        {
            var rng = new Random(8405);
            for (int trial = 0; trial < 3000; trial++)
            {
                var data = new byte[rng.Next(0, trial < 2000 ? 40 : 300)];
                rng.NextBytes(data);
                int zeros = rng.Next(4) == 0 ? rng.Next(0, Math.Min(5, data.Length + 1)) : 0;
                for (int i = 0; i < zeros; i++) data[i] = 0;
                if (rng.Next(10) == 0) for (int i = 0; i < data.Length; i++) data[i] = rng.Next(2) == 0 ? (byte)0 : (byte)255;
                string expected = NaiveBase58(data);
                Check.Equal(expected, Base58.Encode(data), "trial " + trial);
                Check.True(data.SequenceEqual(Base58.Decode(expected)), "decode, trial " + trial);
            }
            Check.Equal("1112", Base58.Encode(new byte[] { 0, 0, 0, 1 }));
            Check.Equal(new byte[] { 0, 0 }.Length, Base58.Decode("11").Length);
        }

        public void Base58IsFastEnoughAt100KilobytesAndDecodeCanBeCapped()
        {
            var data = new byte[100_000];
            new Random(8406).NextBytes(data);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string text = Base58.Encode(data);
            Check.True(data.SequenceEqual(Base58.Decode(text)));
            Check.True(watch.Elapsed.TotalSeconds < 4, $"took {watch.Elapsed.TotalSeconds:F1} s");    // was about 7 s, now 1.5
            Check.Throws<FormatException>(() => Base58.Decode(text, 1000));
            Check.True(new byte[] { 1, 2, 3 }.SequenceEqual(Base58.Decode(Base58.Encode(new byte[] { 1, 2, 3 }), 5)));
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => Base58.Decode("2", -1));
            Check.Equal("maxLength", ex.ParamName);
        }

        // ---------- PngReader.Decode(Stream) ----------

        // Endless bytes after a given prefix; counts what was read.
        private sealed class EndlessStream : Stream
        {
            private readonly byte[] _prefix;
            public long Read_;
            public EndlessStream(byte[] prefix) { _prefix = prefix; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                for (int i = 0; i < count; i++) buffer[offset + i] = Read_ + i < _prefix.Length ? _prefix[Read_ + i] : (byte)0;
                Read_ += count;
                return count;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Read_; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // Nothing was checked until IEND or 2 GB: a GIF followed by endless zeros was read for 2 GB before being rejected.
        public void PngStreamRejectsNonPngInputAtOnce()
        {
            var png = PngWriter.Encode(new ImageBuffer(3, 2, Rgba.Red));
            var cases = new[]
            {
                System.Text.Encoding.ASCII.GetBytes("GIF89a"),
                png.Take(8).ToArray(),                                                 // signature, then zero chunks
                png.Take(33).ToArray(),                                                // IHDR, then zero chunks
                png.Take(33).Concat(new byte[] { 0x7F, 0xFF, 0xFF, 0x00, (byte)'t', (byte)'E', (byte)'X', (byte)'t' }).ToArray(),
                png.Take(33).Concat(new byte[] { 0x7F, 0xFF, 0xFF, 0x00, (byte)'I', (byte)'D', (byte)'A', (byte)'T' }).ToArray(),
            };
            var withBadCrc = png.Take(33).ToArray();
            withBadCrc[30] ^= 1;
            cases = cases.Concat(new[] { withBadCrc }).ToArray();
            foreach (var prefix in cases)
            {
                var stream = new EndlessStream(prefix);
                Check.Throws<InvalidDataException>(() => PngReader.Decode(stream));
                Check.True(stream.Read_ < 1 << 20, "read " + stream.Read_ + " bytes");
            }
            // A valid stream still decodes.
            Check.Equal(Rgba.Red, PngReader.Decode(new MemoryStream(png)).GetPixel(2, 1));
        }

        // Ancillary chunks are now checked and skipped as they stream past; a bad CRC in one is still caught.
        // A guard (passes on the old code): skipping ancillary chunks while streaming still checks their CRCs.
        public void PngStreamSkipsAncillaryChunksAndChecksThem()
        {
            var rng = new Random(8403);
            var image = new ImageBuffer(7, 5);
            for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++) image.SetPixel(x, y, new Rgba((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)));
            var raw = new System.Collections.Generic.List<byte>();
            for (int y = 0; y < 5; y++)
            {
                raw.Add(0);
                for (int x = 0; x < 7; x++) { var p = image.GetPixel(x, y); raw.AddRange(new[] { p.R, p.G, p.B, p.A }); }
            }
            var text = new byte[100_000];
            rng.NextBytes(text);
            byte[] png = Png(("IHDR", Ihdr(7, 5, 8, 6)), ("zTXt", text), ("IDAT", Zlib(raw.ToArray())), ("tIME", new byte[7]), ("IEND", Array.Empty<byte>()));
            foreach (var decoded in new[] { PngReader.Decode(png), PngReader.Decode(new MemoryStream(png)) })
                for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++) Check.Equal(image.GetPixel(x, y), decoded.GetPixel(x, y));
            var bad = (byte[])png.Clone();
            bad[33 + 8 + 5000] ^= 1;                                                   // inside zTXt
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(bad)));
            Check.Throws<InvalidDataException>(() => PngReader.Decode(bad));
        }

        // Memory was the image plus 4.7 times the file (byte[]) or 9.5 times (Stream), against a doc saying the image
        // plus the compressed data.
        public void PngDecodeMemoryIsTheImagePlusAFewCopiesOfTheFile()
        {
            const int w = 1000, h = 1000;
            var rng = new Random(8404);
            var image = new ImageBuffer(w, h);
            rng.NextBytes(image.Pixels);                                               // incompressible: the file is about the image
            byte[] png = PngWriter.Encode(image);
            long imageBytes = 4L * w * h;
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            PngReader.Decode(png);
            long fromArray = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.Collect();
            before = GC.GetAllocatedBytesForCurrentThread();
            PngReader.Decode(new MemoryStream(png));
            long fromStream = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(fromArray < imageBytes + 1.5 * png.Length + 1_000_000, $"byte[]: {fromArray:N0} for a {png.Length:N0}-byte file");
            Check.True(fromStream < imageBytes + 3.5 * png.Length + 1_000_000, $"Stream: {fromStream:N0} for a {png.Length:N0}-byte file");
        }

        // ---------- PngReader chunk rules ----------

        private static byte[] Zlib(byte[] raw)
        {
            var ms = new MemoryStream();
            using (var d = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) d.Write(raw, 0, raw.Length);
            var adler = new byte[4];
            EndianConverter.WriteUInt32BigEndian(adler, 0, Adler32.Compute(raw));
            return new byte[] { 0x78, 0xDA }.Concat(ms.ToArray()).Concat(adler).ToArray();
        }

        private static byte[] Ihdr(int w, int h, byte depth, byte colorType)
        {
            var ihdr = new byte[13];
            EndianConverter.WriteUInt32BigEndian(ihdr, 0, (uint)w);
            EndianConverter.WriteUInt32BigEndian(ihdr, 4, (uint)h);
            ihdr[8] = depth; ihdr[9] = colorType;
            return ihdr;
        }

        private static byte[] Png(params (string Type, byte[] Body)[] chunks)
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            foreach (var (type, body) in chunks)
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
            return ms.ToArray();
        }

        // All of these decoded although the spec forbids them.
        public void PngForbiddenPaletteAndTransparencyChunksAreRejected()
        {
            var iend = ("IEND", Array.Empty<byte>());
            void Bad(params (string, byte[])[] chunks) => Check.Throws<InvalidDataException>(() => PngReader.Decode(Png(chunks)));
            void Good(params (string, byte[])[] chunks) => PngReader.Decode(Png(chunks));
            var rgba = ("IDAT", Zlib(new byte[] { 0, 1, 2, 3, 4 }));
            var greyAlpha = ("IDAT", Zlib(new byte[] { 0, 1, 2 }));
            var rgb = ("IDAT", Zlib(new byte[] { 0, 1, 2, 3 }));
            var grey = ("IDAT", Zlib(new byte[] { 0, 1 }));
            var index = ("IDAT", Zlib(new byte[] { 0, 0 }));
            Bad(("IHDR", Ihdr(1, 1, 8, 6)), ("tRNS", new byte[6]), rgba, iend);
            Bad(("IHDR", Ihdr(1, 1, 8, 4)), ("tRNS", new byte[2]), greyAlpha, iend);
            Bad(("IHDR", Ihdr(1, 1, 8, 2)), ("tRNS", new byte[7]), rgb, iend);
            Good(("IHDR", Ihdr(1, 1, 8, 2)), ("tRNS", new byte[6]), rgb, iend);
            Bad(("IHDR", Ihdr(1, 1, 8, 0)), ("tRNS", new byte[1]), grey, iend);
            Good(("IHDR", Ihdr(1, 1, 8, 0)), ("tRNS", new byte[2]), grey, iend);
            Bad(("IHDR", Ihdr(1, 1, 8, 3)), ("PLTE", new byte[3]), ("tRNS", new byte[3]), index, iend);
            Good(("IHDR", Ihdr(1, 1, 8, 3)), ("PLTE", new byte[3]), ("tRNS", new byte[1]), index, iend);
            Bad(("IHDR", Ihdr(1, 1, 1, 3)), ("PLTE", new byte[768]), index, iend);
            Good(("IHDR", Ihdr(1, 1, 1, 3)), ("PLTE", new byte[6]), index, iend);
            Good(("IHDR", Ihdr(1, 1, 8, 3)), ("PLTE", new byte[768]), index, iend);
        }

        // ---------- Bmp.Decode ----------

        private static byte[] Bmp8(byte[] palette, int colors, int offset, params byte[] pixels)
        {
            int stride = (pixels.Length + 3) & ~3;
            var data = new byte[Math.Max(offset, 54 + palette.Length) + stride];
            data[0] = (byte)'B'; data[1] = (byte)'M';
            BitConverter.GetBytes(data.Length).CopyTo(data, 2);
            BitConverter.GetBytes(offset).CopyTo(data, 10);
            BitConverter.GetBytes(40).CopyTo(data, 14);
            BitConverter.GetBytes(pixels.Length).CopyTo(data, 18);
            BitConverter.GetBytes(1).CopyTo(data, 22);
            BitConverter.GetBytes((short)1).CopyTo(data, 26);
            BitConverter.GetBytes((short)8).CopyTo(data, 28);
            BitConverter.GetBytes(colors).CopyTo(data, 46);
            palette.CopyTo(data, 54);
            pixels.CopyTo(data, offset);
            return data;
        }

        // An index past the palette read as black, and a pixel offset inside the palette was accepted.
        public void BmpPaletteIndexAndOffsetAreChecked()
        {
            byte[] palette = { 10, 20, 30, 0, 40, 50, 60, 0 };                     // BGRx
            var image = Bmp.Decode(Bmp8(palette, 2, 62, 0, 1));
            Check.Equal(new Rgba(30, 20, 10), image.GetPixel(0, 0));
            Check.Equal(new Rgba(60, 50, 40), image.GetPixel(1, 0));
            Check.Throws<InvalidDataException>(() => Bmp.Decode(Bmp8(palette, 2, 62, 0, 200)));
            Check.Throws<InvalidDataException>(() => Bmp.Decode(Bmp8(palette, 2, 62, 2)));
            Check.Throws<InvalidDataException>(() => Bmp.Decode(Bmp8(palette, 2, 58, 0, 1)));
        }

        // ---------- SvgUtils.EstimateTextWidth ----------

        // Zero width came from the framework's Unicode tables, which differ by version: 440 code points measured
        // differently on .NET Framework 4.8 and .NET 8. Now fixed at Unicode 15.0 everywhere.
        public void TextWidthClassesAreTheSameOnEveryFramework()
        {
            Check.Equal(0.0, SvgUtils.EstimateTextWidth(char.ConvertFromUtf32(0x10D24), 1000), "U+10D24 (Hanifi Rohingya mark)");
            foreach (int cp in new[] { 0x07FD, 0x0890, 0x0891, 0x0898, 0x089F, 0x08CA, 0x08D2, 0x0300, 0x200D, 0xFE0F })
                Check.Equal(0.0, SvgUtils.EstimateTextWidth(((char)cp).ToString(), 1000), $"U+{cp:X4}");
            Check.Equal(556.0, SvgUtils.EstimateTextWidth("\u1734", 1000), "U+1734 is a spacing mark since Unicode 14");
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth("\U0001F600", 1000));
            // A symbol after a joiner is part of the emoji (So), a letter is not.
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth("\U0001F468\u200D\u2764", 1000));
            Check.Equal(1556.0, SvgUtils.EstimateTextWidth("\U0001F468\u200D\u0416", 1000));
        }

        // A flag (two regional indicators) renders as one glyph but counted two em.
        public void FlagEmojiCountsOneEm()
        {
            const string us = "\uD83C\uDDFA\uD83C\uDDF8", fr = "\uD83C\uDDEB\uD83C\uDDF7";
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth(us, 1000));
            Check.Equal(2000.0, SvgUtils.EstimateTextWidth(us + fr, 1000));
            Check.Equal(2000.0, SvgUtils.EstimateTextWidth(us + "\uD83C\uDDFA", 1000), "a lone third indicator is its own em");
            double a = SvgUtils.EstimateTextWidth("a", 1000);
            Check.Equal(2000.0 + a, SvgUtils.EstimateTextWidth("\uD83C\uDDFA" + "a" + us, 1000), "a letter breaks the pairing");
        }
    }
}
