using System;
using System.Linq;
using System.Text;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    /// <summary>Regressions from review round 174 (Text & small types, tenth pass): each test failed before its fix.</summary>
    public sealed class TextReview174Tests
    {
        // ---------------- TextAlign ----------------

        private static readonly string[] Pieces = { "\uFE0F", "\uFE0F", "\uFE0F", "a", "Z", "❤", "中", "·", " ", "\u001b[1m" };
        private static readonly char[] Pads = { ' ', '█', '·', '-', '░' };

        private static string RandomText(Random rng, int pieces)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pieces; i++) sb.Append(Pieces[rng.Next(Pieces.Length)]);
            return sb.ToString();
        }

        // Only the first of several leading U+FE0Fs was dropped, so the next joined the pad or the character before the
        // ellipsis and widened it: Fit("\uFE0F\uFE0F", 1, Right, '█') measured 2.
        public void TextAlign_LeadingSelectorRuns_DoNotWidenThePadOrTheEllipsis()
        {
            Check.Equal(1, TextAlign.DisplayWidth(TextAlign.Fit("\uFE0F\uFE0F", 1, Alignment.Right, '█')));
            Check.Equal(4, TextAlign.DisplayWidth(TextAlign.PadLeft("\uFE0F\uFE0Fx", 4, '·')));
            Check.True(TextAlign.DisplayWidth(TextAlign.Fit("❤abc", 2, ellipsis: "\uFE0F\uFE0F…")) <= 2);

            var rng = new Random(174);
            for (int trial = 0; trial < 5000; trial++)
            {
                string text = RandomText(rng, rng.Next(7));
                char pad = Pads[rng.Next(Pads.Length)];
                int width = rng.Next(9);
                int natural = TextAlign.DisplayWidth(text);
                string shown = text.Replace("\uFE0F", "<FE0F>").Replace("\u001b", "<ESC>") + " pad " + pad + " width " + width;

                Check.Equal(Math.Max(width, natural), TextAlign.DisplayWidth(TextAlign.PadLeft(text, width, pad)), "PadLeft " + shown);
                Check.Equal(Math.Max(width, natural), TextAlign.DisplayWidth(TextAlign.Center(text, width, pad)), "Center " + shown);
                Check.Equal(Math.Max(width, natural), TextAlign.DisplayWidth(TextAlign.PadRight(text, width, pad)), "PadRight " + shown);
                var alignment = (Alignment)rng.Next(3);
                Check.True(TextAlign.DisplayWidth(TextAlign.Fit(text, width, alignment, pad)) <= width, "Fit " + shown);
                string ellipsis = string.Concat(Enumerable.Repeat("\uFE0F", rng.Next(4))) + "…";
                Check.True(TextAlign.DisplayWidth(TextAlign.Fit(text, width, alignment, pad, ellipsis: ellipsis)) <= width, "Fit with ellipsis " + shown);
            }
        }

        // ---------------- CaseConverter ----------------

        // .NET 8 upper-cases the combining ypogegrammeni U+0345 to '\u0399'; .NET Framework's gap table lacked it, so it stayed a
        // mark there: "a\u0345b" in CONSTANT_CASE was "A\u0399B" on one runtime and "A\u0345B" on the other.
        public void CaseConverter_Ypogegrammeni_UpperCasesToIotaOnEveryRuntime()
        {
            Check.Equal("A\u0399B", CaseConverter.ToConstantCase("a\u0345b"));
            Check.Equal("a\u03B9b", CaseConverter.ToSnakeCase(CaseConverter.ToConstantCase("a\u0345b")));

            var rng = new Random(1741);
            string[] parts = { "a", "q", "\u03B1", "\u03C9", "\u0345", "_" };
            for (int trial = 0; trial < 2000; trial++)
            {
                var sb = new StringBuilder("x");
                int n = rng.Next(8);
                for (int i = 0; i < n; i++)
                {
                    string part = parts[rng.Next(parts.Length)];
                    if (part == "\u0345" && !char.IsLetter(sb[sb.Length - 1])) continue;   // the mark only after a letter
                    sb.Append(part);
                }
                string s = sb.ToString();
                // After a letter the mark upper-cases as a small iota would.
                Check.Equal(CaseConverter.ToConstantCase(s.Replace("\u0345", "\u03B9")), CaseConverter.ToConstantCase(s), s);
            }
        }
    }
}
