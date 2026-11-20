using System;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>
    /// Round 191 review regressions (binary formats and images, eleventh pass): each failed on the code before its fix.
    /// </summary>
    public sealed class BinaryImagesReview191Tests
    {
        private static double Width(string s) => SvgUtils.EstimateTextWidth(s, 1000);

        // Every astral character that is not zero width counted a full em, so a math-bold A (U+1D400) or a Deseret letter
        // (U+10400) measured 1000 where the docs give non-ASCII letters the average, 556. Graded over random astral code
        // points against the block rule: wide (emoji and pictographs, CJK extensions, kana, Tangut) an em, else 556.
        public void EstimateTextWidth_AstralLetters_TakeTheAverageWidth()
        {
            Check.Equal(556.0, Width(char.ConvertFromUtf32(0x1D400)), "math bold A");
            Check.Equal(556.0, Width(char.ConvertFromUtf32(0x10400)), "Deseret letter");
            Check.Equal(Width("\u00E9"), Width(char.ConvertFromUtf32(0x10330)), "Gothic letter as e acute");
            Check.Equal(1000.0, Width(char.ConvertFromUtf32(0x1F600)), "emoji");
            Check.Equal(1000.0, Width(char.ConvertFromUtf32(0x20000)), "CJK extension B");
            Check.Equal(1000.0, Width(char.ConvertFromUtf32(0x1B000)), "kana supplement");
            var rng = new Random(19101);
            int letters = 0, wide = 0;
            for (int trial = 0; trial < 20000; trial++)
            {
                int cp = 0x10000 + rng.Next(0x30000);
                // Zero-width marks and tags, regional indicators (paired) and skin tones have their own rules.
                double alone = Width(char.ConvertFromUtf32(cp));
                if (alone == 0 || (cp >= 0x1F1E6 && cp <= 0x1F1FF) || (cp >= 0x1F3FB && cp <= 0x1F3FF)) continue;
                bool isWide = (cp >= 0x16FE0 && cp <= 0x1B2FF) || (cp >= 0x1F000 && cp <= 0x1FAFF) || (cp >= 0x20000 && cp <= 0x3FFFD);
                Check.Equal(isWide ? 1000.0 : 556.0, alone, "U+" + cp.ToString("X"));
                if (isWide) wide++; else letters++;
            }
            Check.True(letters > 1000 && wide > 1000, $"letters {letters}, wide {wide}");
        }

        // U+2028 and U+2029 break a line, as \n does (0), but measured as letters (556).
        public void EstimateTextWidth_LineAndParagraphSeparators_TakeNoWidth()
        {
            Check.Equal(Width("ab"), Width("a\u2028b"));
            Check.Equal(Width("ab"), Width("a\u2029b"));
            Check.Equal(Width("a\nb"), Width("a\u2028b"));
            Check.Equal(0.0, Width("\u2028\u2029"));
        }

        // A positive stroke width that writes as "0" at six decimals (1e-7) gave stroke-width="0", which draws no stroke;
        // FitWidth had the same rounding fixed. Graded over random tiny widths.
        public void StrokeAttributes_TinyPositiveWidth_StillStrokes()
        {
            var black = new Rgba(0, 0, 0);
            Check.Equal("stroke=\"#000000\" stroke-width=\"0.000001\"", SvgUtils.StrokeAttributes(black, 1e-7));
            Check.Equal("stroke=\"#000000\" stroke-width=\"0\"", SvgUtils.StrokeAttributes(black, 0));
            Check.Equal("stroke=\"#000000\" stroke-width=\"0\"", SvgUtils.StrokeAttributes(black, -0.0));
            Check.Equal("stroke=\"#000000\" stroke-width=\"1.5\"", SvgUtils.StrokeAttributes(black, 1.5));
            var rng = new Random(19102);
            for (int trial = 0; trial < 2000; trial++)
            {
                double w = trial % 2 == 0 ? rng.NextDouble() * 4.9e-7 : Math.Pow(10, -320 + rng.NextDouble() * 313);
                if (w == 0) continue;
                string s = SvgUtils.StrokeAttributes(black, w);
                Check.False(s.Contains("stroke-width=\"0\""), $"{w:R}: {s}");
            }
        }
    }
}
