using System;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class Base85Tests
    {
        public void KnownVector()
        {
            // The classic Ascii85 example: "Man " -> "9jqo^".
            Check.Equal("9jqo^", Base85.Encode(Encoding.ASCII.GetBytes("Man ")));
            Check.Equal("Man ", Encoding.ASCII.GetString(Base85.Decode("9jqo^")));
        }

        public void Empty()
        {
            Check.Equal("", Base85.Encode(Array.Empty<byte>()));
            Check.Equal(0, Base85.Decode("").Length);
        }

        public void PartialGroups()
        {
            // 1 byte -> 2 chars, 2 -> 3, 3 -> 4, 4 -> 5.
            Check.Equal(2, Base85.Encode(new byte[] { 1 }).Length);
            Check.Equal(3, Base85.Encode(new byte[] { 1, 2 }).Length);
            Check.Equal(4, Base85.Encode(new byte[] { 1, 2, 3 }).Length);
            Check.Equal(5, Base85.Encode(new byte[] { 1, 2, 3, 4 }).Length);
        }

        public void InvalidChar_Throws()
        {
            Check.Throws<FormatException>(() => Base85.Decode("9j~o^")); // '~' is out of range
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Base85.Encode(null!));
            Check.Throws<ArgumentNullException>(() => Base85.Decode(null!));
        }

        // Property: Decode(Encode(x)) == x, and output uses only '!'..'u'.
        public void RoundTrip_OverRandomBytes()
        {
            var rng = new Random(85858);
            for (int trial = 0; trial < 3000; trial++)
            {
                var data = new byte[rng.Next(0, 50)];
                rng.NextBytes(data);

                string encoded = Base85.Encode(data);
                foreach (char c in encoded)
                    Check.True(c >= '!' && c <= 'u', $"trial {trial}: char '{c}' out of range");

                Check.True(Base85.Decode(encoded).SequenceEqual(data), $"trial {trial}: round-trip (len {data.Length})");
            }
        }
    }
}
