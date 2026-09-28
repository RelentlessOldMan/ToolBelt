using System;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class CrockfordBase32Tests
    {
        public void Empty()
        {
            Check.Equal("", CrockfordBase32.Encode(Array.Empty<byte>()));
            Check.Equal(0, CrockfordBase32.Decode("").Length);
        }

        public void EncodeUsesCrockfordAlphabet()
        {
            // 0xFF -> 11111 111(00) -> indices 31,28 -> 'Z','W' in the Crockford alphabet.
            Check.Equal("ZW", CrockfordBase32.Encode(new byte[] { 0xFF }));
        }

        public void Decode_NormalizesConfusableLetters()
        {
            // I/L -> 1, O -> 0, and hyphens are ignored; lower-case accepted.
            byte[] canonical = CrockfordBase32.Decode("91JPRV3F");
            Check.True(CrockfordBase32.Decode("9-1-JP-RV-3F").SequenceEqual(canonical), "hyphens ignored");
            Check.True(CrockfordBase32.Decode("91jprv3f").SequenceEqual(canonical), "case-insensitive");

            // 'O' decodes as 0, 'I'/'L' as 1.
            Check.True(CrockfordBase32.Decode("O1").SequenceEqual(CrockfordBase32.Decode("01")));
            Check.True(CrockfordBase32.Decode("I0").SequenceEqual(CrockfordBase32.Decode("10")));
            Check.True(CrockfordBase32.Decode("L0").SequenceEqual(CrockfordBase32.Decode("10")));
        }

        public void InvalidChar_Throws()
        {
            Check.Throws<FormatException>(() => CrockfordBase32.Decode("U")); // U is excluded
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => CrockfordBase32.Encode(null!));
            Check.Throws<ArgumentNullException>(() => CrockfordBase32.Decode(null!));
        }

        // Property: Decode(Encode(x)) == x, and the encoding uses only the Crockford alphabet.
        public void RoundTrip_OverRandomBytes()
        {
            const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            var rng = new Random(1234321);
            for (int trial = 0; trial < 3000; trial++)
            {
                var data = new byte[rng.Next(0, 40)];
                rng.NextBytes(data);

                string encoded = CrockfordBase32.Encode(data);
                foreach (char c in encoded)
                    Check.True(alphabet.IndexOf(c) >= 0, $"trial {trial}: '{c}' not in alphabet");

                Check.True(CrockfordBase32.Decode(encoded).SequenceEqual(data), $"trial {trial}: round-trip");
            }
        }
    }
}
