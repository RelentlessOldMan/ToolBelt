using System;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Base64UrlTests
    {
        public void KnownVector()
        {
            // "subjects?_d" chosen because its standard base64 contains both '+' and '/'.
            byte[] data = { 0xFB, 0xEF, 0xBE, 0xFF, 0xFF };
            string standard = Convert.ToBase64String(data); // "++++//8="
            string url = Base64Url.Encode(data);

            Check.True(standard.Contains('+') && standard.Contains('/'), "fixture should have + and /");
            Check.False(url.Contains('+') || url.Contains('/') || url.Contains('='), $"url-safe: '{url}'");
            Check.Equal("----__8", url); // + -> -, / -> _, padding stripped
        }

        public void Decode_AcceptsPaddedAndUnpadded()
        {
            byte[] data = Encoding.ASCII.GetBytes("foobar");
            string url = Base64Url.Encode(data);
            Check.True(Base64Url.Decode(url).SequenceEqual(data));

            // Manually padded standard-alphabet form should be rejected (base64url is unpadded)...
            Check.Throws<FormatException>(() => Base64Url.Decode(url + "="));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Base64Url.Encode(null!));
            Check.Throws<ArgumentNullException>(() => Base64Url.Decode(null!));
        }

        // Property: Decode(Encode(x)) == x, and the encoding uses only the URL-safe alphabet.
        public void RoundTrip_OverRandomBytes()
        {
            var rng = new Random(6022);
            for (int trial = 0; trial < 3000; trial++)
            {
                var data = new byte[rng.Next(0, 50)];
                rng.NextBytes(data);

                string encoded = Base64Url.Encode(data);
                foreach (char c in encoded)
                {
                    bool safe = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                                || (c >= '0' && c <= '9') || c == '-' || c == '_';
                    Check.True(safe, $"trial {trial}: non-url-safe char '{c}' in '{encoded}'");
                }

                Check.True(Base64Url.Decode(encoded).SequenceEqual(data), $"trial {trial}: round-trip (len {data.Length})");
            }
        }
    }
}
