using System;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Identifiers
{
    public sealed class ShortGuidTests
    {
        public void EncodeProducesUrlSafe22Chars()
        {
            var guid = Guid.Parse("c9a646d3-9c61-4cb7-bfcd-ee2522c8f633");
            string s = ShortGuid.Encode(guid);
            Check.Equal(22, s.Length);
            foreach (char c in s)
                Check.True(char.IsLetterOrDigit(c) || c == '-' || c == '_', $"non-url-safe char '{c}'");
        }

        public void RoundTrip()
        {
            var guid = Guid.Parse("c9a646d3-9c61-4cb7-bfcd-ee2522c8f633");
            Check.Equal(guid, ShortGuid.Decode(ShortGuid.Encode(guid)));
        }

        public void EmptyGuid()
        {
            string s = ShortGuid.Encode(Guid.Empty);
            Check.Equal(22, s.Length);
            Check.Equal(Guid.Empty, ShortGuid.Decode(s));
        }

        public void Decode_Invalid_Throws()
        {
            Check.Throws<FormatException>(() => ShortGuid.Decode("tooshort"));
            Check.Throws<FormatException>(() => ShortGuid.Decode(new string('!', 22))); // bad chars
            Check.Throws<ArgumentNullException>(() => ShortGuid.Decode(null!));
        }

        public void TryDecode()
        {
            var guid = Guid.NewGuid();
            Check.True(ShortGuid.TryDecode(ShortGuid.Encode(guid), out var back) && back == guid);
            Check.False(ShortGuid.TryDecode("nope", out _));
            Check.False(ShortGuid.TryDecode(null!, out _));
        }

        // Property: encode -> decode is the identity for arbitrary Guids.
        public void Property_RoundTrip()
        {
            for (int t = 0; t < 5000; t++)
            {
                var guid = Guid.NewGuid();
                string s = ShortGuid.Encode(guid);
                Check.Equal(22, s.Length, $"t{t}: length");
                Check.Equal(guid, ShortGuid.Decode(s), $"t{t}: round-trip");
            }
        }
    }
}
