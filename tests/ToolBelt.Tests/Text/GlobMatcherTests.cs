using System;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class GlobMatcherTests
    {
        public void StarAndQuestion()
        {
            Check.True(GlobMatcher.IsMatch("readme.txt", "*.txt"));
            Check.False(GlobMatcher.IsMatch("readme.md", "*.txt"));
            Check.True(GlobMatcher.IsMatch("cat", "c?t"));
            Check.False(GlobMatcher.IsMatch("coat", "c?t"));
            Check.True(GlobMatcher.IsMatch("anything", "*"));
            Check.True(GlobMatcher.IsMatch("", "*"));
        }

        public void AnchoredWholeString()
        {
            Check.False(GlobMatcher.IsMatch("xreadme.txt", "readme*"));
            Check.True(GlobMatcher.IsMatch("readme.txt", "readme*"));
        }

        public void CharacterClasses()
        {
            Check.True(GlobMatcher.IsMatch("file1", "file[0-9]"));
            Check.False(GlobMatcher.IsMatch("filex", "file[0-9]"));
            Check.True(GlobMatcher.IsMatch("cat", "[cb]at"));
            Check.True(GlobMatcher.IsMatch("bat", "[cb]at"));
        }

        public void NegatedClass()
        {
            Check.True(GlobMatcher.IsMatch("dat", "[!cb]at"));
            Check.False(GlobMatcher.IsMatch("cat", "[!cb]at"));
            Check.True(GlobMatcher.IsMatch("dat", "[^cb]at")); // ^ negation alias
        }

        public void IgnoreCase()
        {
            Check.True(GlobMatcher.IsMatch("README.TXT", "*.txt", ignoreCase: true));
            Check.False(GlobMatcher.IsMatch("README.TXT", "*.txt", ignoreCase: false));
            Check.True(GlobMatcher.IsMatch("ABC", "[a-z][a-z][a-z]", ignoreCase: true));
        }

        public void UnterminatedClass_Throws()
        {
            Check.Throws<FormatException>(() => GlobMatcher.IsMatch("x", "[abc"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => GlobMatcher.IsMatch(null!, "*"));
            Check.Throws<ArgumentNullException>(() => GlobMatcher.IsMatch("x", null!));
        }

        // Differential: for patterns using only '*', '?', and literals, our matcher must agree with an
        // equivalent anchored regex over random inputs. (Classes are covered by the explicit tests above.)
        public void Differential_MatchesRegexReference()
        {
            var rng = new Random(31415);
            for (int trial = 0; trial < 5000; trial++)
            {
                string pattern = RandomFrom(rng, "ab*?", maxLen: 8);
                string input = RandomFrom(rng, "ab", maxLen: 8);

                bool actual = GlobMatcher.IsMatch(input, pattern);
                bool expected = Regex.IsMatch(input, "^" + GlobToRegex(pattern) + "$");
                Check.Equal(expected, actual, $"trial {trial}: input '{input}' pattern '{pattern}'");
            }
        }

        private static string GlobToRegex(string glob)
        {
            var sb = new StringBuilder();
            foreach (char c in glob)
            {
                sb.Append(c switch
                {
                    '*' => ".*",
                    '?' => ".",
                    _ => Regex.Escape(c.ToString()),
                });
            }
            return sb.ToString();
        }

        private static string RandomFrom(Random rng, string palette, int maxLen)
        {
            int len = rng.Next(0, maxLen + 1);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(palette[rng.Next(palette.Length)]);
            return sb.ToString();
        }
    }
}
