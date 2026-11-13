using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>Round 68 review regressions (binary formats and images, fourth pass).</summary>
    public sealed class BinaryImagesReview4Tests
    {
        // ---------- SVG numbers ----------

        // An independent reference: floor(|v| · 10⁷) from the exact binary value, then the seventh decimal decides
        // (≥ 5 rounds the magnitude up, which is half away from zero).
        private static string Reference(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int e = (int)((bits >> 52) & 0x7FF);
            BigInteger m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m += BigInteger.One << 52;
            e -= 1075;
            BigInteger seven = e >= 0 ? (m << e) * 10000000 : (m * 10000000) >> -e;   // floor
            return Format(seven / 10 + (seven % 10 >= 5 ? 1 : 0), v < 0);
        }

        private static string Format(BigInteger six, bool negative)
        {
            if (six.IsZero) return "0";
            string digits = six.ToString(CultureInfo.InvariantCulture).PadLeft(7, '0');
            string whole = digits.Substring(0, digits.Length - 6), frac = digits.Substring(digits.Length - 6).TrimEnd('0');
            return (negative ? "-" : "") + whole + (frac.Length > 0 ? "." + frac : "");
        }

        private static double RandomDouble(Random rng)
        {
            switch (rng.Next(4))
            {
                case 0: return (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-8, 19));
                case 1: return Math.Round(rng.NextDouble() * 1e6) / 1e6 * Math.Pow(10, rng.Next(0, 10)) + rng.Next(-3, 4) * 5e-7;
                case 2: return rng.Next(-100000, 100000) / Math.Pow(2, rng.Next(0, 30));      // binary fractions, many ties
                default:
                    var b = new byte[8]; rng.NextBytes(b);
                    double d = BitConverter.ToDouble(b, 0);
                    return double.IsNaN(d) || double.IsInfinity(d) ? 0.5 : d;
            }
        }

        public void SvgNumber_RoundsTheExactValue_AgainstAReference()
        {
            // "0.######" rounded to 15 significant digits first and differed between .NET Framework and .NET.
            var rng = new Random(6801);
            for (int t = 0; t < 20000; t++)
            {
                double v = RandomDouble(rng);
                if (Math.Abs(v) > 1e40) v = Math.Sign(v) * Math.Abs(v) % 1e40;
                string expected = Reference(v);
                Check.Equal(expected, SvgUtils.Number(v));
                if (v >= 1e-6 && v < 1e30)                                              // the size path in SvgDocument
                    Check.True(new SvgDocument(v, 1).ToString().Contains("width=\"" + expected + "\""), $"{v:R}");
#if !NETFRAMEWORK
                // .NET Core 3.0+ formats exact digits: a second, independent reference.
                string f = Math.Abs(v).ToString("F40", CultureInfo.InvariantCulture);
                int dot = f.IndexOf('.');
                var six = BigInteger.Parse(f.Substring(0, dot) + f.Substring(dot + 1, 6), CultureInfo.InvariantCulture);
                if (f[dot + 7] >= '5') six += 1;
                Check.Equal(Format(six, v < 0), expected, $"{v:R}");
#endif
            }
        }

        public void SvgNumber_KnownCases()
        {
            Check.Equal("0.755138", SvgUtils.Number(0.7551384999999999));               // was 0.755139
            Check.Equal("4503599627370497", SvgUtils.Number(4503599627370497.0));      // was 4503599627370500
            Check.Equal("0.007813", SvgUtils.Number(1.0 / 128));                        // an exact tie: away from zero
            Check.Equal("-0.007813", SvgUtils.Number(-1.0 / 128));
            Check.Equal("0", SvgUtils.Number(-1e-9));
            Check.Equal("0", SvgUtils.Number(-0.0));
            Check.Equal("123456789012.345673", SvgUtils.Number(123456789012.345673));
            Check.Equal("1000000000000000000000", SvgUtils.Number(1e21));
            Check.Equal("0.1", SvgUtils.Number(0.1));
            Check.True(new SvgDocument(4503599627370497.0, 1).ToString().Contains("width=\"4503599627370497\""));
        }

        // ---------- SVG text width ----------

        public void EstimateTextWidth_ZeroWidthCharacters_AddNothing()
        {
            double W(string s) => SvgUtils.EstimateTextWidth(s, 10);
            double e = W("e"), ab = W("ab");
            Check.Equal(e, W("e॑"));                    // Devanagari stress sign (a mark outside U+0300–036F)
            Check.Equal(e, W("e⃝"));                    // enclosing circle
            Check.Equal(e, W("e᪰"));                    // combining diacritics extended
            Check.Equal(ab, W("a​b"));                  // zero-width space
            Check.Equal(ab, W("a‌b"));                  // ZWNJ
            Check.Equal(ab, W("a﻿b"));                  // BOM
            Check.Equal(ab, W("a️b"));                  // variation selector
            Check.Equal(e, W("e\U0001D167"));                // an astral combining mark
            Check.Equal(e, W("e\U000E0061"));                // a tag character
            Check.Equal(W(" "), W(" "));                // no-break space is a space, not an average letter
        }

        public void EstimateTextWidth_EmojiSequences_CountOnce()
        {
            double W(string s) => SvgUtils.EstimateTextWidth(s, 10);
            double one = W("\U0001F600");
            Check.Equal(10.0, one);
            Check.Equal(one, W("\U0001F468‍\U0001F469‍\U0001F467"));           // family
            Check.Equal(one, W("\U0001F44D\U0001F3FD"));                                 // skin tone
            Check.Equal(one, W("\U0001F468‍❤️‍\U0001F468"));         // couple with heart
            Check.Equal(one + W("a"), W("\U0001F600a"));
        }

        public void EstimateTextWidth_AnUnrepresentableWidth_IsRefused()
        {
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.EstimateTextWidth("@", double.MaxValue, bold: true)).ParamName);
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.EstimateTextWidth("WWWW", double.MaxValue)).ParamName);
            Check.Equal(0.0, SvgUtils.EstimateTextWidth("", double.MaxValue));
        }

        // ---------- canonical text encodings ----------

        public void Crockford_NonZeroUnusedBits_AreRejected()
        {
            Check.Throws<FormatException>(() => CrockfordBase32.Decode("01"));       // decoded to 0x00, like "00"
            Check.True(CrockfordBase32.Decode("00").SequenceEqual(new byte[] { 0 }));
            var rng = new Random(6802);
            const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            for (int t = 0; t < 5000; t++)
            {
                var data = new byte[rng.Next(0, 12)]; rng.NextBytes(data);
                Check.True(CrockfordBase32.Decode(CrockfordBase32.Encode(data)).SequenceEqual(data));
                var sb = new StringBuilder();
                for (int k = rng.Next(1, 14); k > 0; k--) sb.Append(alphabet[rng.Next(32)]);
                string s = sb.ToString();
                byte[]? decoded = null;
                try { decoded = CrockfordBase32.Decode(s); } catch (FormatException) { }
                if (decoded != null) Check.Equal(s, CrockfordBase32.Encode(decoded));   // one spelling per value
            }
        }

        public void Base85_NonCanonicalFinalGroup_IsRejected()
        {
            Check.Throws<FormatException>(() => Base85.Decode("!#"));                // decoded to 0x00, like "!!"
            Check.True(Base85.Decode("!!").SequenceEqual(new byte[] { 0 }));
            var rng = new Random(6803);
            for (int t = 0; t < 5000; t++)
            {
                var data = new byte[rng.Next(0, 12)]; rng.NextBytes(data);
                Check.True(Base85.Decode(Base85.Encode(data)).SequenceEqual(data));
                var sb = new StringBuilder();
                for (int k = rng.Next(2, 13); k > 0; k--) sb.Append((char)('!' + rng.Next(85)));
                string s = sb.ToString();
                byte[]? decoded = null;
                try { decoded = Base85.Decode(s); } catch (FormatException) { }
                if (decoded != null) Check.Equal(s, Base85.Encode(decoded));
            }
        }

        // ---------- Base58 ----------

        private static string NaiveBase58(byte[] data)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            var n = new BigInteger(Enumerable.Reverse(data).Concat(new byte[] { 0 }).ToArray());
            var sb = new StringBuilder();
            while (n > 0) { sb.Insert(0, alphabet[(int)(n % 58)]); n /= 58; }
            for (int i = 0; i < data.Length && data[i] == 0; i++) sb.Insert(0, '1');
            return sb.ToString();
        }

        public void Base58_Encode_MatchesANaiveReference()
        {
            var rng = new Random(6804);
            for (int t = 0; t < 3000; t++)
            {
                var data = new byte[rng.Next(0, 40)]; rng.NextBytes(data);
                if (data.Length > 0 && rng.Next(4) == 0) data[0] = 0;
                if (data.Length > 1 && rng.Next(8) == 0) data[1] = 0;
                if (rng.Next(10) == 0) for (int i = data.Length / 2; i < data.Length; i++) data[i] = 0;
                Check.Equal(NaiveBase58(data), Base58.Encode(data));
            }
        }

        public void Base58_Encode_IsNotSlowerThanDecode()
        {
            // One big division per digit made encoding about seven times slower than decoding.
            var data = new byte[24000]; new Random(6805).NextBytes(data);
            string text = Base58.Encode(data);
            Check.True(Base58.Decode(text).SequenceEqual(data));
            double best(Action a) { double b = double.MaxValue; for (int i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); a(); b = Math.Min(b, sw.Elapsed.TotalSeconds); } return b; }
            double enc = best(() => Base58.Encode(data)), dec = best(() => Base58.Decode(text));
            Check.True(enc < 3 * dec, $"encode {enc:F3} s, decode {dec:F3} s");
        }

        // ---------- PngReader ----------

        private static void Chunk(Stream ms, string type, byte[] body)
        {
            var len = new byte[4];
            EndianConverter.WriteUInt32BigEndian(len, 0, (uint)body.Length);
            ms.Write(len, 0, 4);
            var td = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            ms.Write(td, 0, td.Length);
            var crc = new byte[4];
            EndianConverter.WriteUInt32BigEndian(crc, 0, Crc32.Compute(td));
            ms.Write(crc, 0, 4);
        }

        private static byte[] Ihdr(int w, int h, byte depth, byte colorType)
        {
            var ihdr = new byte[13];
            EndianConverter.WriteUInt32BigEndian(ihdr, 0, (uint)w);
            EndianConverter.WriteUInt32BigEndian(ihdr, 4, (uint)h);
            ihdr[8] = depth; ihdr[9] = colorType;
            return ihdr;
        }

        // A zlib stream of `head` followed by `zeros` zero bytes, written in pieces; the Adler-32 is computed unless
        // `adler` is given.
        private static byte[] Zlib(byte[] head, long zeros, uint? adler = null)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78); ms.WriteByte(0x01);
            var hasher = new Adler32Hasher();
            using (var d = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                d.Write(head, 0, head.Length);
                if (adler is null) hasher.Append(head, 0, head.Length);
                var block = new byte[1 << 20];
                for (long left = zeros; left > 0; left -= block.Length)
                {
                    int n = (int)Math.Min(block.Length, left);
                    d.Write(block, 0, n);
                    if (adler is null) hasher.Append(block, 0, n);
                }
            }
            var tail = new byte[4];
            EndianConverter.WriteUInt32BigEndian(tail, 0, adler ?? hasher.Value);
            ms.Write(tail, 0, 4);
            return ms.ToArray();
        }

        private static byte[] Png(byte[] ihdr, byte[] idat)
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            Chunk(ms, "IHDR", ihdr);
            Chunk(ms, "IDAT", idat);
            Chunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        public void PngReader_TallNarrowImage_DecodesQuickly()
        {
            // Two deflate reads per row (about 4 µs each): 1 × 4·10⁶ took about 17 s.
            const int h = 4_000_000;
            var raw = new byte[2 * h];
            for (int r = 0; r < h; r++) raw[2 * r + 1] = (byte)(r * 7);
            var ms = new MemoryStream();
            ms.WriteByte(0x78); ms.WriteByte(0x01);
            using (var d = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true)) d.Write(raw, 0, raw.Length);
            var adler = new byte[4];
            EndianConverter.WriteUInt32BigEndian(adler, 0, Adler32.Compute(raw));
            ms.Write(adler, 0, 4);
            byte[] png = Png(Ihdr(1, h, 8, 0), ms.ToArray());
            var sw = Stopwatch.StartNew();
            var image = PngReader.Decode(png);
            double s = sw.Elapsed.TotalSeconds;
            Check.Equal(h, image.Height);
            for (int r = 0; r < h; r += 99991) Check.Equal((byte)(r * 7), image.GetPixel(0, r).R);
            Check.True(s < 4, $"{s:F2} s");
        }

        public void PngReader_DataPastTheLastScanline_IsDrainedOnlySoFar()
        {
            // 1 × 1 RGB with 512 MB of zeros after the row (a 512 KB file): every byte was inflated and checksummed.
            // Past the drain allowance the rest is ignored unchecked, as libpng does, so a wrong Adler there is not seen.
            byte[] png = Png(Ihdr(1, 1, 8, 2), Zlib(new byte[] { 0, 10, 20, 30 }, 512L << 20, adler: 0x12345678));
            var sw = Stopwatch.StartNew();
            var image = PngReader.Decode(png);
            double s = sw.Elapsed.TotalSeconds;
            Check.Equal(new Rgba(10, 20, 30, 255), image.GetPixel(0, 0));
            Check.True(s < 1, $"{s:F2} s");
            // Within the allowance the checksum is still checked.
            byte[] near = Png(Ihdr(1, 1, 8, 2), Zlib(new byte[] { 0, 10, 20, 30 }, 1000, adler: 0x12345678));
            Check.Throws<InvalidDataException>(() => PngReader.Decode(near));
            Check.Equal(new Rgba(10, 20, 30, 255), PngReader.Decode(Png(Ihdr(1, 1, 8, 2), Zlib(new byte[] { 0, 10, 20, 30 }, 1000))).GetPixel(0, 0));
        }

        public void PngReader_ARowOverTwoGigabits_Decodes()
        {
            // 2^25 × 1 RGBA16 is a third of MaxPixels, but a row of 2^31 bits was refused as "too large".
            const int w = 1 << 25;
            byte[] png = Png(Ihdr(w, 1, 16, 6), Zlib(new byte[] { 0 }, (long)w * 8));
            var image = PngReader.Decode(png);
            Check.Equal(w, image.Width);
            Check.Equal(new Rgba(0, 0, 0, 0), image.GetPixel(w - 1, 0));
        }

        private sealed class ThenFails : Stream
        {
            private readonly byte[] _data;
            private int _pos;
            public ThenFails(byte[] data) { _data = data; }
            public int Position0 => _pos;
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= _data.Length) throw new InvalidOperationException("read past the PNG");
                int n = Math.Min(count, _data.Length - _pos);
                Array.Copy(_data, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        public void PngReader_Stream_StopsAtIend()
        {
            // CopyTo read the source to its end: an endless or huge source ran until a 2 GB IOException, and a PNG
            // followed by other data was consumed past its end.
            byte[] png = Png(Ihdr(1, 1, 8, 2), Zlib(new byte[] { 0, 10, 20, 30 }, 0));
            var withMore = png.Concat(new byte[] { 1, 2, 3 }).ToArray();
            var src = new ThenFails(withMore);
            Check.Equal(new Rgba(10, 20, 30, 255), PngReader.Decode(src).GetPixel(0, 0));
            Check.Equal(png.Length, src.Position0);
            Check.Equal(new Rgba(10, 20, 30, 255), PngReader.Decode(new ThenFails(png)).GetPixel(0, 0));
        }

        public void PngReader_Stream_AChunkPastTwoGigabytes_IsInvalidData()
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            ms.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xF0, (byte)'I', (byte)'D', (byte)'A', (byte)'T' }, 0, 8);
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new ThenFails(ms.ToArray())));
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new MemoryStream(new byte[] { 137, 80, 78 })));
        }
    }
}
