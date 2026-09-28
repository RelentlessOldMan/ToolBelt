using System;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class MaskTests
    {
        public void Trailing()
        {
            Check.Equal("************3456", Mask.Trailing("1234567890123456", 4));
            Check.Equal("****", Mask.Trailing("abcd", 0));
        }

        public void Leading()
        {
            Check.Equal("1234************", Mask.Leading("1234567890123456", 4));
        }

        public void VisibleGreaterOrEqualLength_Unchanged()
        {
            Check.Equal("abc", Mask.Trailing("abc", 5));
            Check.Equal("abc", Mask.Leading("abc", 3));
        }

        public void CustomMaskChar()
        {
            Check.Equal("XXcret", Mask.Trailing("secret", 4, 'X'));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Mask.Trailing(null!, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => Mask.Leading("x", -1));
        }

        // Property: length is preserved and the visible window matches the original.
        public void Properties_OverRandomStrings()
        {
            var rng = new Random(4);
            for (int trial = 0; trial < 2000; trial++)
            {
                int len = rng.Next(0, 30);
                var sb = new StringBuilder(len);
                for (int i = 0; i < len; i++)
                    sb.Append((char)('a' + rng.Next(0, 26)));
                string value = sb.ToString();
                int visible = rng.Next(0, 35);

                string trailing = Mask.Trailing(value, visible);
                Check.Equal(value.Length, trailing.Length, $"trial {trial}: trailing length");
                int shown = Math.Min(visible, value.Length);
                if (shown > 0 && shown < value.Length)
                    Check.Equal(value.Substring(value.Length - shown), trailing.Substring(trailing.Length - shown),
                        $"trial {trial}: trailing window");

                string leading = Mask.Leading(value, visible);
                Check.Equal(value.Length, leading.Length, $"trial {trial}: leading length");
            }
        }
    }
}
