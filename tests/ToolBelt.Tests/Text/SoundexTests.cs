using System;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class SoundexTests
    {
        public void KnownVectors()
        {
            Check.Equal("R163", Soundex.Encode("Robert"));
            Check.Equal("R163", Soundex.Encode("Rupert"));
            Check.Equal("A261", Soundex.Encode("Ashcraft"));   // h/w bridging rule
            Check.Equal("T522", Soundex.Encode("Tymczak"));
            Check.Equal("P236", Soundex.Encode("Pfister"));    // adjacent same-code letters collapse
            Check.Equal("H555", Soundex.Encode("Honeyman"));
        }

        public void PadsAndTruncatesToFour()
        {
            Check.Equal("A000", Soundex.Encode("A"));
            Check.Equal("W252", Soundex.Encode("Washington")); // W-A-S(2)-H-I-N(5)-G-T(3)... -> W252
        }

        public void CaseInsensitive_AndIgnoresNonLetters()
        {
            Check.Equal(Soundex.Encode("Robert"), Soundex.Encode("r-o-b-e-r-t"));
            Check.Equal(Soundex.Encode("Robert"), Soundex.Encode("ROBERT"));
        }

        public void NoLetters_ReturnsEmpty()
        {
            Check.Equal("", Soundex.Encode(""));
            Check.Equal("", Soundex.Encode("123 !@#"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Soundex.Encode(null!));
        }
    }
}
