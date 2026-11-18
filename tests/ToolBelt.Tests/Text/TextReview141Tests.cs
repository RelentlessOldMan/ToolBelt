using System;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Round 141 review regressions (Text & small types, eighth pass): each failed on the code before its fix (the casing
    // one on .NET Framework, whose invariant casing lacked these mappings).
    public sealed class TextReview141Tests
    {
        // ---- CaseConverter: mappings .NET Framework's invariant casing lacks ----

        public void CaseConverter_CasesLettersNetFrameworkLeftAlone()
        {
            Check.Equal("ΛΌΓΟΣ", CaseConverter.ToConstantCase("λόγος"));       // "ΛΌΓΟς" on .NET Framework
            Check.Equal("SOME", CaseConverter.ToConstantCase("ſome"));     // long s
            Check.Equal("ΜM", CaseConverter.ToConstantCase("µm"));         // micro sign → Greek capital mu
            Check.Equal("kelvin", CaseConverter.ToSnakeCase("Kelvin"));    // Kelvin sign
            Check.Equal("ω_x", CaseConverter.ToSnakeCase("Ω_x"));           // Ohm sign
            Check.Equal("ᎠᎡ", CaseConverter.ToConstantCase("ꭰꭱ"));   // Cherokee
            Check.Equal("ꭰꭱ", CaseConverter.ToSnakeCase("ᎠᎡ"));
            // Stable once through: a style applied twice gives the same text.
            foreach (string s in new[] { "λόγος", "ſome", "Kelvin", "ꭰꭱ" })
            {
                string c = CaseConverter.ToConstantCase(s);
                Check.Equal(c, CaseConverter.ToConstantCase(c), s);
            }
        }

        // ---- TextAlign: exactly width columns ----

        // A cut-short escape holding a line feed was dropped whole, gluing the character before it to a U+FE0F after.
        public void TextAlign_FitKeepsTheControlOfADroppedEscape()
        {
            string fit = TextAlign.Fit("❤\u001b[3\n️ab", 2);
            Check.Equal(2, TextAlign.DisplayWidth(fit), fit);
            Check.True(fit.IndexOf('\n') >= 0, "the line feed was lost");
            string right = TextAlign.Fit("❤\u001b[3\n️❤\U0001F600 ", 3, Alignment.Right);
            Check.Equal(3, TextAlign.DisplayWidth(right), right);
        }

        // A symbol pad before a U+FE0F that starts the text turned emoji-wide; so did the character before an ellipsis
        // starting with one.
        public void TextAlign_PaddingAndEllipsisBeforeASelectorStayOneColumn()
        {
            string left = TextAlign.PadLeft("️x", 4, '·');
            Check.Equal(4, TextAlign.DisplayWidth(left), left);
            string centre = TextAlign.Center("️x", 5, '·');
            Check.Equal(5, TextAlign.DisplayWidth(centre), centre);
            string cut = TextAlign.Fit("❤abc", 2, ellipsis: "️…");
            Check.Equal(2, TextAlign.DisplayWidth(cut), cut);
            // Guard: a space pad leaves the selector where it was.
            Check.Equal("  ️x", TextAlign.PadLeft("️x", 3));
        }

        // ---- WordWrap ----

        public void WordWrap_ANullNewlineIsRefused()
        {
            var ex = Check.Throws<ArgumentNullException>(() => WordWrap.WrapToString("aaa bbb ccc", 3, null!));
            Check.Equal("newline", ex.ParamName);
        }

        // ---- UlidGenerator ----

        private sealed class ThrowOnceRandom : Random
        {
            private bool _thrown;
            public override void NextBytes(byte[] buffer)
            {
                if (!_thrown)
                {
                    _thrown = true;
                    buffer[0] = 0x55;
                    throw new InvalidOperationException("transient");
                }
                for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xA0;
            }
        }

        // The timestamp moved on before the Random threw, so the rest of that millisecond counted up from stale bits.
        public void Ulid_ARandomThatThrowsLeavesNoStaleBits()
        {
            var at = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
            var gen = new UlidGenerator(() => at, new ThrowOnceRandom());
            Check.Throws<InvalidOperationException>(() => gen.NewUlid());
            string id = gen.NewUlid();
            // Fresh bits, as a generator whose Random never threw draws them; not 0x55 and zeros counted up by one.
            var steady = new ThrowOnceRandom();
            try { steady.NextBytes(new byte[1]); } catch (InvalidOperationException) { }
            Check.Equal(new UlidGenerator(() => at, steady).NewUlid(), id);
        }

        // ---- Tokenizer ----

        public void Tokenizer_RefusesOverlappingRoles()
        {
            var ex = Check.Throws<ArgumentException>(() => Tokenizer.Tokenize("a,b", new TokenizerOptions { Escape = ',', Delimiters = new[] { ',' } }));
            Check.Equal("options", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => Tokenizer.Tokenize("a b", new TokenizerOptions { Quotes = new[] { ' ' } }));
            Check.Equal("options", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => Tokenizer.Tokenize("a b", new TokenizerOptions { Escape = '"' }));
            Check.Equal("options", ex.ParamName);
            Check.Equal(2, Tokenizer.Tokenize("a\\ b c", TokenizerOptions.Shell()).Count);   // guard: the presets are consistent
        }
    }
}
