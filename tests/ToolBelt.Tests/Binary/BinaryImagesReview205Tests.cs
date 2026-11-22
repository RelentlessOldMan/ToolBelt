using System;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>Round 205 review regressions (binary formats and images, twelfth pass).</summary>
    public sealed class BinaryImagesReview205Tests
    {
        // Unicode 15.0 EastAsianWidth.txt, East_Asian_Width W, in the BMP blocks IsWide missed: the emoji among the
        // symbols (U+2300..U+2BFF), Hangul Jamo Extended-A and the vertical forms.
        private static readonly int[] Wide =
        {
            0x231A, 0x231B, 0x2329, 0x232A, 0x23E9, 0x23EC, 0x23F0, 0x23F0, 0x23F3, 0x23F3, 0x25FD, 0x25FE,
            0x2614, 0x2615, 0x2648, 0x2653, 0x267F, 0x267F, 0x2693, 0x2693, 0x26A1, 0x26A1, 0x26AA, 0x26AB,
            0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4, 0x26EA, 0x26EA, 0x26F2, 0x26F3,
            0x26F5, 0x26F5, 0x26FA, 0x26FA, 0x26FD, 0x26FD, 0x2705, 0x2705, 0x270A, 0x270B, 0x2728, 0x2728,
            0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755, 0x2757, 0x2757, 0x2795, 0x2797, 0x27B0, 0x27B0,
            0x27BF, 0x27BF, 0x2B1B, 0x2B1C, 0x2B50, 0x2B50, 0x2B55, 0x2B55, 0xA960, 0xA97C, 0xFE10, 0xFE19,
        };

        private static bool IsWide(int cp)
        {
            for (int i = 0; i < Wide.Length; i += 2)
                if (cp >= Wide[i] && cp <= Wide[i + 1]) return true;
            return false;
        }

        // The watch, the star, the check mark and the rest measured 556 where the docs give emoji a full em; the
        // characters around them that are not wide keep their old width.
        public void EstimateTextWidth_WideBmpEmoji_TakeAnEm()
        {
            foreach (var (lo, hi) in new[] { (0x2300, 0x2BFF), (0xA950, 0xA98F), (0xFE00, 0xFE2F) })
                for (int cp = lo; cp <= hi; cp++)
                {
                    string s = ((char)cp).ToString();
                    double w = SvgUtils.EstimateTextWidth(s, 1000);
                    if (IsWide(cp)) Check.Equal(1000.0, w, $"U+{cp:X4}");
                    else Check.True(w < 1000, $"U+{cp:X4} is {w}");
                }
        }

        // U+FE0F asks for the emoji form, an em wide: a red heart and a keycap digit measured as text.
        public void EstimateTextWidth_EmojiPresentation_TakesAnEm()
        {
            Check.Equal(556.0, SvgUtils.EstimateTextWidth("\u2764", 1000));
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth("\u2764\uFE0F", 1000));
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth("1\uFE0F\u20E3", 1000));
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth("\u2B50\uFE0F", 1000));      // already wide: not two ems
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth("\uD83D\uDE00\uFE0F", 1000)); // astral: unchanged
            Check.Equal(2000.0, SvgUtils.EstimateTextWidth("\u2764\uFE0F\u2764\uFE0F", 1000));
            Check.Equal(0.0, SvgUtils.EstimateTextWidth("\uFE0F", 1000));
            Check.Equal(556.0, SvgUtils.EstimateTextWidth("a\uFE0F", 1000));            // a letter has no emoji form
            // A heart joined into an emoji sequence is still part of the glyph before it.
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth("\uD83D\uDC69\u200D\u2764\uFE0F", 1000));
        }
    }
}
