using System;
using System.Linq;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class CommandLineBuilderTests
    {
        public void QuotesWhenNeeded()
        {
            Check.Equal("plain", CommandLineBuilder.Quote("plain"));
            Check.Equal("\"has space\"", CommandLineBuilder.Quote("has space"));
            Check.Equal("\"\"", CommandLineBuilder.Quote(""));            // empty needs quotes
            Check.Equal("\"a\"\"b\"", CommandLineBuilder.Quote("a\"b"));  // embedded quote doubled
        }

        public void JoinsArguments()
        {
            Check.Equal("a \"b c\" d", CommandLineBuilder.Join(new[] { "a", "b c", "d" }));
        }

        public void NullArgument_Throws()
        {
            Check.Throws<ArgumentNullException>(() => CommandLineBuilder.Quote(null!));
            Check.Throws<ArgumentNullException>(() => CommandLineBuilder.Join(null!));
        }

        // The core contract: Split(Join(args)) == args for arbitrary arguments.
        public void Property_RoundTripThroughSplitter()
        {
            var rng = new Random(38);
            var alphabet = new[] { "a", "b", " ", "\t", "\"", "'", "x", "" };
            for (int trial = 0; trial < 5000; trial++)
            {
                int argc = rng.Next(1, 6);
                var args = new string[argc];
                for (int i = 0; i < argc; i++)
                {
                    int len = rng.Next(0, 6);
                    var chars = new char[len];
                    for (int j = 0; j < len; j++)
                    {
                        string piece = alphabet[rng.Next(alphabet.Length)];
                        chars[j] = piece.Length > 0 ? piece[0] : 'z';
                    }
                    args[i] = new string(chars);
                }

                string line = CommandLineBuilder.Join(args);
                string[] roundTripped = CommandLineSplitter.Split(line);
                Check.True(roundTripped.SequenceEqual(args),
                    $"trial {trial}: [{string.Join("|", args)}] -> '{line}' -> [{string.Join("|", roundTripped)}]");
            }
        }
    }
}
