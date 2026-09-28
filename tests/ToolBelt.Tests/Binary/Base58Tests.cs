using System;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Base58Tests
    {
        public void Empty()
        {
            Check.Equal("", Base58.Encode(Array.Empty<byte>()));
            Check.Equal(0, Base58.Decode("").Length);
        }

        public void KnownVector()
        {
            // "Hello World!" in ASCII encodes to this well-known Base58 string.
            Check.Equal("2NEpo7TZRRrLZSi2U", Base58.Encode(Encoding.ASCII.GetBytes("Hello World!")));
        }

        public void LeadingZeros_BecomeOnes()
        {
            Check.Equal("1", Base58.Encode(new byte[] { 0 }));
            Check.Equal("11", Base58.Encode(new byte[] { 0, 0 }));
            var decoded = Base58.Decode("11z");
            Check.Equal(0, decoded[0]);
            Check.Equal(0, decoded[1]);
        }

        public void InvalidChar_Throws()
        {
            Check.Throws<FormatException>(() => Base58.Decode("0OIl")); // all excluded from the alphabet
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Base58.Encode(null!));
            Check.Throws<ArgumentNullException>(() => Base58.Decode(null!));
        }

        // Property: Decode(Encode(x)) == x, including buffers with leading zeros.
        public void RoundTrip_OverRandomBytes()
        {
            var rng = new Random(58);
            for (int trial = 0; trial < 3000; trial++)
            {
                var data = new byte[rng.Next(0, 40)];
                rng.NextBytes(data);
                // Occasionally force leading zeros.
                if (data.Length > 2 && rng.Next(3) == 0)
                    data[0] = data[1] = 0;

                Check.True(Base58.Decode(Base58.Encode(data)).SequenceEqual(data), $"trial {trial} (len {data.Length})");
            }
        }
    }
}
