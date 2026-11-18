using System;
using System.Linq;
using ToolBelt.Identifiers;
using ToolBelt.Tests.Framework;
using ToolBelt.Text;

namespace ToolBelt.Tests.Text
{
    // Round 123 review regressions (Text & small types, seventh pass): each failed on the code before its fix.
    public sealed class TextReview123Tests
    {
        // ---- CaseConverter: lower-case letters with no one-letter capital ----

        public void UpperStyles_SpellOutLettersWithNoOneLetterCapital()
        {
            Check.Equal("FILE_NAME", CaseConverter.ToConstantCase("ﬁle_name"));        // "ﬁLE_NAME", then "ﬁ_LE_NAME"
            Check.Equal("FFX", CaseConverter.ToConstantCase("ﬀx"));
            Check.Equal("Ϊ́Α", CaseConverter.ToConstantCase("ΐα"));
            Check.Equal("ʼNA", CaseConverter.ToConstantCase("ŉa"));
            Check.Equal("File", CaseConverter.ToPascalCase("ﬁle"));
            Check.Equal("ʼNa", CaseConverter.ToTitleCase("ŉa"));
            Check.Equal("ʼNa", CaseConverter.ToTitleCase("ʼNa"));
            Check.Equal(100001, CaseConverter.ToTitleCase(new string('ʼ', 100000) + "n").Length);
            Check.Equal("STRASSE", CaseConverter.ToConstantCase("straße"));
            Check.Equal(1, CaseConverter.ToWords("STRAßE").Count);

            // Every style is idempotent, and those that keep lower case read back the same words as the input.
            var rng = new Random(123);
            string[] pieces = { "a", "b", "B", "Z", "1", "ß", "ﬀ", "ﬁ", "ŉ", "ʼ", "ǰ", "ΐ", "α",
                                "և", "ᾳ", "ᾶ", "ĸ", "中", "ſ", "ǆ", "İ", "ı", "_", "-", " " };
            var styles = new Func<string, string>[] { CaseConverter.ToSnakeCase, CaseConverter.ToKebabCase, CaseConverter.ToConstantCase, CaseConverter.ToTitleCase };
            int failures = 0;
            string first = "";
            for (int trial = 0; trial < 20000; trial++)
            {
                string input = string.Concat(Enumerable.Range(0, rng.Next(1, 8)).Select(_ => pieces[rng.Next(pieces.Length)]));
                foreach (var style in styles)
                {
                    string once = style(input);
                    if (style(once) != once) { if (failures++ == 0) first = $"{style.Method.Name}('{input}') = '{once}' -> '{style(once)}'"; }
                }
            }
            Check.True(failures == 0, $"{failures} not idempotent, first {first}");
        }

        public void PascalAndCamel_KeepTheBoundaryAfterALetterWithNoOneLetterCapital()
        {
            Check.Equal("BßX", CaseConverter.ToPascalCase("bß_x"));
            Check.Equal("BßX", CaseConverter.ToPascalCase("BßX"));                   // "Bßx"
            Check.Equal("AﬀB", CaseConverter.ToPascalCase("aﬀ_b"));
            Check.Equal("AﬀB", CaseConverter.ToPascalCase("AﬀB"));                   // "Aﬀb"
            Check.Equal("bß_x", CaseConverter.ToSnakeCase("BßX"));
            string[] words = { "bß", "aﬀ", "ﬁle", "straße", "ab", "αΐ", "xŉ" };
            foreach (string w1 in words)
                foreach (string w2 in words)
                {
                    string snake = w1 + "_" + w2;
                    foreach (Func<string, string> style in new Func<string, string>[] { CaseConverter.ToPascalCase, CaseConverter.ToCamelCase })
                    {
                        string once = style(snake);
                        Check.Equal(once, style(once), $"{style.Method.Name}('{snake}')");
                        Check.Equal(2, CaseConverter.ToWords(once).Count, $"{style.Method.Name}('{snake}') = '{once}'");
                    }
                }
        }

        // ---- NanoId ----

        public void NanoId_DefaultSizePastTheLongestString_IsRejectedByTheConstructor()
        {
            var e = Check.Throws<ArgumentOutOfRangeException>(() => new NanoId(new Random(1), null, int.MaxValue));
            Check.Equal("defaultSize", e.ParamName);
            e = Check.Throws<ArgumentOutOfRangeException>(() => new NanoId(new Random(1), null, 0x3FFFFFE0));
            Check.Equal("defaultSize", e.ParamName);
            Check.Equal(5, new NanoId(new Random(1), null, 5).New().Length);
            new NanoId(new Random(1), null, 0x3FFFFFDF);                                          // the longest string is allowed
        }

        // ---- TextAlign: the emoji selector past an escape that keeps a control character ----

        public void DisplayWidth_SelectorPastAControlInsideAnEscape_DoesNotWiden()
        {
            foreach (string s in new[] { "❤\u001b[3\nm️", "❤\u001b[\t\u001b[0m️", "❤\u001b(\nB️" })
            {
                Check.Equal(1, TextAlign.DisplayWidth(s), s);
                Check.Equal(TextAlign.DisplayWidth(AnsiText.Strip(s)), TextAlign.DisplayWidth(s), s);
            }
            Check.Equal(2, TextAlign.DisplayWidth("❤\u001b[0m️"));                    // a plain escape still passes
            Check.Equal(2, TextAlign.DisplayWidth("❤\u001b]0;t\a️"));                   // so does an OSC string

            // Measuring the text and its stripped form agree.
            var rng = new Random(1230);
            string[] pieces = { "❤", "️", "\u001b[", "\u001b(", "\u001b]", "\u001b", "\n", "\t", "\a", "3", "m", "B", "0", ";", "\\", "a", "中" };
            for (int trial = 0; trial < 50000; trial++)
            {
                string s = string.Concat(Enumerable.Range(0, rng.Next(1, 9)).Select(_ => pieces[rng.Next(pieces.Length)]));
                int direct = TextAlign.DisplayWidth(s), stripped = TextAlign.DisplayWidth(AnsiText.Strip(s));
                Check.True(direct == stripped, $"'{Escape(s)}': {direct} vs stripped {stripped}");
            }
        }

        private static string Escape(string s) => string.Concat(s.Select(c => c < ' ' || c > '~' ? $"\\u{(int)c:X4}" : c.ToString()));
    }
}
