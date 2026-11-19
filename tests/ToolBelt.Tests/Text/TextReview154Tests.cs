using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Round 154 (text and small types, ninth pass): each failed on the code before its fix.
    public sealed class TextReview154Tests
    {
        // ---------- NGramSimilarity ----------

        // The characters of s, a surrogate pair as one (a lone surrogate as its own).
        private static List<string> Characters(string s)
        {
            var list = new List<string>();
            for (int i = 0; i < s.Length; i++)
            {
                if (i + 1 < s.Length && char.IsSurrogatePair(s[i], s[i + 1])) { list.Add(s.Substring(i, 2)); i++; }
                else list.Add(s[i].ToString());
            }
            return list;
        }

        private static List<string> Grams(string s, int n)
        {
            var c = Characters(s);
            var grams = new List<string>();
            for (int i = 0; i + n <= c.Count; i++) grams.Add(string.Join("\u0001", c.Skip(i).Take(n)));
            return grams;
        }

        private static double NaiveDice(string a, string b, int n)
        {
            List<string> ga = Grams(a, n), gb = Grams(b, n);
            if (ga.Count + gb.Count == 0) return a == b ? 1.0 : 0.0;
            if (ga.Count == 0 || gb.Count == 0) return 0.0;
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string g in ga) count[g] = count.TryGetValue(g, out int k) ? k + 1 : 1;
            int both = 0;
            foreach (string g in gb)
                if (count.TryGetValue(g, out int k) && k > 0) { count[g] = k - 1; both++; }
            return 2.0 * both / (ga.Count + gb.Count);
        }

        private static double NaiveJaccard(string a, string b, int n)
        {
            List<string> ga = Grams(a, n), gb = Grams(b, n);
            if (ga.Count + gb.Count == 0) return a == b ? 1.0 : 0.0;
            if (ga.Count == 0 || gb.Count == 0) return 0.0;
            var sa = new HashSet<string>(ga, StringComparer.Ordinal);
            var sb = new HashSet<string>(gb, StringComparer.Ordinal);
            int both = sa.Count(sb.Contains);
            return (double)both / (sa.Count + sb.Count - both);
        }

        // Grams were UTF-16 units, so two emoji from one surrogate block shared a "gram" (their high surrogate):
        // Jaccard("x😀", "x😁", 1) was 0.5, not 1/3. Graded against grams over characters, lone surrogates included.
        public void NGram_GramsAreTakenOverCodePoints()
        {
            Check.Close(1.0 / 3, NGramSimilarity.Jaccard("x\U0001F600", "x\U0001F601", 1), 1e-15);
            Check.Close(0.5, NGramSimilarity.Dice("x\U0001F600", "x\U0001F601", 1), 1e-15);
            Check.Equal(0.0, NGramSimilarity.Dice("\U0001F600", "\U0001F601", 2));
            Check.Equal(1.0, NGramSimilarity.Dice("\U0001F600", "\U0001F600", 2));
            string[] parts = { "a", "b", "\U0001F600", "\U0001F601", "\U00010000", "\uD83D", "\uDE00", "\uDE01", "￿", "\uD800" };
            var rng = new Random(15401);
            for (int trial = 0; trial < 20000; trial++)
            {
                string Make()
                {
                    var sb = new StringBuilder();
                    int len = rng.Next(0, 8);
                    for (int k = 0; k < len; k++) sb.Append(parts[rng.Next(parts.Length)]);
                    return sb.ToString();
                }
                string a = Make(), b = Make();
                int n = rng.Next(1, 4);
                Check.Close(NaiveDice(a, b, n), NGramSimilarity.Dice(a, b, n), 1e-12, $"Dice trial {trial}");
                Check.Close(NaiveJaccard(a, b, n), NGramSimilarity.Jaccard(a, b, n), 1e-12, $"Jaccard trial {trial}");
            }
        }

        // ---------- Indent.Dedent ----------

        // A whitespace-only line kept one \r at most, so "  \r\r" (two lines ended by lone CRs) came out "\r".
        public void Dedent_AWhitespaceOnlyLineKeepsEveryCarriageReturn()
        {
            Check.Equal("\r\r", Indent.Dedent("  \r\r"));
            Check.Equal("\r\r\na", Indent.Dedent(" \r \r\n   a"));
            Check.Equal("\r\n\r\na", Indent.Dedent("  \r\n\t\r\n  a"));
            // Guards: the line count (by \n) and the CR count are kept, and dedenting is unchanged otherwise.
            Check.Equal("a\n  b\n\nc", Indent.Dedent("  a\n    b\n   \n  c"));
            Check.Equal("x\r\n", Indent.Dedent("  x\r\n"));
        }

        // ---------- TextAlign.Fit ----------

        // The ellipsis was taken as it came: a tab measured no columns ("abc\t" for width 4), a lone surrogate made the
        // result invalid UTF-16, a leading combining mark joined the last character kept (the cut did not show).
        public void Fit_AnEllipsisThatBreaksTheWidthIsRefused()
        {
            foreach (string bad in new[] { "\t", "..\n", "\u0007", "\u0085", "\uD83D", "x\uDE00", "́", "⃝", "ः." })
                Check.Equal("ellipsis", Check.Throws<ArgumentException>(() => TextAlign.Fit("abcdefgh", 4, ellipsis: bad)).ParamName,
                    string.Join(" ", bad.Select(c => ((int)c).ToString("X4"))));
            // Guards: ordinary, empty, styled, emoji and selector-led ellipses still work; null means none.
            Check.Equal("abc…", TextAlign.Fit("abcdefgh", 4));
            Check.Equal("a...", TextAlign.Fit("abcdefgh", 4, ellipsis: "..."));
            Check.Equal("abcd", TextAlign.Fit("abcdefgh", 4, ellipsis: ""));
            Check.Equal("abcd", TextAlign.Fit("abcdefgh", 4, ellipsis: null!));
            Check.Equal("ab\U0001F600", TextAlign.Fit("abcdefgh", 4, ellipsis: "\U0001F600"));
            Check.Equal("abc\u001b[2m…\u001b[0m", TextAlign.Fit("abcdefgh", 4, ellipsis: "\u001b[2m…\u001b[0m"));
            Check.Equal(4, TextAlign.DisplayWidth(TextAlign.Fit("abcdefgh", 4, ellipsis: "️…")));
            Check.Equal("ab", TextAlign.Fit("abc", 2, ellipsis: "....."));
            // The check holds even when nothing is cut, so a bad ellipsis is found the first time.
            Check.Equal("ellipsis", Check.Throws<ArgumentException>(() => TextAlign.Fit("a", 4, ellipsis: "\t")).ParamName);
        }
    }
}
