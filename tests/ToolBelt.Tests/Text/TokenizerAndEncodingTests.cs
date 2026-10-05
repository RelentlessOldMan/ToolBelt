using System;
using System.Linq;
using System.Text;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    /// <summary>Tokenizer, percent-encoding and display-width alignment.</summary>
    public sealed class TokenizerAndEncodingTests
    {
        private static void Seq(string[] expected, string[] actual, string? message = null)
            => Check.Equal(string.Join("|", expected.Select(e => "[" + e + "]")), string.Join("|", actual.Select(a => "[" + a + "]")), message);

        // ---------- tokenizer ----------

        public void Whitespace_CollapsesAndQuotesGroup()
        {
            Seq(new[] { "a", "b c", "d" }, Tokenizer.Split("  a  \"b c\"\td  "));
            Seq(new[] { "abc de" }, Tokenizer.Split("ab\"c d\"e"));
            Seq(new[] { "say \"hi\"" }, Tokenizer.Split("\"say \"\"hi\"\"\""));
            Check.Equal(0, Tokenizer.Split("   ").Length);
        }

        public void EmptyQuotedTokenIsKept()
        {
            var t = Tokenizer.Tokenize("a \"\" b");
            Check.Equal(3, t.Count);
            Check.Equal("", t[1].Value);
            Check.True(t[1].WasQuoted);
            Check.False(t[0].WasQuoted);
        }

        public void SpansPointAtTheSource()
        {
            const string text = "run  \"my file\" -v";
            var t = Tokenizer.Tokenize(text);
            Check.Equal("\"my file\"", text.Substring(t[1].Start, t[1].Length));
            Check.Equal("-v", text.Substring(t[2].Start, t[2].Length));
        }

        public void Csv_KeepsEmptyFields()
        {
            var csv = TokenizerOptions.Csv();
            Seq(new[] { "a", "", "c", "" }, Tokenizer.Split("a,,c,", csv));
            Seq(new[] { "" }, Tokenizer.Split("", csv));
            Seq(new[] { "", "" }, Tokenizer.Split(",", csv));
            Seq(new[] { "x,y", "he said \"no\"" }, Tokenizer.Split("\"x,y\",\"he said \"\"no\"\"\"", csv));
            Seq(new[] { "a", "b" }, Tokenizer.Split("a;b", TokenizerOptions.Csv(';')));
        }

        public void Csv_MatchesCsvLineOnRandomRecords()
        {
            var rng = new DeterministicRandom(1);
            const string alphabet = "ab ,\"x";
            for (int trial = 0; trial < 500; trial++)
            {
                var fields = Enumerable.Range(0, rng.Next(1, 6))
                    .Select(_ => new string(Enumerable.Range(0, rng.Next(0, 5)).Select(__ => alphabet[rng.Next(alphabet.Length)]).ToArray()))
                    .ToArray();
                string line = ToolBelt.IO.CsvLine.Format(fields);
                Seq(fields, Tokenizer.Split(line, TokenizerOptions.Csv()), line);
            }
        }

        public void Shell_EscapesAndSingleQuotes()
        {
            var sh = TokenizerOptions.Shell();
            Seq(new[] { "a b", "it's", "c\"d" }, Tokenizer.Split(@"a\ b ""it's"" c\""d", sh));
            Seq(new[] { "x y" }, Tokenizer.Split("'x y'", sh));
        }

        public void TrimUnquoted()
        {
            var o = new TokenizerOptions { Delimiters = new[] { '|' }, CollapseDelimiters = false, TrimUnquoted = true };
            Seq(new[] { "a", "b c", "  kept  ", "" }, Tokenizer.Split(" a | b c |\"  kept  \"|  ", o));
        }

        public void MalformedInputNamesThePosition()
        {
            var ex = Check.Throws<FormatException>(() => Tokenizer.Split("ok \"open"));
            Check.True(ex.Message.Contains("position 3"), ex.Message);
            Check.Throws<FormatException>(() => Tokenizer.Split("trail\\", TokenizerOptions.Shell()));
        }

        // ---------- percent-encoding ----------

        public void Encode_UnreservedSafeAndUtf8()
        {
            Check.Equal("a-b_c.d~e", PercentEncoding.Encode("a-b_c.d~e"));
            Check.Equal("a%20b%2Fc", PercentEncoding.Encode("a b/c"));
            Check.Equal("a%20b/c", PercentEncoding.Encode("a b/c", safe: "/"));
            Check.Equal("caf%C3%A9%20%E2%82%AC%F0%9F%98%80", PercentEncoding.Encode("café €😀"));
            Check.Equal("a+b%2Bc", PercentEncoding.EncodeForm("a b+c"));
        }

        public void Decode_RoundTripsAndMatchesTheBcl()
        {
            var rng = new DeterministicRandom(2);
            for (int trial = 0; trial < 300; trial++)
            {
                var sb = new StringBuilder();
                for (int i = rng.Next(0, 20); i > 0; i--)
                {
                    int pick = rng.Next(4);
                    sb.Append(pick == 0 ? (char)rng.Next(0x20, 0x7F) : pick == 1 ? (char)rng.Next(0xA0, 0x2000) : pick == 2 ? ' ' : '%');
                }
                string s = sb.ToString();
                string enc = PercentEncoding.Encode(s);
                Check.Equal(Uri.EscapeDataString(s), enc, s);
                Check.Equal(s, PercentEncoding.Decode(enc));
                Check.Equal(s, PercentEncoding.DecodeForm(PercentEncoding.EncodeForm(s)));
            }
            Check.Equal("ä", PercentEncoding.Decode("%c3%a4"));
            Check.Equal("a+b", PercentEncoding.Decode("a+b"));
            Check.Equal("é", PercentEncoding.Decode("é"));                          // raw non-ASCII passes through
        }

        public void Decode_IsStrict()
        {
            foreach (string bad in new[] { "%", "%4", "abc%zz", "%C3", "%FF%FE" })
            {
                Check.Throws<FormatException>(() => PercentEncoding.Decode(bad), bad);
                Check.False(PercentEncoding.TryDecode(bad, out string? r), bad);
                Check.Null(r);
            }
            Check.True(PercentEncoding.TryDecode("%41", out string? ok));
            Check.Equal("A", ok);
        }

        // ---------- alignment ----------

        public void DisplayWidth_IgnoresEscapesAndCountsWideChars()
        {
            Check.Equal(5, TextAlign.DisplayWidth("hello"));
            Check.Equal(5, TextAlign.DisplayWidth("\u001b[31mhello\u001b[0m"));
            Check.Equal(4, TextAlign.DisplayWidth("日本"));
            Check.Equal(2, TextAlign.DisplayWidth("😀"));
            Check.Equal(1, TextAlign.DisplayWidth("é"));                      // e + combining acute
            Check.Equal(4, TextAlign.DisplayWidth("\u001b]8;;http://x\u0007link\u001b]8;;\u0007"));   // OSC 8 hyperlink
        }

        public void PadAndCenter()
        {
            Check.Equal("  ab", TextAlign.PadLeft("ab", 4));
            Check.Equal("ab..", TextAlign.PadRight("ab", 4, '.'));
            Check.Equal(" ab  ", TextAlign.Center("ab", 5));
            Check.Equal("日本  ", TextAlign.PadRight("日本", 6));
            Check.Equal("\u001b[1mab\u001b[0m  ", TextAlign.PadRight("\u001b[1mab\u001b[0m", 4));
            Check.Equal("toolong", TextAlign.PadLeft("toolong", 3));                 // pad never truncates
        }

        public void Fit_TruncatesSafely()
        {
            Check.Equal("hell…", TextAlign.Fit("hello world", 5));
            Check.Equal(5, TextAlign.DisplayWidth(TextAlign.Fit("日本語のテキスト", 5)));
            Check.Equal("日 …", TextAlign.Fit("日本語", 4));                         // a wide char can't straddle: padded
            string colored = TextAlign.Fit("\u001b[31mhello world\u001b[0m", 6);
            Check.Equal(6, TextAlign.DisplayWidth(colored));
            Check.True(colored.EndsWith("\u001b[0m", StringComparison.Ordinal), "colour is reset after a cut");
            Check.Equal("he", TextAlign.Fit("hello", 2, ellipsis: ""));
            Check.Equal("😀", TextAlign.Fit("😀😀", 2, ellipsis: ""));
            Check.Equal("ab   ", TextAlign.Fit("ab", 5));
        }
    }
}
