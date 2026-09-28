using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class WordWrapTests
    {
        public void Basic()
        {
            var lines = WordWrap.Wrap("the quick brown fox", 9);
            Check.True(lines.SequenceEqual(new[] { "the quick", "brown fox" }));
        }

        public void CollapsesWhitespace()
        {
            var lines = WordWrap.Wrap("  the   quick\n\tbrown ", 20);
            Check.True(lines.SequenceEqual(new[] { "the quick brown" }));
        }

        public void HardBreaksOverlongWord()
        {
            var lines = WordWrap.Wrap("supercalifragilistic", 5);
            Check.True(lines.SequenceEqual(new[] { "super", "calif", "ragil", "istic" }));
        }

        public void OverlongWordAmongOthers()
        {
            var lines = WordWrap.Wrap("hi abcdefghij yo", 4);
            // "hi" fits; "abcdefghij" hard-breaks into 4s leaving "ij"; "yo" cannot join "ij"
            // ("ij yo" is 5 > 4), so it lands on its own line.
            Check.True(lines.SequenceEqual(new[] { "hi", "abcd", "efgh", "ij", "yo" }));
        }

        public void EmptyOrWhitespace_ReturnsNoLines()
        {
            Check.Equal(0, WordWrap.Wrap("", 10).Count);
            Check.Equal(0, WordWrap.Wrap("   \n\t ", 10).Count);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => WordWrap.Wrap(null!, 10));
            Check.Throws<ArgumentOutOfRangeException>(() => WordWrap.Wrap("x", 0));
        }

        public void WrapToString_JoinsWithNewline()
        {
            Check.Equal("the quick\nbrown fox", WordWrap.WrapToString("the quick brown fox", 9));
        }

        // Properties: every line fits the width, no line has edge spaces, and re-splitting the wrapped
        // output recovers exactly the original word sequence (after hard-break re-joining).
        public void Properties_OverRandomText()
        {
            var rng = new Random(56789);
            for (int trial = 0; trial < 1000; trial++)
            {
                int width = rng.Next(1, 12);
                int wordCount = rng.Next(0, 15);
                var words = new List<string>();
                for (int w = 0; w < wordCount; w++)
                {
                    int len = rng.Next(1, 16); // some words exceed width → force hard-breaks
                    var sb = new StringBuilder(len);
                    for (int k = 0; k < len; k++)
                        sb.Append((char)('a' + rng.Next(0, 26)));
                    words.Add(sb.ToString());
                }
                string input = string.Join("  ", words);

                var lines = WordWrap.Wrap(input, width);

                foreach (var line in lines)
                {
                    Check.True(line.Length <= width, $"trial {trial}: line '{line}' exceeds width {width}");
                    Check.True(line.Length == line.Trim().Length, $"trial {trial}: line '{line}' has edge spaces");
                }

                // Concatenating all line content (dropping the wrap spaces) must equal the original
                // words concatenated — hard-breaking splits a word but never drops/reorders characters.
                string rejoined = string.Concat(lines.SelectMany(l => l.Split(' ')));
                string expected = string.Concat(words);
                Check.Equal(expected, rejoined, $"trial {trial}: content preserved (w={width})");
            }
        }
    }
}
