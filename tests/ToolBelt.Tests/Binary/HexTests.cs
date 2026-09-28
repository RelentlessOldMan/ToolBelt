using System;
using System.Linq;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class HexTests
    {
        public void EncodeKnown()
        {
            byte[] data = { 0x00, 0xFF, 0x10, 0xAB };
            Check.Equal("00ff10ab", Hex.Encode(data));
            Check.Equal("00FF10AB", Hex.Encode(data, upperCase: true));
        }

        public void DecodeKnown_IsCaseInsensitive()
        {
            Check.True(Hex.Decode("00ff10ab").SequenceEqual(new byte[] { 0x00, 0xFF, 0x10, 0xAB }));
            Check.True(Hex.Decode("00FF10AB").SequenceEqual(new byte[] { 0x00, 0xFF, 0x10, 0xAB }));
        }

        public void Empty()
        {
            Check.Equal("", Hex.Encode(Array.Empty<byte>()));
            Check.Equal(0, Hex.Decode("").Length);
        }

        public void OddLength_Throws()
        {
            Check.Throws<FormatException>(() => Hex.Decode("abc"));
        }

        public void InvalidChar_Throws()
        {
            Check.Throws<FormatException>(() => Hex.Decode("00zz"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Hex.Encode(null!));
            Check.Throws<ArgumentNullException>(() => Hex.Decode(null!));
        }

        // Property: Decode(Encode(x)) == x for both cases, over random buffers.
        public void RoundTrip_OverRandomBytes()
        {
            var rng = new Random(0xBEEF);
            for (int trial = 0; trial < 3000; trial++)
            {
                var data = new byte[rng.Next(0, 50)];
                rng.NextBytes(data);

                Check.True(Hex.Decode(Hex.Encode(data)).SequenceEqual(data), $"trial {trial}: lower");
                Check.True(Hex.Decode(Hex.Encode(data, upperCase: true)).SequenceEqual(data), $"trial {trial}: upper");
            }
        }
    }
}
