using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class CaseConverterTests
    {
        public void Camel_Basic()
        {
            Check.Equal("fooBarBaz", CaseConverter.ToCamelCase("foo_bar_baz"));
        }

        public void Pascal_Basic()
        {
            Check.Equal("FooBarBaz", CaseConverter.ToPascalCase("foo bar baz"));
        }

        public void Snake_Basic()
        {
            Check.Equal("foo_bar_baz", CaseConverter.ToSnakeCase("FooBarBaz"));
        }

        public void Kebab_Basic()
        {
            Check.Equal("foo-bar-baz", CaseConverter.ToKebabCase("fooBarBaz"));
        }

        public void Constant_Basic()
        {
            Check.Equal("FOO_BAR", CaseConverter.ToConstantCase("fooBar"));
        }

        public void Title_Basic()
        {
            Check.Equal("Foo Bar Baz", CaseConverter.ToTitleCase("foo-bar-baz"));
        }

        public void Idempotent_PerStyle()
        {
            var s = CaseConverter.ToSnakeCase("getHTTPResponseCode");
            Check.Equal(s, CaseConverter.ToSnakeCase(s));

            var p = CaseConverter.ToPascalCase("some_mixed input");
            Check.Equal(p, CaseConverter.ToPascalCase(p));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => CaseConverter.ToSnakeCase(null!));
        }

        // Differential: generate a known word list, build every expected form by hand, and confirm the
        // converter maps one canonical form into the others. Words are plain lowercase letters so there
        // is no acronym/hump ambiguity in the reference.
        public void Differential_RoundTripsAcrossForms()
        {
            var rng = new Random(9871);
            for (int trial = 0; trial < 500; trial++)
            {
                int wordCount = rng.Next(1, 6);
                var words = new List<string>();
                for (int w = 0; w < wordCount; w++)
                {
                    // Length >= 2: single-letter words make Pascal/camel round-trips genuinely
                    // ambiguous ("A"+"L" -> "AL" is indistinguishable from the acronym "AL").
                    int len = rng.Next(2, 8);
                    var sb = new StringBuilder();
                    for (int k = 0; k < len; k++)
                        sb.Append((char)('a' + rng.Next(0, 26)));
                    words.Add(sb.ToString());
                }

                string snake = string.Join("_", words);
                string kebab = string.Join("-", words);
                string pascal = string.Concat(words.Select(Cap));
                string camel = words[0] + string.Concat(words.Skip(1).Select(Cap));
                string title = string.Join(" ", words.Select(Cap));
                string constant = string.Join("_", words.Select(x => x.ToUpperInvariant()));

                // Feed each form; expect the tokenizer to recover the same words and rejoin correctly.
                foreach (var input in new[] { snake, kebab, pascal, camel, title })
                {
                    Check.Equal(snake, CaseConverter.ToSnakeCase(input), $"trial {trial}: snake from '{input}'");
                    Check.Equal(kebab, CaseConverter.ToKebabCase(input), $"trial {trial}: kebab from '{input}'");
                    Check.Equal(pascal, CaseConverter.ToPascalCase(input), $"trial {trial}: pascal from '{input}'");
                    Check.Equal(camel, CaseConverter.ToCamelCase(input), $"trial {trial}: camel from '{input}'");
                    Check.Equal(title, CaseConverter.ToTitleCase(input), $"trial {trial}: title from '{input}'");
                    Check.Equal(constant, CaseConverter.ToConstantCase(input), $"trial {trial}: constant from '{input}'");
                }
            }
        }

        private static string Cap(string w) => char.ToUpperInvariant(w[0]) + w.Substring(1);
    }
}
