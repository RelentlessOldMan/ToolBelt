using System;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Binary
{
    public sealed class LuhnTests
    {
        public void KnownValid()
        {
            Check.True(Luhn.IsValid("79927398713"));   // classic Luhn example
            Check.False(Luhn.IsValid("79927398714"));
        }

        public void ComputeCheckDigit()
        {
            Check.Equal(3, Luhn.ComputeCheckDigit("7992739871"));
            Check.Equal("79927398713", Luhn.AppendCheckDigit("7992739871"));
        }

        public void NonDigit_Throws()
        {
            Check.Throws<FormatException>(() => Luhn.IsValid("1234a"));
            Check.Throws<FormatException>(() => Luhn.ComputeCheckDigit("12x4"));
        }

        public void Empty_IsInvalid()
        {
            Check.False(Luhn.IsValid(""));
        }

        // Property: appending the computed check digit always yields a Luhn-valid string.
        public void Property_AppendedIsAlwaysValid()
        {
            var rng = new Random(1971);
            for (int trial = 0; trial < 5000; trial++)
            {
                int len = rng.Next(1, 20);
                var sb = new StringBuilder(len);
                for (int i = 0; i < len; i++)
                    sb.Append((char)('0' + rng.Next(0, 10)));
                string payload = sb.ToString();

                string full = Luhn.AppendCheckDigit(payload);
                Check.True(Luhn.IsValid(full), $"trial {trial}: '{full}' should be valid");
            }
        }
    }
}
