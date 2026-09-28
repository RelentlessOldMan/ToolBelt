using System;
using System.Linq;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class CommandLineSplitterTests
    {
        public void SimpleTokens()
        {
            Check.True(CommandLineSplitter.Split("a b c").SequenceEqual(new[] { "a", "b", "c" }));
            Check.True(CommandLineSplitter.Split("  a\t b ").SequenceEqual(new[] { "a", "b" }));
        }

        public void QuotedGroupsPreserveWhitespace()
        {
            Check.True(CommandLineSplitter.Split("a \"b c\" d").SequenceEqual(new[] { "a", "b c", "d" }));
            Check.True(CommandLineSplitter.Split("'single quoted'").SequenceEqual(new[] { "single quoted" }));
        }

        public void QuotesCanAbutText()
        {
            Check.True(CommandLineSplitter.Split("abc\"d e\"f").SequenceEqual(new[] { "abcd ef" }));
        }

        public void DoubledQuoteIsLiteral()
        {
            Check.True(CommandLineSplitter.Split("\"he said \"\"hi\"\"\"").SequenceEqual(new[] { "he said \"hi\"" }));
        }

        public void EmptyQuotedToken()
        {
            Check.True(CommandLineSplitter.Split("a \"\" b").SequenceEqual(new[] { "a", "", "b" }));
        }

        public void EmptyOrWhitespace_NoTokens()
        {
            Check.Equal(0, CommandLineSplitter.Split("").Length);
            Check.Equal(0, CommandLineSplitter.Split("   \t ").Length);
        }

        public void UnterminatedQuote_Throws()
        {
            Check.Throws<FormatException>(() => CommandLineSplitter.Split("a \"unterminated"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => CommandLineSplitter.Split(null!));
        }
    }
}
