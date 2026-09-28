using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Base32Tests
    {
        // The canonical RFC 4648 §10 test vectors.
        public void Rfc4648_Vectors_Encode()
        {
            Check.Equal("", Base32.Encode(Ascii("")));
            Check.Equal("MY======", Base32.Encode(Ascii("f")));
            Check.Equal("MZXQ====", Base32.Encode(Ascii("fo")));
            Check.Equal("MZXW6===", Base32.Encode(Ascii("foo")));
            Check.Equal("MZXW6YQ=", Base32.Encode(Ascii("foob")));
            Check.Equal("MZXW6YTB", Base32.Encode(Ascii("fooba")));
            Check.Equal("MZXW6YTBOI======", Base32.Encode(Ascii("foobar")));
        }

        public void Rfc4648_Vectors_Decode()
        {
            Check.Equal("f", AsStr(Base32.Decode("MY======")));
            Check.Equal("fo", AsStr(Base32.Decode("MZXQ====")));
            Check.Equal("foo", AsStr(Base32.Decode("MZXW6===")));
            Check.Equal("foob", AsStr(Base32.Decode("MZXW6YQ=")));
            Check.Equal("fooba", AsStr(Base32.Decode("MZXW6YTB")));
            Check.Equal("foobar", AsStr(Base32.Decode("MZXW6YTBOI======")));
        }

        public void Decode_IsCaseInsensitive_AndIgnoresWhitespace()
        {
            Check.Equal("foobar", AsStr(Base32.Decode("mzxw6ytb oi======")));
            Check.Equal("foobar", AsStr(Base32.Decode("MZXW6YTBOI")));       // no padding
        }

        public void Decode_InvalidChar_Throws()
        {
            Check.Throws<FormatException>(() => Base32.Decode("MZXW0==="));   // '0' not in alphabet
        }

        public void Encode_NoPadding_OmitsEquals()
        {
            Check.Equal("MY", Base32.Encode(Ascii("f"), padding: false));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Base32.Encode(null!));
            Check.Throws<ArgumentNullException>(() => Base32.Decode(null!));
        }

        // Property: Decode(Encode(x)) == x for random byte arrays, with and without padding.
        public void RoundTrip_OverRandomBytes()
        {
            var rng = new Random(80081);
            for (int trial = 0; trial < 2000; trial++)
            {
                var data = new byte[rng.Next(0, 40)];
                rng.NextBytes(data);

                var padded = Base32.Decode(Base32.Encode(data, padding: true));
                var unpadded = Base32.Decode(Base32.Encode(data, padding: false));

                Check.True(BytesEqual(data, padded), $"trial {trial}: padded round-trip (len {data.Length})");
                Check.True(BytesEqual(data, unpadded), $"trial {trial}: unpadded round-trip (len {data.Length})");
            }
        }

        public void Encode_PaddedLength_IsMultipleOfEight()
        {
            var rng = new Random(1001);
            for (int trial = 0; trial < 200; trial++)
            {
                var data = new byte[rng.Next(1, 40)];
                rng.NextBytes(data);
                Check.Equal(0, Base32.Encode(data).Length % 8, $"trial {trial}: padded length");
            }
        }

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
        private static string AsStr(byte[] b) => Encoding.ASCII.GetString(b);

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
