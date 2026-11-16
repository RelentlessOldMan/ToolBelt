using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Round 98 review regressions (Text & small types, sixth pass): each failed on the code before its fix.
    public sealed class TextReview98Tests
    {
        // ---- TextAlign: escapes cut short by the next character ----

        public void Fit_AnEscapeCutShortDoesNotSwallowThePadding()
        {
            Check.Equal(6, TextAlign.DisplayWidth(TextAlign.PadRight("ab\u001b\u001b", 6)));
            Check.Equal(6, TextAlign.DisplayWidth(TextAlign.Center("ab\u001b\u001b", 6)));
            Check.Equal(10, TextAlign.DisplayWidth(TextAlign.Fit("\u001b\u001b", 10)));
            Check.Equal(4, TextAlign.DisplayWidth(TextAlign.PadRight("x\u001b\u001b", 4)));
            Check.Equal(2, TextAlign.DisplayWidth(TextAlign.Fit("a\u001b中", 2, ellipsis: "")));
            Check.Equal(3, TextAlign.DisplayWidth(TextAlign.Fit("a\u001b中b", 3)));
        }

        // Every result is exactly the width asked for (or the text's own, when it is wider and not truncated), over random
        // text heavy with escape fragments, wide characters and aborting characters.
        public void Fit_IsExactlyTheWidthOverRandomEscapeFragments()
        {
            string[] parts = { "a", "b", "中", "é", "́", "\u001b", "\u001b", "[", "31", "m", "]", "0;t", "\a", "\u0018", "\u001a", "\\", "(", "B", "P", "❤", "️" };
            var rng = new Random(9801);
            var alignments = new[] { Alignment.Left, Alignment.Right, Alignment.Center };
            for (int trial = 0; trial < 20000; trial++)
            {
                var sb = new StringBuilder();
                int n = rng.Next(0, 9);
                for (int k = 0; k < n; k++) sb.Append(parts[rng.Next(parts.Length)]);
                string text = sb.ToString();
                int width = rng.Next(0, 9);
                Alignment alignment = alignments[rng.Next(3)];
                bool truncate = rng.Next(2) == 0;
                string ellipsis = rng.Next(3) == 0 ? "" : "…";
                int own = TextAlign.DisplayWidth(text);
                int expected = !truncate && own > width ? own : width;
                string fitted = TextAlign.Fit(text, width, alignment, ' ', truncate, ellipsis);
                Check.Equal(expected, TextAlign.DisplayWidth(fitted), $"trial {trial}: Fit({Show(text)}, {width}, {alignment}, truncate {truncate}, \"{ellipsis}\")");
            }
        }

        // ---- TextAlign: the emoji selector ----

        public void DisplayWidth_TheEmojiSelectorWidensPastAnEscape()
        {
            Check.Equal(2, TextAlign.DisplayWidth("❤\u001b[0m️"));
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F321\u001b[31m\u001b]0;t\a️"));
        }

        // Well-formed escapes inserted anywhere take no columns, whatever they sit between.
        public void DisplayWidth_EscapesBetweenAnyCharactersTakeNoColumns()
        {
            string[] chars = { "a", "中", "❤", "️", "\U0001F321", "\U0001F600", "́", "☀", "1", "‍" };
            string[] escapes = { "\u001b[31m", "\u001b[0m", "\u001b]0;title\a", "\u001b(B", "\u001b]8;;x\u001b\\" };
            var rng = new Random(9802);
            for (int trial = 0; trial < 5000; trial++)
            {
                var plain = new StringBuilder();
                var coloured = new StringBuilder();
                int n = rng.Next(1, 8);
                for (int k = 0; k < n; k++)
                {
                    string c = chars[rng.Next(chars.Length)];
                    plain.Append(c);
                    coloured.Append(c);
                    while (rng.Next(3) == 0) coloured.Append(escapes[rng.Next(escapes.Length)]);
                }
                Check.Equal(TextAlign.DisplayWidth(plain.ToString()), TextAlign.DisplayWidth(coloured.ToString()), $"trial {trial}: {Show(coloured.ToString())}");
            }
        }

        public void DisplayWidth_TheEmojiSelectorWidensSupplementarySymbols()
        {
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F321️"));   // thermometer
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F549️"));   // om
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F5E8️"));   // speech bubble
            Check.Equal(" …", TextAlign.Fit("\U0001F321️xyz", 2));
            Check.Equal(2, TextAlign.DisplayWidth(TextAlign.Fit("a\U0001F321️", 2, ellipsis: "")));
        }

        // Guard: without the selector, text-presentation symbols stay one column, and already-wide emoji stay two.
        public void DisplayWidth_WithoutTheSelectorSymbolsKeepTheirWidth()
        {
            Check.Equal(1, TextAlign.DisplayWidth("\U0001F321"));
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F600"));
            Check.Equal(2, TextAlign.DisplayWidth("\U0001F600️"));
            Check.Equal(1, TextAlign.DisplayWidth("\U0001D400️"));   // a math letter is no emoji
        }

        // ---- GlobMatcher: masks per token group ----

        public void Glob_ALongRepeatedClassOverManyCharactersIsFast()
        {
            var pattern = new StringBuilder("*");
            for (int k = 0; k < 20000; k++) pattern.Append("[一-龥]");
            pattern.Append("x*");
            var rng = new Random(9803);
            var input = new char[100000];
            for (int i = 0; i < input.Length; i++) input[i] = (char)rng.Next(0x4E00, 0x9E20);
            var sw = Stopwatch.StartNew();
            Check.False(GlobMatcher.IsMatch(new string(input), pattern.ToString()));
            sw.Stop();
            Check.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
        }

        // Long segments (past one 64-bit word, so the groups hold bitsets) against a naive matcher, both case modes.
        public void Glob_LongSegmentsMatchANaiveMatcher()
        {
            string[] tokens = { "a", "b", "A", "?", "[ab]", "[!a]", "[a-c]", "[B-b]", "[ab]", "a", "a", "*" };
            var rng = new Random(9804);
            for (int trial = 0; trial < 1500; trial++)
            {
                var parts = new List<string>();
                int m = rng.Next(1, 200);
                for (int k = 0; k < m; k++) parts.Add(tokens[rng.Next(tokens.Length)]);
                string pattern = string.Concat(parts);
                var sb = new StringBuilder();
                int n = rng.Next(0, 260);
                for (int k = 0; k < n; k++) sb.Append("abcAB"[rng.Next(5)]);
                if (rng.Next(2) == 0) sb.Insert(0, Instance(parts, rng));
                string input = sb.ToString();
                foreach (bool ignoreCase in new[] { false, true })
                    Check.Equal(Naive(input, parts, ignoreCase), GlobMatcher.IsMatch(input, pattern, ignoreCase), $"trial {trial}, ignoreCase {ignoreCase}");
            }
        }

        // A string matching the tokens (a star becomes zero to two letters), so matches are common.
        private static string Instance(List<string> parts, Random rng)
        {
            var sb = new StringBuilder();
            foreach (string p in parts)
            {
                if (p == "*") { int k = rng.Next(3); for (int i = 0; i < k; i++) sb.Append("abc"[rng.Next(3)]); continue; }
                for (int tries = 0; ; tries++)
                {
                    char c = "abcAB"[rng.Next(5)];
                    if (One(p, c, false) || tries > 50) { sb.Append(c); break; }
                }
            }
            return sb.ToString();
        }

        private static bool Naive(string s, List<string> parts, bool ignoreCase)
        {
            int n = s.Length, m = parts.Count;
            var dp = new bool[n + 1, m + 1];
            dp[0, 0] = true;
            for (int j = 1; j <= m; j++) dp[0, j] = dp[0, j - 1] && parts[j - 1] == "*";
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                    dp[i, j] = parts[j - 1] == "*"
                        ? dp[i, j - 1] || dp[i - 1, j]
                        : dp[i - 1, j - 1] && One(parts[j - 1], s[i - 1], ignoreCase);
            return dp[n, m];
        }

        // ASCII letters only, so the case class of c is just its upper and lower forms.
        private static bool One(string token, char c, bool ignoreCase)
        {
            if (!ignoreCase) return OneExact(token, c);
            char lo = char.ToLowerInvariant(c), up = char.ToUpperInvariant(c);
            if (token[0] != '[') return OneExact(token, lo) || OneExact(token, up);
            bool negated = token[1] == '!';
            string inner = token.Substring(negated ? 2 : 1, token.Length - (negated ? 3 : 2));
            bool inSet = InClass(inner, lo) || InClass(inner, up);
            return inSet != negated;
        }

        private static bool OneExact(string token, char c)
        {
            if (token == "?") return true;
            if (token[0] != '[') return token[0] == c;
            bool negated = token[1] == '!';
            string inner = token.Substring(negated ? 2 : 1, token.Length - (negated ? 3 : 2));
            return InClass(inner, c) != negated;
        }

        private static bool InClass(string inner, char c)
        {
            if (inner.Length == 3 && inner[1] == '-') return c >= inner[0] && c <= inner[2];
            return inner.IndexOf(c) >= 0;
        }

        private static string Show(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s) sb.Append(c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString());
            return sb.Append('"').ToString();
        }
    }
}
