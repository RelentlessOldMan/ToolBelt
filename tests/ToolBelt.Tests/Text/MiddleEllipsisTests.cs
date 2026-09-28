using System;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class MiddleEllipsisTests
    {
        public void ShorterThanMax_Unchanged()
        {
            Check.Equal("short", MiddleEllipsis.Apply("short", 10));
            Check.Equal("exact", MiddleEllipsis.Apply("exact", 5));
        }

        public void KeepsHeadAndTail()
        {
            // budget = 10 - 1 = 9; head = 5, tail = 4.
            Check.Equal("veryl….txt", MiddleEllipsis.Apply("verylongfilename.txt", 10));
        }

        public void CustomEllipsis()
        {
            // budget = 9 - 3 = 6; head 3, tail 3.
            Check.Equal("abc...xyz", MiddleEllipsis.Apply("abcdefghijklmnopqrstuvwxyz", 9, "..."));
        }

        public void EllipsisLongerThanMax_ReturnsPartial()
        {
            Check.Equal("..", MiddleEllipsis.Apply("abcdef", 2, "..."));
            Check.Equal("", MiddleEllipsis.Apply("abcdef", 0));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => MiddleEllipsis.Apply(null!, 5));
            Check.Throws<ArgumentOutOfRangeException>(() => MiddleEllipsis.Apply("x", -1));
        }

        // Property: result never exceeds max; when truncated it starts with the original head and ends
        // with the original tail.
        public void Properties_OverRandomStrings()
        {
            var rng = new Random(4242);
            for (int trial = 0; trial < 2000; trial++)
            {
                int len = rng.Next(0, 40);
                var sb = new StringBuilder(len);
                for (int i = 0; i < len; i++)
                    sb.Append((char)('a' + rng.Next(0, 26)));
                string input = sb.ToString();
                int max = rng.Next(0, 45);

                string result = MiddleEllipsis.Apply(input, max);
                Check.True(result.Length <= max, $"trial {trial}: '{result}' exceeds {max}");

                if (input.Length <= max)
                {
                    Check.Equal(input, result);
                }
                else if (max > 1) // ellipsis is 1 char; need room for head+tail
                {
                    int budget = max - 1;
                    int head = (budget + 1) / 2;
                    int tail = budget - head;
                    Check.True(result.StartsWith(input.Substring(0, head), StringComparison.Ordinal),
                        $"trial {trial}: head mismatch");
                    Check.True(result.EndsWith(input.Substring(input.Length - tail), StringComparison.Ordinal),
                        $"trial {trial}: tail mismatch");
                }
            }
        }
    }
}
