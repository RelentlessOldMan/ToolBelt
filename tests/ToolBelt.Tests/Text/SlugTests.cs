using System;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class SlugTests
    {
        public void Basic()
        {
            Check.Equal("hello-world", Slug.Slugify("Hello, World!"));
        }

        public void CollapsesAndTrimsSeparators()
        {
            Check.Equal("multiple-spaces", Slug.Slugify("  Multiple   spaces "));
            Check.Equal("a-b-c", Slug.Slugify("---a__b  c---"));
        }

        public void FoldsDiacritics()
        {
            Check.Equal("cafe-munchen", Slug.Slugify("Café Münchën"));
            Check.Equal("aeioun", Slug.Slugify("áéíóúñ"));
        }

        public void AlreadyASlug_IsUnchanged()
        {
            Check.Equal("already-a-slug", Slug.Slugify("already-a-slug"));
        }

        public void CustomSeparator()
        {
            Check.Equal("hello_world", Slug.Slugify("Hello World", separator: '_'));
        }

        public void MaxLength_TruncatesWithoutTrailingSeparator()
        {
            // 10-char cap lands mid-"world"; must not leave a dangling separator.
            Check.Equal("hello-worl", Slug.Slugify("Hello World Foo", maxLength: 10));
            // Cap that would land on a separator boundary still yields no trailing separator.
            Check.Equal("hello", Slug.Slugify("Hello World", maxLength: 5));
        }

        public void EmptyOrPunctuationOnly_ReturnsEmpty()
        {
            Check.Equal("", Slug.Slugify(""));
            Check.Equal("", Slug.Slugify("!!!  ---  ???"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => Slug.Slugify(null!));
        }

        // Property: output is either empty or a canonical slug, and slugifying is idempotent.
        public void Properties_OverRandomStrings()
        {
            var canonical = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
            var rng = new Random(24680);
            const string palette = "AaBb  Zz09-_,.!éü/中\t";

            for (int trial = 0; trial < 1000; trial++)
            {
                int len = rng.Next(0, 30);
                var sb = new StringBuilder(len);
                for (int i = 0; i < len; i++)
                    sb.Append(palette[rng.Next(palette.Length)]);
                string input = sb.ToString();

                string slug = Slug.Slugify(input);

                Check.True(slug.Length == 0 || canonical.IsMatch(slug),
                    $"trial {trial}: '{input}' -> '{slug}' is not canonical");
                Check.Equal(slug, Slug.Slugify(slug), $"trial {trial}: idempotence for '{slug}'");
            }
        }
    }
}
