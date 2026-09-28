using System;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class DotEnvTests
    {
        public void BasicPairs()
        {
            var env = DotEnv.Parse("A=1\nB=hello");
            Check.Equal("1", env["A"]);
            Check.Equal("hello", env["B"]);
        }

        public void IgnoresBlankLinesAndComments()
        {
            var env = DotEnv.Parse("\n# a comment\n  \nK=V\n");
            Check.Equal(1, env.Count);
            Check.Equal("V", env["K"]);
        }

        public void ExportPrefixStripped()
        {
            var env = DotEnv.Parse("export PATH_LIKE=/usr/bin");
            Check.Equal("/usr/bin", env["PATH_LIKE"]);
        }

        public void QuotedValues()
        {
            var env = DotEnv.Parse("D=\"a b c\"\nS='literal # not comment'");
            Check.Equal("a b c", env["D"]);
            Check.Equal("literal # not comment", env["S"]);
        }

        public void DoubleQuoteEscapes()
        {
            var env = DotEnv.Parse("E=\"line1\\nline2\\ttab\"");
            Check.Equal("line1\nline2\ttab", env["E"]);
        }

        public void UnquotedInlineCommentStripped()
        {
            var env = DotEnv.Parse("K=value   # trailing comment");
            Check.Equal("value", env["K"]);
        }

        public void LaterKeysOverride()
        {
            var env = DotEnv.Parse("K=1\nK=2");
            Check.Equal("2", env["K"]);
        }

        public void SkipsLinesWithoutEquals()
        {
            var env = DotEnv.Parse("no_equals_here\nGOOD=1");
            Check.Equal(1, env.Count);
            Check.True(env.ContainsKey("GOOD"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => DotEnv.Parse(null!));
        }
    }
}
