using System.Linq;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class CaseConverterHardeningTests
    {
        public void Acronym_BoundaryIsDetected()
        {
            Check.Equal("http_server", CaseConverter.ToSnakeCase("HTTPServer"));
            Check.Equal("get_http_response_code", CaseConverter.ToSnakeCase("getHTTPResponseCode"));
        }

        public void Digits_StayWithPrecedingLetters()
        {
            Check.Equal("utf8_encoder", CaseConverter.ToSnakeCase("utf8Encoder"));
        }

        public void LeadingTrailingAndRepeatedSeparators_Collapse()
        {
            Check.Equal("foo_bar", CaseConverter.ToSnakeCase("__foo__bar__"));
        }

        public void MixedSeparators_AllNormalize()
        {
            Check.Equal("foo_bar_baz_qux", CaseConverter.ToSnakeCase("foo-bar_baz qux"));
        }

        public void Empty_ReturnsEmpty()
        {
            Check.Equal("", CaseConverter.ToSnakeCase(""));
            Check.Equal("", CaseConverter.ToPascalCase("   "));
        }

        public void SingleWord()
        {
            Check.Equal("Hello", CaseConverter.ToPascalCase("hello"));
            Check.Equal("hello", CaseConverter.ToCamelCase("HELLO"));
        }

        public void TrailingUpperRun_ReadsAsAcronym_ByDesign()
        {
            // Inherent to casing: a trailing run of capitals cannot be distinguished from an acronym,
            // so "AL" is one token, not "a" + "l". Documented so it is not mistaken for a bug.
            Check.Equal("al", CaseConverter.ToSnakeCase("AL"));
            Check.Equal("foo_al", CaseConverter.ToSnakeCase("FooAL"));
        }

        public void ToWords_SplitsAcronymRun()
        {
            var words = CaseConverter.ToWords("parseJSONFromURL").ToArray();
            Check.True(words.SequenceEqual(new[] { "parse", "JSON", "From", "URL" }),
                "expected [parse, JSON, From, URL], got [" + string.Join(", ", words) + "]");
        }
    }
}
