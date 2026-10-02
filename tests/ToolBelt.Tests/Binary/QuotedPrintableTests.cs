using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class QuotedPrintableTests
    {
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        public void PlainAscii_PassesThrough()
        {
            Check.Equal("Hello, World!", QuotedPrintable.Encode(Ascii("Hello, World!")));
        }

        public void EqualsSign_IsEscaped()
        {
            Check.Equal("a=3Db", QuotedPrintable.Encode(Ascii("a=b")));
        }

        public void NonAscii_BytesBecomeHexEscapes()
        {
            // UTF-8 for "é" is C3 A9.
            Check.Equal("=C3=A9", QuotedPrintable.Encode(new byte[] { 0xC3, 0xA9 }));
        }

        public void TrailingWhitespace_IsEscaped()
        {
            Check.Equal("x=20", QuotedPrintable.Encode(Ascii("x ")));
            Check.Equal("x=09", QuotedPrintable.Encode(new byte[] { (byte)'x', (byte)'\t' }));
            // ...but interior whitespace stays literal.
            Check.Equal("a b", QuotedPrintable.Encode(Ascii("a b")));
        }

        public void Newlines_AreFullyEncoded()
        {
            // Byte-exact encoder: CR/LF in the data are escaped, not emitted as literal line breaks.
            Check.Equal("a=0D=0Ab", QuotedPrintable.Encode(Ascii("a\r\nb")));
        }

        public void Decode_HandlesSoftBreaksAndCaseInsensitiveHex()
        {
            Check.True(((ReadOnlySpan<byte>)Decode("a=\r\nb")).SequenceEqual(Ascii("ab")));   // =\r\n soft break
            Check.True(((ReadOnlySpan<byte>)Decode("a=\nb")).SequenceEqual(Ascii("ab")));      // =\n soft break
            Check.True(((ReadOnlySpan<byte>)Decode("=c3=a9")).SequenceEqual(new byte[] { 0xC3, 0xA9 })); // lowercase
        }

        public void LinesNeverExceed76Chars()
        {
            var data = new byte[300];
            for (int i = 0; i < data.Length; i++) data[i] = 0xC8; // each -> "=C8" (3 chars)
            string encoded = QuotedPrintable.Encode(data);
            Check.True(encoded.Contains("=\r\n"), "expected soft line breaks");
            foreach (string line in encoded.Split(new[] { "\r\n" }, StringSplitOptions.None))
                Check.True(line.Length <= 76, $"line too long ({line.Length}): {line}");
        }

        public void Decode_Malformed_Throws()
        {
            Check.Throws<FormatException>(() => QuotedPrintable.Decode("abc="));   // truncated escape
            Check.Throws<FormatException>(() => QuotedPrintable.Decode("a=ZZb"));  // non-hex digits
        }

        public void Property_RoundTripsAnyBytes()
        {
            var r = new DeterministicRandom(2026);
            for (int trial = 0; trial < 1000; trial++)
            {
                var data = new byte[r.Next(0, 300)];
                r.NextBytes(data);
                byte[] back = QuotedPrintable.Decode(QuotedPrintable.Encode(data));
                Check.True(((ReadOnlySpan<byte>)data).SequenceEqual(back), $"round-trip failed at trial {trial} (len {data.Length})");
            }
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => QuotedPrintable.Encode(null!));
            Check.Throws<ArgumentNullException>(() => QuotedPrintable.Decode(null!));
        }

        private static byte[] Decode(string s) => QuotedPrintable.Decode(s);
    }
}
