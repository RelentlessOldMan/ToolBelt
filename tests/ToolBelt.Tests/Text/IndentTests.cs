using System;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class IndentTests
    {
        public void IndentLines_PrefixesEachLine()
        {
            Check.Equal("  a\n  b", Indent.IndentLines("a\nb", "  "));
        }

        public void IndentLines_SkipsBlankByDefault()
        {
            Check.Equal("  a\n\n  b", Indent.IndentLines("a\n\nb", "  "));
        }

        public void IndentLines_CanIndentBlank()
        {
            Check.Equal("  a\n  \n  b", Indent.IndentLines("a\n\nb", "  ", indentBlankLines: true));
        }

        public void Dedent_RemovesCommonWhitespace()
        {
            Check.Equal("a\n  b\nc", Indent.Dedent("    a\n      b\n    c"));
        }

        public void Dedent_IgnoresBlankLinesWhenMeasuring()
        {
            Check.Equal("a\n\nb", Indent.Dedent("    a\n\n    b"));
        }

        public void Dedent_NoCommonPrefix_Unchanged()
        {
            Check.Equal("a\n  b", Indent.Dedent("a\n  b")); // first line has no indent
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Indent.IndentLines(null!, "  "));
            Check.Throws<ArgumentNullException>(() => Indent.Dedent(null!));
        }

        // Property: dedenting after a uniform indent restores the original (for non-blank content).
        public void Property_DedentUndoesUniformIndent()
        {
            var rng = new Random(88);
            for (int trial = 0; trial < 1000; trial++)
            {
                int lineCount = rng.Next(1, 6);
                var lines = new string[lineCount];
                for (int i = 0; i < lineCount; i++)
                {
                    int len = rng.Next(1, 6); // non-blank so measurement is well-defined
                    var chars = new char[len];
                    for (int k = 0; k < len; k++)
                        chars[k] = (char)('a' + rng.Next(0, 26));
                    lines[i] = new string(chars);
                }
                string original = string.Join("\n", lines);

                string indent = new string(' ', rng.Next(1, 5));
                string indented = Indent.IndentLines(original, indent);
                Check.Equal(original, Indent.Dedent(indented), $"trial {trial}");
            }
        }
    }
}
