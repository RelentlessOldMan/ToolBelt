using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>Round 52 review regressions (binary formats and images, third pass).</summary>
    public sealed class BinaryImagesReview3Tests
    {
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        // ---------- SVG ----------

        private static void Unchanged(SvgDocument doc, string param, Action call)
        {
            string before = doc.ToString();
            var ex = Check.Throws<ArgumentOutOfRangeException>(call);
            Check.Equal(param, ex.ParamName);
            Check.Equal(before, doc.ToString());                                   // was "<line x1="0" y1="</svg>"
        }

        public void SvgDocument_ANonFiniteNumber_LeavesTheDocumentUnchangedAndNamesIt()
        {
            var doc = new SvgDocument(10, 10).Line(0, 0, 1, 1);
            Unchanged(doc, "y1", () => doc.Line(0, double.NaN, 1, 1));
            Unchanged(doc, "y", () => doc.Rectangle(1, double.PositiveInfinity, 2, 2));
            Unchanged(doc, "width", () => doc.Rectangle(1, 1, double.PositiveInfinity, 2));
            Unchanged(doc, "strokeWidth", () => doc.Rectangle(1, 1, 2, 2, stroke: "red", strokeWidth: double.PositiveInfinity));
            Unchanged(doc, "cx", () => doc.Circle(double.NaN, 0, 1));
            Unchanged(doc, "radius", () => doc.Circle(0, 0, double.PositiveInfinity));
            Unchanged(doc, "y", () => doc.Text(0, double.NegativeInfinity, "t"));
            Unchanged(doc, "fontSize", () => doc.Text(0, 0, "t", double.PositiveInfinity));
            Unchanged(doc, "points", () => doc.Polygon(new[] { (0.0, 0.0), (double.NaN, 1.0) }));
            Unchanged(doc, "strokeWidth", () => doc.Path("M0 0", stroke: "red", strokeWidth: double.PositiveInfinity));
            Check.True(System.Xml.Linq.XDocument.Parse(doc.ToString()) != null);
        }

        public void SvgDocument_ASizeThatRoundsToZero_IsRejected()
        {
            // 1e-7 was written as width="0" height="0" viewBox="0 0 0 0": an SVG no viewer renders.
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(1e-7, 1)).ParamName);
            Check.Equal("height", Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(1, 4e-7)).ParamName);
            // The double nearest 5e-7 is just below 0.0000005, so it rounds to 0 exactly (round 68); just above it is kept.
            Check.Equal("height", Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(1, 5e-7)).ParamName);
            Check.True(new SvgDocument(1e-6, 5.000001e-7).ToString().Contains("viewBox=\"0 0 0.000001 0.000001\""));
        }

        public void PathBuilder_ANonFiniteArgument_LeavesThePathUnchangedAndNamesIt()
        {
            var path = new PathBuilder();
            Check.Equal("y", Check.Throws<ArgumentOutOfRangeException>(() => path.MoveTo(1, double.NaN)).ParamName);
            Check.True(path.IsEmpty);                                              // was "M1 ", not empty
            path.MoveTo(0, 0);
            Check.Equal("x", Check.Throws<ArgumentOutOfRangeException>(() => path.ArcTo(1, 1, 0, false, false, double.NaN, 0)).ParamName);
            Check.Equal("x2", Check.Throws<ArgumentOutOfRangeException>(() => path.CubicTo(1, 1, double.PositiveInfinity, 2, 3, 3)).ParamName);
            Check.Equal("rotationDegrees", Check.Throws<ArgumentOutOfRangeException>(() => path.ArcTo(1, 1, double.NaN, false, false, 0, 0)).ParamName);
            Check.Equal("M0 0", path.ToString());                                  // was "M0 0 A1 1 0 0 0 C1 1 "
            path.ArcTo(1, 2, 30, true, false, 5, 6).HorizontalTo(-0.0000001).VerticalTo(2.5).QuadraticTo(1, 2, 3, 4).LineTo(7, 8).Close();
            Check.Equal("M0 0 A1 2 30 1 0 5 6 H0 V2.5 Q1 2 3 4 L7 8 Z", path.ToString());
        }

        public void StrokeAttributes_ANegativeWidth_IsRejected()
        {
            // stroke-width="-1" is an SVG error; SvgDocument already rejected the same value.
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.StrokeAttributes(Rgba.Black, -1)).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.StrokeAttributes(Rgba.Black, double.NaN)).ParamName);
            Check.Equal(string.Empty, SvgUtils.StrokeAttributes(null, -1));
            Check.Equal("stroke=\"#000000\" stroke-width=\"0\"", SvgUtils.StrokeAttributes(Rgba.Black, 0));
        }

        public void EstimateTextWidth_AnInfiniteFontSize_IsRejected()
        {
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.EstimateTextWidth("", double.PositiveInfinity)).ParamName);   // was NaN
            Check.Equal("fontSize", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.EstimateTextWidth("a", double.PositiveInfinity)).ParamName);  // was ∞
            Check.Equal(0.0, SvgUtils.EstimateTextWidth("", 12));
        }

        // ---------- DrawLineClipped ----------

        private static int Lit(ImageBuffer image, Rgba color)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).Equals(color)) n++;
            return n;
        }

        public void DrawLineClipped_EndpointsNearTheDoubleLimit_StillDraw()
        {
            var image = new ImageBuffer(10, 10);
            image.DrawLineClipped(-1e308, 5, 1e308, 5, Rgba.White, 0, 0, 9, 9);     // dx overflowed to ∞: nothing drawn
            for (int x = 0; x < 10; x++) Check.True(image.GetPixel(x, 5).Equals(Rgba.White), "x=" + x);
            Check.Equal(10, Lit(image, Rgba.White));
            var v = new ImageBuffer(10, 10);
            v.DrawLineClipped(3, double.MaxValue, 3, -double.MaxValue, Rgba.White, 0, 0, 9, 9);
            Check.Equal(10, Lit(v, Rgba.White));
        }

        // Differential against exact rational clipping. Far endpoints are only representable on lines through the origin
        // (any other line rounds away from itself at 1e100), so each segment runs from (X, Y) to (-X·2^m, -Y·2^m), exactly
        // collinear with the origin, against a random rectangle around the origin.
        public void DrawLineClipped_FarEndpoints_MatchExactClipping()
        {
            var rng = new Random(52);
            int failures = 0, drawn = 0;
            for (int trial = 0; trial < 600; trial++)
            {
                double X = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(0, 300));
                double Y = (rng.NextDouble() * 2 - 1) * Math.Pow(10, rng.Next(0, 300));
                if (rng.Next(4) == 0) Y = X * (rng.Next(-3, 4) / 2.0);                  // gentle slopes too
                double scale = Math.Pow(2, rng.Next(0, 8));
                double x1 = -X * scale, y1 = -Y * scale;
                if (double.IsInfinity(x1) || double.IsInfinity(y1)) continue;
                int left = -rng.Next(0, 150), top = -rng.Next(0, 150), right = left + 199, bottom = top + 199;
                var actual = new ImageBuffer(200, 200);
                actual.DrawLineClipped(X, Y, x1, y1, Rgba.White, left, top, right, bottom);
                var expected = new ImageBuffer(200, 200);
                var ends = ExactClip(X, Y, x1, y1, left, top, right, bottom);
                if (ends != null)
                {
                    var (ax, ay, bx, by) = ends.Value;
                    expected.DrawLine(ax, ay, bx, by, Rgba.White);
                    drawn++;
                }
                if (!actual.Pixels.SequenceEqual(expected.Pixels)) failures++;
            }
            Check.True(drawn > 400, "drawn " + drawn);
            Check.Equal(0, failures);                                              // was 1e17+: pixels off or missing
        }

        // The same reference for ordinary segments: random real endpoints around and across the rectangle.
        public void DrawLineClipped_NearSegments_MatchExactClipping()
        {
            var rng = new Random(5253);
            int failures = 0;
            for (int trial = 0; trial < 3000; trial++)
            {
                double R() => rng.Next(4) == 0 ? rng.Next(-300, 400) + rng.Next(2) * 0.5 : rng.NextDouble() * 700 - 300;
                double x0 = R(), y0 = R(), x1 = R(), y1 = R();
                if (rng.Next(8) == 0) x1 = x0;
                if (rng.Next(8) == 0) y1 = y0;
                int left = rng.Next(-20, 60), top = rng.Next(-20, 60), right = left + rng.Next(0, 120), bottom = top + rng.Next(0, 120);
                var actual = new ImageBuffer(150, 150);
                actual.DrawLineClipped(x0, y0, x1, y1, Rgba.White, left, top, right, bottom);
                var expected = new ImageBuffer(150, 150);
                if (ExactClip(x0, y0, x1, y1, left, top, right, bottom) is var (ax, ay, bx, by))
                    expected.DrawLine(ax, ay, bx, by, Rgba.White);
                if (!actual.Pixels.SequenceEqual(expected.Pixels)) failures++;
            }
            Check.Equal(0, failures);
        }

        // Liang–Barsky over exact rationals (t = n / d), then each end rounded half-to-even, as the method documents.
        private static (int, int, int, int)? ExactClip(double x0, double y0, double x1, double y1, int left, int top, int right, int bottom)
        {
            var (X0, Y0, X1, Y1) = (Exact(x0), Exact(y0), Exact(x1), Exact(y1));
            var dx = Sub(X1, X0);
            var dy = Sub(Y1, Y0);
            var t0 = (N: System.Numerics.BigInteger.Zero, D: System.Numerics.BigInteger.One);
            var t1 = (N: System.Numerics.BigInteger.One, D: System.Numerics.BigInteger.One);
            bool Edge((System.Numerics.BigInteger N, System.Numerics.BigInteger D) p, (System.Numerics.BigInteger N, System.Numerics.BigInteger D) q)
            {
                if (p.N.IsZero) return q.N.Sign >= 0;
                var t = (N: q.N * p.D, D: q.D * p.N);
                if (t.D.Sign < 0) t = (-t.N, -t.D);
                if (p.N.Sign < 0) { if (Cmp(t, t1) > 0) return false; if (Cmp(t, t0) > 0) t0 = t; }
                else { if (Cmp(t, t0) < 0) return false; if (Cmp(t, t1) < 0) t1 = t; }
                return true;
            }
            var L = Exact(left); var R = Exact(right); var T = Exact(top); var B = Exact(bottom);
            if (!Edge(Neg(dx), Sub(X0, L)) || !Edge(dx, Sub(R, X0)) || !Edge(Neg(dy), Sub(Y0, T)) || !Edge(dy, Sub(B, Y0))) return null;
            int At((System.Numerics.BigInteger N, System.Numerics.BigInteger D) v0, (System.Numerics.BigInteger N, System.Numerics.BigInteger D) d,
                (System.Numerics.BigInteger N, System.Numerics.BigInteger D) t, int lo, int hi)
            {
                var v = Add(v0, (d.N * t.N, d.D * t.D));
                var r = RoundHalfEven(v);
                return (int)System.Numerics.BigInteger.Max(lo, System.Numerics.BigInteger.Min(hi, r));
            }
            return (At(X0, dx, t0, left, right), At(Y0, dy, t0, top, bottom), At(X0, dx, t1, left, right), At(Y0, dy, t1, top, bottom));
        }

        private static (System.Numerics.BigInteger N, System.Numerics.BigInteger D) Exact(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0) exp = 1; else mant |= 1L << 52;
            exp -= 1075;
            System.Numerics.BigInteger n = bits < 0 ? -mant : mant;
            return exp >= 0 ? (n << exp, System.Numerics.BigInteger.One) : (n, System.Numerics.BigInteger.One << -exp);
        }

        private static (System.Numerics.BigInteger, System.Numerics.BigInteger) Add((System.Numerics.BigInteger N, System.Numerics.BigInteger D) a, (System.Numerics.BigInteger N, System.Numerics.BigInteger D) b)
            => (a.N * b.D + b.N * a.D, a.D * b.D);
        private static (System.Numerics.BigInteger, System.Numerics.BigInteger) Neg((System.Numerics.BigInteger N, System.Numerics.BigInteger D) a) => (-a.N, a.D);
        private static (System.Numerics.BigInteger, System.Numerics.BigInteger) Sub((System.Numerics.BigInteger N, System.Numerics.BigInteger D) a, (System.Numerics.BigInteger N, System.Numerics.BigInteger D) b)
            => Add(a, Neg(b));
        private static int Cmp((System.Numerics.BigInteger N, System.Numerics.BigInteger D) a, (System.Numerics.BigInteger N, System.Numerics.BigInteger D) b)
            => (a.N * b.D).CompareTo(b.N * a.D);                                   // both denominators positive

        private static System.Numerics.BigInteger RoundHalfEven((System.Numerics.BigInteger N, System.Numerics.BigInteger D) v)
        {
            var (n, d) = v.D.Sign < 0 ? (-v.N, -v.D) : (v.N, v.D);
            var q = System.Numerics.BigInteger.DivRem(n, d, out var r);
            if (r.Sign < 0) { q -= 1; r += d; }                                    // floor
            int c = (2 * r).CompareTo(d);
            return c > 0 || (c == 0 && !q.IsEven) ? q + 1 : q;
        }

        public void DrawLineClipped_AHorizontalLineAt1e200_LightsTheWholeRow()
        {
            var image = new ImageBuffer(1000, 3);
            image.DrawLineClipped(-1e200, 1, 1e200, 1, Rgba.White, 0, 0, 999, 2);  // lit 1 of 1000
            Check.Equal(1000, Lit(image, Rgba.White));
            var image17 = new ImageBuffer(1000, 3);
            image17.DrawLineClipped(-1e17, 1, 1e17 + 3, 1, Rgba.White, 0, 0, 999, 2);  // lit 993 of 1000
            Check.Equal(1000, Lit(image17, Rgba.White));
        }

        // ---------- checksums ----------

        public void Checksums_AnOffsetOutsideTheArray_IsReportedAsOffset()
        {
            var data = new byte[4];
            Check.Equal("offset", Check.Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(data, 5, 0)).ParamName);   // was "count"
            Check.Equal("offset", Check.Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(data, -1, 1)).ParamName);
            Check.Equal("offset", Check.Throws<ArgumentOutOfRangeException>(() => new Crc32Hasher().Append(data, 5, 0)).ParamName);
            Check.Equal("offset", Check.Throws<ArgumentOutOfRangeException>(() => new Crc16Hasher().Append(data, -1, 0)).ParamName);
            Check.Equal("offset", Check.Throws<ArgumentOutOfRangeException>(() => new Adler32Hasher().Append(data, 5, 0)).ParamName);
            Check.Equal("count", Check.Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(data, 2, 3)).ParamName);
            Check.Equal("count", Check.Throws<ArgumentOutOfRangeException>(() => new Adler32Hasher().Append(data, 0, -1)).ParamName);
            Check.Equal(Crc32.Compute(Array.Empty<byte>()), Crc32.Compute(data, 4, 0));   // an empty segment at the end is fine
        }

        // ---------- encodings ----------

        public void Base32_NonZeroTrailingBitsAndPartialPadding_AreRejected()
        {
            Check.True(Base32.Decode("MY").SequenceEqual(new byte[] { 0x66 }));
            Check.Throws<FormatException>(() => Base32.Decode("MZ"));             // decoded like "MY"
            Check.Throws<FormatException>(() => Base32.Decode("MY="));            // partial padding
            Check.Throws<FormatException>(() => Base32.Decode("MY====="));
            Check.True(Base32.Decode("MY======").SequenceEqual(new byte[] { 0x66 }));
            Check.True(Base32.Decode("my ====== ").SequenceEqual(new byte[] { 0x66 }));
            Check.True(Base32.Decode("MZXW6===").SequenceEqual(Ascii("foo")));
            Check.True(Base32.Decode("MZXW6YQ=").SequenceEqual(Ascii("foob")));
            Check.True(Base32.Decode("MZXW6YTB").SequenceEqual(Ascii("fooba")));
            Check.Throws<FormatException>(() => Base32.Decode("MZXW6YTB========"));
        }

        // Differential: every short symbol string decodes iff re-encoding the result gives it back (one spelling per value).
        public void Base32_EveryShortString_DecodesIffCanonical()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var rng = new Random(5232);
            int bad = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                int len = rng.Next(1, 9);
                var sb = new StringBuilder();
                for (int i = 0; i < len; i++) sb.Append(alphabet[rng.Next(32)]);
                string s = sb.ToString();
                byte[]? decoded = null;
                try { decoded = Base32.Decode(s); } catch (FormatException) { }
                bool canonical = decoded != null && Base32.Encode(decoded, padding: false) == s;
                bool encodable = (len % 8 is 0 or 2 or 4 or 5 or 7) && Base32.Decode(Base32.Encode(Base32Probe(s), padding: false)) != null;
                if ((decoded != null) != (Base32.Encode(Base32Probe(s), padding: false) == s)) bad++;
                if (decoded != null && !canonical) bad++;
                _ = encodable;
            }
            Check.Equal(0, bad);
        }

        // The bytes an encoder would have to be given to produce s (its whole bytes, trailing bits dropped).
        private static byte[] Base32Probe(string s)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bytes = new System.Collections.Generic.List<byte>();
            int buffer = 0, bits = 0;
            foreach (char c in s)
            {
                buffer = (buffer << 5) | alphabet.IndexOf(c);
                bits += 5;
                if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
            }
            return bytes.ToArray();
        }

        public void Base64Url_NonZeroTrailingBits_AreRejected()
        {
            Check.True(Base64Url.Decode("QQ").SequenceEqual(new byte[] { 0x41 }));
            Check.Throws<FormatException>(() => Base64Url.Decode("QR"));          // decoded like "QQ"
            Check.Throws<FormatException>(() => Base64Url.Decode("QUF"));         // like "QUE"
            Check.True(Base64Url.Decode("QUE").SequenceEqual(Ascii("AA")));
            Check.True(Base64Url.Decode("_w").SequenceEqual(new byte[] { 0xFF }));
            Check.Throws<FormatException>(() => Base64Url.Decode("__"));
        }

        // Differential over every 2- and 3-character string: accepted iff it is what the encoder produces.
        public void Base64Url_EveryShortString_DecodesIffCanonical()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            int bad = 0, accepted = 0;
            foreach (char a in alphabet)
                foreach (char b in alphabet)
                {
                    foreach (string s in new[] { "" + a + b }.Concat(alphabet.Select(c => "" + a + b + c)))
                    {
                        byte[]? decoded = null;
                        try { decoded = Base64Url.Decode(s); } catch (FormatException) { }
                        if (decoded == null) { if (Base64Url.Encode(Probe64(s)) == s) bad++; continue; }
                        accepted++;
                        if (Base64Url.Encode(decoded) != s) bad++;
                    }
                }
            Check.Equal(0, bad);
            Check.Equal(64 * 4 + 64 * 64 * 16, accepted);                          // 4 of 64 last-character values, then 16
        }

        private static byte[] Probe64(string s)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            var bytes = new System.Collections.Generic.List<byte>();
            int buffer = 0, bits = 0;
            foreach (char c in s)
            {
                buffer = (buffer << 6) | alphabet.IndexOf(c);
                bits += 6;
                if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
            }
            return bytes.ToArray();
        }

        public void VarInt_AZeroPaddedEncoding_DecodesLikeTheMinimalOne()
        {
            // Documented (protobuf, DWARF and wasm allow it); the doc used to say over-long input was rejected.
            Check.Equal(0UL, VarInt.DecodeUnsigned(new byte[] { 0x80, 0x00 }, out int read));
            Check.Equal(2, read);
            Check.Throws<FormatException>(() => VarInt.DecodeUnsigned(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 }, out _));
        }

        public void Hex_InputPastTheStringLimit_IsAnArgumentException()
        {
            var ex = Check.Throws<ArgumentException>(() => Hex.Encode(new byte[1 << 30]));   // was OverflowException
            Check.Equal("data", ex.ParamName);
        }

        // ---------- BMP / PNG writers ----------

        public void Bmp_PastTheArrayLimit_IsAnArgumentException()
        {
            // 2,147,483,622 bytes: under int.MaxValue but over the largest array, so it was OutOfMemoryException.
            var image = new ImageBuffer(1, 536870875);
            var ex = Check.Throws<ArgumentException>(() => Bmp.Encode(image));
            Check.Equal("image", ex.ParamName);
        }

        public void PngWriter_AnImageNearTheArrayLimit_IsWrittenInChunks()
        {
            // 1 × 429,496,725: the filtered scanlines (2,147,483,625 bytes) were over the largest array, so it threw
            // OutOfMemoryException after the 33-byte header was already written.
            var image = new ImageBuffer(1, 429496725);
            image.SetPixel(0, 429496724, new Rgba(1, 2, 3, 4));
            var ms = new MemoryStream();
            PngWriter.Write(image, ms);
            var chunks = Chunks(ms.ToArray());
            Check.Equal("IHDR", chunks[0].Type);
            Check.Equal("IEND", chunks[chunks.Count - 1].Type);
            Check.True(chunks.Count > 3, "several IDAT chunks");
            Check.True(chunks.Where(c => c.Type == "IDAT").All(c => c.Data.Length <= 1 << 16));
            byte[] zlib = chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
            long total = 0;
            uint a = 1, b = 0;
            byte last = 0;
            using (var inflate = new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), CompressionMode.Decompress))
            {
                var buf = new byte[1 << 20];
                for (int n; (n = inflate.Read(buf, 0, buf.Length)) > 0;)
                {
                    for (int i = 0; i < n; i++) { a = (a + buf[i]) % 65521; b = (b + a) % 65521; }
                    total += n;
                    last = buf[n - 1];
                }
            }
            Check.Equal(429496725L * 5, total);
            Check.Equal((byte)4, last);
            Check.Equal((b << 16) | a, EndianConverter.ReadUInt32BigEndian(zlib, zlib.Length - 4));
        }

        public void PngWriter_SeveralIdatChunks_RoundTrip()
        {
            var rng = new Random(5252);
            foreach (var (w, h) in new[] { (300, 300), (1, 70000), (20000, 2), (16384, 1), (16383, 3) })
            {
                var image = new ImageBuffer(w, h);
                rng.NextBytes(image.Pixels);
                byte[] png = PngWriter.Encode(image);
                Check.True(Chunks(png).Count(c => c.Type == "IDAT") > 1 || png.Length < 1 << 16, $"{w}x{h}");
                Check.True(PngReader.Decode(png).Pixels.SequenceEqual(image.Pixels), $"{w}x{h}");
            }
            var empty = new ImageBuffer(1, 1);
            Check.True(PngReader.Decode(PngWriter.Encode(empty)).Pixels.SequenceEqual(empty.Pixels));
        }

        private static System.Collections.Generic.List<(string Type, byte[] Data)> Chunks(byte[] png)
        {
            var list = new System.Collections.Generic.List<(string, byte[])>();
            for (int pos = 8; pos < png.Length;)
            {
                int length = (int)EndianConverter.ReadUInt32BigEndian(png, pos);
                var td = new byte[4 + length];
                Array.Copy(png, pos + 4, td, 0, td.Length);
                Check.Equal(Crc32.Compute(td), EndianConverter.ReadUInt32BigEndian(png, pos + 8 + length));
                list.Add((Encoding.ASCII.GetString(td, 0, 4), td.Skip(4).ToArray()));
                pos += 12 + length;
            }
            return list;
        }

        // ---------- PngReader ----------

        private static void Chunk(MemoryStream ms, string type, byte[] body)
        {
            var len = new byte[4];
            EndianConverter.WriteUInt32BigEndian(len, 0, (uint)body.Length);
            ms.Write(len, 0, 4);
            var td = Ascii(type).Concat(body).ToArray();
            ms.Write(td, 0, td.Length);
            var crc = new byte[4];
            EndianConverter.WriteUInt32BigEndian(crc, 0, Crc32.Compute(td));
            ms.Write(crc, 0, 4);
        }

        private static byte[] Zlib(byte[] raw, byte cmf = 0x78)
        {
            var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) d.Write(raw, 0, raw.Length);
            byte flg = 0;
            while (((cmf << 8) | flg) % 31 != 0) flg++;
            var result = new byte[] { cmf, flg }.Concat(ms.ToArray()).ToList();
            var adler = new byte[4];
            EndianConverter.WriteUInt32BigEndian(adler, 0, Adler32.Compute(raw));
            result.AddRange(adler);
            return result.ToArray();
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
            foreach (var (type, body) in chunks) Chunk(ms, type, body);
            return ms.ToArray();
        }

        private static readonly byte[] Rgb1x1 = { 0, 10, 20, 30 };

        public void PngReader_TrailingDecompressedData_IsChecksummedAndIgnored()
        {
            // libpng and Pillow accept it; it was reported as a "zlib checksum mismatch".
            byte[] png = Png(("IHDR", Ihdr(1, 1, 8, 2)), ("IDAT", Zlib(new byte[] { 0, 10, 20, 30, 0, 0, 0, 0 })), ("IEND", Array.Empty<byte>()));
            Check.Equal(new Rgba(10, 20, 30), PngReader.Decode(png).GetPixel(0, 0));
            // A wrong checksum over the whole stream (trailing bytes included) is still caught.
            byte[] z = Zlib(new byte[] { 0, 10, 20, 30, 0, 0, 0, 0 });
            z[z.Length - 1] ^= 1;
            Check.Throws<InvalidDataException>(() => PngReader.Decode(Png(("IHDR", Ihdr(1, 1, 8, 2)), ("IDAT", z), ("IEND", Array.Empty<byte>()))));
        }

        public void PngReader_ChunkLayoutErrors_AreRejected()
        {
            var ihdr = ("IHDR", Ihdr(1, 1, 8, 2));
            var idat = ("IDAT", Zlib(Rgb1x1));
            var iend = ("IEND", Array.Empty<byte>());
            Check.Equal(new Rgba(10, 20, 30), PngReader.Decode(Png(ihdr, idat, iend)).GetPixel(0, 0));
            void Bad(params (string, byte[])[] chunks) => Check.Throws<InvalidDataException>(() => PngReader.Decode(Png(chunks)));
            Bad(ihdr, idat);                                                        // no IEND: a cut-off file decoded
            Bad(ihdr, idat, ("IHDR", Ihdr(1, 1, 16, 4)), iend);                    // re-described the pixels as #0A0A0A1E
            Bad(ihdr, ihdr, idat, iend);
            Bad(("teXt", Ascii("a\0b")), ihdr, idat, iend);                        // IHDR must come first
            Bad(ihdr, ("ab1d", new byte[0]), idat, iend);                          // not a letter
            Bad(ihdr, ("IDAT", Zlib(Rgb1x1).Take(5).ToArray()), ("teXt", Ascii("a\0b")), ("IDAT", Zlib(Rgb1x1).Skip(5).ToArray()), iend);
            Bad(("IHDR", Ihdr(1, 1, 8, 0)), ("PLTE", new byte[3]), ("IDAT", Zlib(new byte[] { 0, 7 })), iend);   // greyscale
            Bad(("IHDR", Ihdr(1, 1, 8, 4)), ("PLTE", new byte[3]), ("IDAT", Zlib(new byte[] { 0, 7, 9 })), iend);
            var pal = ("IHDR", Ihdr(1, 1, 8, 3));
            var palData = ("IDAT", Zlib(new byte[] { 0, 0 }));
            Check.Equal(new Rgba(1, 2, 3, 4), PngReader.Decode(Png(pal, ("PLTE", new byte[] { 1, 2, 3 }), ("tRNS", new byte[] { 4 }), palData, iend)).GetPixel(0, 0));
            Bad(pal, ("PLTE", new byte[] { 1, 2, 3, 4 }), palData, iend);          // not a multiple of 3
            Bad(pal, ("PLTE", new byte[0]), palData, iend);
            Bad(pal, ("PLTE", new byte[771]), palData, iend);
            Bad(pal, ("tRNS", new byte[] { 4 }), ("PLTE", new byte[] { 1, 2, 3 }), palData, iend);   // tRNS before PLTE
            Bad(pal, ("PLTE", new byte[] { 1, 2, 3 }), ("PLTE", new byte[] { 1, 2, 3 }), palData, iend);
            Bad(pal, ("PLTE", new byte[] { 1, 2, 3 }), palData, ("tRNS", new byte[] { 4 }), iend);   // after the data
            Bad(ihdr, idat, ("PLTE", new byte[3]), iend);
            // Split IDAT chunks and an ancillary chunk before them are fine.
            byte[] z = Zlib(Rgb1x1);
            Check.Equal(new Rgba(10, 20, 30),
                PngReader.Decode(Png(ihdr, ("teXt", Ascii("a\0b")), ("IDAT", z.Take(3).ToArray()), ("IDAT", z.Skip(3).ToArray()), iend)).GetPixel(0, 0));
        }

        public void PngReader_ZlibWindowPast32K_IsRejected()
        {
            var ihdr = ("IHDR", Ihdr(1, 1, 8, 2));
            var iend = ("IEND", Array.Empty<byte>());
            Check.Throws<InvalidDataException>(() => PngReader.Decode(Png(ihdr, ("IDAT", Zlib(Rgb1x1, cmf: 0x88)), iend)));   // CINFO 8
            Check.Equal(new Rgba(10, 20, 30), PngReader.Decode(Png(ihdr, ("IDAT", Zlib(Rgb1x1, cmf: 0x08)), iend)).GetPixel(0, 0));
        }

        public void PngReader_CorruptDeflateData_SaysSo()
        {
            // net8 surfaced DeflateStream's "unsupported compression method" message.
            byte[] z = { 0x78, 0x9C, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x01 };
            var ex = Check.Throws<InvalidDataException>(() =>
                PngReader.Decode(Png(("IHDR", Ihdr(1, 1, 8, 2)), ("IDAT", z), ("IEND", Array.Empty<byte>()))));
            Check.True(ex.Message.Contains("PNG"), ex.Message);
        }

        public void PngReader_ALargeImage_PeaksNearItsOwnSize()
        {
            // 4000 × 4000 RGBA16 (128 MB of scanlines, 64 MB decoded) peaked near 3× the scanlines while inflating them whole.
            int w = 4000, h = 4000;
            int rowBytes = w * 8;
            var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[rowBytes + 1];
                for (int y = 0; y < h; y++) d.Write(row, 0, row.Length);
            }
            var raw = ms.ToArray();
            var zlib = new byte[] { 0x78, 0x01 }.Concat(raw).Concat(AdlerZeros((long)h * (rowBytes + 1))).ToArray();
            byte[] png = Png(("IHDR", Ihdr(w, h, 16, 6)), ("IDAT", zlib), ("IEND", Array.Empty<byte>()));
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var image = PngReader.Decode(png);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.Equal(w, image.Width);
            Check.True(allocated < 64_000_000L + 8 * png.Length + 4_000_000, $"allocated {allocated:N0} bytes");   // was ~350 MB
        }

        // The decoder refuses data too short to inflate to the image (deflate's ceiling is 1032:1); zlib's best on all-zero
        // rows comes close to that ceiling and must still decode.
        public void PngReader_MaximallyCompressedData_StillDecodes()
        {
            int w = 5000, h = 5000;
            var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[w + 1];
                for (int y = 0; y < h; y++) d.Write(row, 0, row.Length);
            }
            long raw = (long)h * (w + 1);
            byte[] zlib = new byte[] { 0x78, 0xDA }.Concat(ms.ToArray()).Concat(AdlerZeros(raw)).ToArray();
            Check.True(raw > 900L * zlib.Length, $"ratio {raw / zlib.Length}");
            var image = PngReader.Decode(Png(("IHDR", Ihdr(w, h, 8, 0)), ("IDAT", zlib), ("IEND", Array.Empty<byte>())));
            Check.Equal(new Rgba(0, 0, 0), image.GetPixel(w - 1, h - 1));
            // The same stream under a header claiming twice the rows is refused before the image is allocated.
            long before = GC.GetAllocatedBytesForCurrentThread();
            Check.Throws<InvalidDataException>(() => PngReader.Decode(Png(("IHDR", Ihdr(w, h * 2, 8, 0)), ("IDAT", zlib), ("IEND", Array.Empty<byte>()))));
            Check.True(GC.GetAllocatedBytesForCurrentThread() - before < 8L << 20, "allocated the image");
        }

        private static byte[] AdlerZeros(long count)
        {
            // Adler-32 of `count` zero bytes: a stays 1, b grows by 1 per byte.
            uint b = (uint)(count % 65521);
            var r = new byte[4];
            EndianConverter.WriteUInt32BigEndian(r, 0, (b << 16) | 1);
            return r;
        }
    }
}
