using System;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class TruncateTests
    {
        public void ShorterThanMax_Unchanged()
        {
            Check.Equal("hi", Truncate.WithEllipsis("hi", 10));
            Check.Equal("exact", Truncate.WithEllipsis("exact", 5)); // equal length is unchanged
        }

        public void Truncates_WithEllipsis()
        {
            Check.Equal("hello…", Truncate.WithEllipsis("hello world", 6));
            Check.Equal("ab...", Truncate.WithEllipsis("abcdefg", 5, "..."));
        }

        public void EllipsisLongerThanMax_ReturnsPartialEllipsis()
        {
            Check.Equal("..", Truncate.WithEllipsis("abcdef", 2, "..."));
            Check.Equal("", Truncate.WithEllipsis("abcdef", 0, "..."));
        }

        public void WordAware_CutsAtWordBoundary()
        {
            // budget = 12 - 1 (ellipsis) = 11 → "the quick b" → back to "the quick".
            Check.Equal("the quick…", Truncate.WithEllipsisOnWord("the quick brown fox", 12));
        }

        public void WordAware_FallsBackWhenNoSpace()
        {
            Check.Equal("abcde…", Truncate.WithEllipsisOnWord("abcdefghij", 6));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Truncate.WithEllipsis(null!, 5));
            Check.Throws<ArgumentOutOfRangeException>(() => Truncate.WithEllipsis("x", -1));
        }

        // Property: output never exceeds maxLength, and inputs within budget are returned verbatim.
        public void Properties_OverRandomStrings()
        {
            var rng = new Random(1234);
            for (int trial = 0; trial < 2000; trial++)
            {
                int len = rng.Next(0, 40);
                var sb = new StringBuilder(len);
                for (int i = 0; i < len; i++)
                    sb.Append(rng.Next(4) == 0 ? ' ' : (char)('a' + rng.Next(0, 26)));
                string input = sb.ToString();
                int max = rng.Next(0, 45);

                foreach (var result in new[]
                {
                    Truncate.WithEllipsis(input, max),
                    Truncate.WithEllipsisOnWord(input, max),
                })
                {
                    Check.True(result.Length <= max, $"trial {trial}: '{result}' exceeds max {max}");
                    if (input.Length <= max)
                        Check.Equal(input, result, $"trial {trial}: within-budget input should be unchanged");
                }
            }
        }
    }
}
