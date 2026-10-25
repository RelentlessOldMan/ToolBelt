using System;
using System.IO;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    // Regressions from review round 6 (binary formats & images).
    public sealed class BinaryImagesReviewTests
    {
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        public void VarInt_TenthByteBeyondBit63IsRejected()
        {
            var max = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };
            Check.Equal(ulong.MaxValue, VarInt.DecodeUnsigned(max, out int n));
            Check.Equal(10, n);
            var over = (byte[])max.Clone();
            over[9] = 0x02;                                                    // bit 64: used to vanish silently
            Check.Throws<FormatException>(() => VarInt.DecodeUnsigned(over, out _));
            Check.Equal(long.MinValue, VarInt.DecodeSigned(VarInt.EncodeSigned(long.MinValue), out _));
        }

        public void QuotedPrintable_RejectsCharactersOutsideAscii()
        {
            Check.Throws<FormatException>(() => QuotedPrintable.Decode("café"));
            Check.Throws<FormatException>(() => QuotedPrintable.Decode("Ł"));  // was truncated to 'A'
        }

        public void QuotedPrintable_DropsTransportPadding()
        {
            Check.True(QuotedPrintable.Decode("a=  \r\nb").SequenceEqual(Ascii("ab")));
            Check.True(QuotedPrintable.Decode("a=\t\nb").SequenceEqual(Ascii("ab")));
            Check.True(QuotedPrintable.Decode("a  \r\nb").SequenceEqual(Ascii("a\r\nb")));
            Check.True(QuotedPrintable.Decode("a b").SequenceEqual(Ascii("a b")));
        }

        public void Base32_RejectsDataAfterPaddingAndImpossibleLengths()
        {
            Check.True(Base32.Decode("MY======").SequenceEqual(Ascii("f")));
            Check.True(Base32.Decode("MZXW6===\n").SequenceEqual(Ascii("foo")));
            Check.Throws<FormatException>(() => Base32.Decode("MY======MZXW6==="));
            Check.Throws<FormatException>(() => Base32.Decode("M"));
            Check.Throws<FormatException>(() => Base32.Decode("MZX"));
            Check.Throws<FormatException>(() => Base32.Decode("MZXW6Y"));
        }

        public void Crockford_RejectsImpossibleLengthsAndFoldsAsciiOnly()
        {
            Check.Throws<FormatException>(() => CrockfordBase32.Decode("C"));
            Check.Throws<FormatException>(() => CrockfordBase32.Decode("CSQ"));
            Check.Throws<FormatException>(() => CrockfordBase32.Decode("Cſ"));   // long s used to fold to 'S'
            Check.True(CrockfordBase32.Decode("cs").SequenceEqual(CrockfordBase32.Decode("CS")));
        }

        public void Base64Url_RejectsTheStandardAlphabetAndWhitespace()
        {
            Check.Throws<FormatException>(() => Base64Url.Decode("+/8A"));
            Check.Throws<FormatException>(() => Base64Url.Decode("AQID    AQID"));
            Check.True(Base64Url.Decode("-_8A").SequenceEqual(new byte[] { 0xFB, 0xFF, 0x00 }));
        }

        public void SegmentChecksDoNotOverflow()
        {
            var data = new byte[4];
            Check.Throws<ArgumentOutOfRangeException>(() => Crc32.Compute(data, 2, int.MaxValue));
            Check.Throws<ArgumentOutOfRangeException>(() => new Crc32Hasher().Append(data, 2, int.MaxValue));
            Check.Throws<ArgumentOutOfRangeException>(() => new Crc16Hasher().Append(data, 2, int.MaxValue));
            Check.Throws<ArgumentOutOfRangeException>(() => new Adler32Hasher().Append(data, 2, int.MaxValue));
            Check.Throws<ArgumentOutOfRangeException>(() => EndianConverter.ReadUInt32BigEndian(data, int.MaxValue - 1));
            Check.Throws<ArgumentOutOfRangeException>(() => EndianConverter.WriteUInt64LittleEndian(data, int.MaxValue - 3, 1));
        }

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

        public void Png_RowWiderThanIntIsRejectedUpFront()
        {
            // 40M × 1 RGBA16: 4e7 pixels is under MaxPixels, but a row is 2.56e9 bits — rowBytes wrapped negative.
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            EndianConverter.WriteUInt32BigEndian(ihdr, 0, 40_000_000);
            EndianConverter.WriteUInt32BigEndian(ihdr, 4, 1);
            ihdr[8] = 16; ihdr[9] = 6;
            Chunk(ms, "IHDR", ihdr);
            Chunk(ms, "IDAT", new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 });
            Chunk(ms, "IEND", Array.Empty<byte>());
            var ex = Check.Throws<InvalidDataException>(() => PngReader.Decode(ms.ToArray()));
            Check.True(ex.Message.Contains("too large"), ex.Message);
        }

        public void Bmp_WideImageStrideDoesNotWrap()
        {
            // w × 32 bits overflowed int for w ≥ 2^26, giving a negative stride and an OverflowException.
            var image = new ImageBuffer((1 << 26) + 1, 1);
            image.SetPixel(1 << 26, 0, new Rgba(1, 2, 3, 4));
            byte[] bmp = Bmp.Encode(image);
            Check.Equal(122 + ((1 << 26) + 1) * 4, bmp.Length);
            int last = bmp.Length - 4;
            Check.Equal((byte)3, bmp[last]);
            Check.Equal((byte)4, bmp[last + 3]);
        }

        public void ImageBuffer_BlitOntoItselfDoesNotSmear()
        {
            var image = new ImageBuffer(3, 1);
            image.SetPixel(0, 0, Rgba.Red);
            image.SetPixel(1, 0, Rgba.Green);
            image.SetPixel(2, 0, Rgba.Blue);
            image.Blit(image, 1, 0);
            Check.Equal(Rgba.Red, image.GetPixel(0, 0));
            Check.Equal(Rgba.Red, image.GetPixel(1, 0));
            Check.Equal(Rgba.Green, image.GetPixel(2, 0));
        }

        public void SvgDocument_RejectsNonFiniteNumbersAndWritesNoNegativeZero()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(double.PositiveInfinity, 10));
            Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(10, 10).Line(double.NaN, 0, 1, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => new SvgDocument(10, 10).Circle(0, double.PositiveInfinity, 1));
            string svg = new SvgDocument(10, 10).Circle(-1e-9, 1, 1).ToString();
            Check.True(svg.Contains("cx=\"0\""), svg);
        }

        public void SvgDocument_ReplacesCharactersXmlForbids()
        {
            string svg = new SvgDocument(10, 10).Text(0, 0, "a\u001Bb", fill: "red\uD800").ToString();
            Check.True(svg.Contains(">a�b<"), svg);
            Check.True(svg.Contains("fill=\"red�\""), svg);
            Check.False(svg.Contains("\u001B"));
        }

        public void SvgDocument_TextNullArgumentsAreNamed()
        {
            Check.Equal("fill", Check.Throws<ArgumentNullException>(() => new SvgDocument(10, 10).Text(0, 0, "x", fill: null!)).ParamName);
            Check.Equal("anchor", Check.Throws<ArgumentNullException>(() => new SvgDocument(10, 10).Text(0, 0, "x", anchor: null!)).ParamName);
            Check.Equal("fontFamily", Check.Throws<ArgumentNullException>(() => new SvgDocument(10, 10).Text(0, 0, "x", fontFamily: null!)).ParamName);
        }

        public void SvgUtils_ErrorsNameTheBadArgument()
        {
            Check.Equal("y", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.Translate(0, double.NaN)).ParamName);
            Check.Equal("f", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.Matrix(1, 0, 0, 1, 0, double.NaN)).ParamName);
            Check.Equal("cy", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.Rotate(45, 1, double.NaN)).ParamName);
            Check.Equal("width", Check.Throws<ArgumentOutOfRangeException>(() => SvgUtils.StrokeAttributes(Rgba.Red, double.NaN)).ParamName);
        }
    }
}
