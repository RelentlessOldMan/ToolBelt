using System;
using System.Linq;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class NaturalComparerTests
    {
        public void SortsEmbeddedNumbersNumerically()
        {
            var input = new[] { "img10", "img2", "img1", "img20" };
            var sorted = input.OrderBy(s => s, NaturalComparer.Ordinal).ToArray();
            Check.True(sorted.SequenceEqual(new[] { "img1", "img2", "img10", "img20" }));
        }

        public void MixedTextAndNumbers()
        {
            var input = new[] { "a10", "a2", "b", "a", "a1" };
            var sorted = input.OrderBy(s => s, NaturalComparer.Ordinal).ToArray();
            Check.True(sorted.SequenceEqual(new[] { "a", "a1", "a2", "a10", "b" }));
        }

        public void PureNumbers()
        {
            Check.True(NaturalComparer.Ordinal.Compare("9", "10") < 0);
            Check.True(NaturalComparer.Ordinal.Compare("100", "99") > 0);
        }

        public void LeadingZeros_CompareEqualByValue()
        {
            Check.Equal(0, NaturalComparer.Ordinal.Compare("x01", "x1"));
            Check.Equal(0, NaturalComparer.Ordinal.Compare("007", "7"));
        }

        public void CaseInsensitiveVariant()
        {
            Check.Equal(0, NaturalComparer.OrdinalIgnoreCase.Compare("Apple2", "apple2"));
            Check.True(NaturalComparer.Ordinal.Compare("Apple", "apple") != 0);
        }

        public void Nulls_SortFirst()
        {
            Check.True(NaturalComparer.Ordinal.Compare(null, "a") < 0);
            Check.True(NaturalComparer.Ordinal.Compare("a", null) > 0);
            Check.Equal(0, NaturalComparer.Ordinal.Compare(null, null));
        }

        // Differential: sign of the comparer must match a reference that zero-pads every digit run to a
        // fixed width and then compares ordinally. Digit runs are kept short so padding cannot overflow.
        public void Differential_MatchesZeroPadReference()
        {
            var rng = new Random(11235);
            const string palette = "aB1290z";
            for (int trial = 0; trial < 5000; trial++)
            {
                string a = RandomString(rng, palette, maxLen: 10);
                string b = RandomString(rng, palette, maxLen: 10);

                int actual = Math.Sign(NaturalComparer.OrdinalIgnoreCase.Compare(a, b));
                int expected = Math.Sign(string.CompareOrdinal(PadNumbers(a), PadNumbers(b)));
                Check.Equal(expected, actual, $"trial {trial}: '{a}' vs '{b}'");
            }
        }

        private static string RandomString(Random rng, string palette, int maxLen)
        {
            int len = rng.Next(0, maxLen + 1);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(palette[rng.Next(palette.Length)]);
            return sb.ToString();
        }

        // Reference: upper-case (to mirror ignore-case) and left-pad each maximal digit run to 12 chars,
        // dropping leading zeros first so value — not width — drives the compare.
        private static string PadNumbers(string s)
        {
            s = s.ToUpperInvariant();
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] >= '0' && s[i] <= '9')
                {
                    int start = i;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                    string digits = s.Substring(start, i - start).TrimStart('0');
                    sb.Append(digits.PadLeft(12, '0'));
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}
