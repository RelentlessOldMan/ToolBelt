using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class QueryStringTests
    {
        private static KeyValuePair<string, string> P(string k, string v) => new KeyValuePair<string, string>(k, v);

        public void Build_Basic()
        {
            Check.Equal("a=1&b=2", QueryString.Format(new[] { P("a", "1"), P("b", "2") }));
        }

        public void Build_Encodes()
        {
            Check.Equal("q=a%20b%26c", QueryString.Format(new[] { P("q", "a b&c") }));
        }

        public void Parse_Basic()
        {
            var parsed = QueryString.Parse("a=1&b=2");
            Check.True(parsed.SequenceEqual(new[] { P("a", "1"), P("b", "2") }));
        }

        public void Parse_ToleratesLeadingQuestionMark_AndDecodes()
        {
            var parsed = QueryString.Parse("?q=a%20b%26c");
            Check.Equal("a b&c", parsed.Single().Value);
        }

        public void Parse_MissingValue()
        {
            var parsed = QueryString.Parse("flag&x=1");
            Check.Equal("", parsed[0].Value);
            Check.Equal("flag", parsed[0].Key);
        }

        public void Parse_Empty()
        {
            Check.Equal(0, QueryString.Parse("").Count);
            Check.Equal(0, QueryString.Parse("?").Count);
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => QueryString.Format(null!));
            Check.Throws<ArgumentNullException>(() => QueryString.Parse(null!));
        }

        // Property: Parse(Build(pairs)) == pairs, even with delimiters and reserved chars in the data.
        public void RoundTrip_OverRandomPairs()
        {
            var rng = new Random(1234);
            const string palette = "ab=&% ?/#+";
            for (int trial = 0; trial < 2000; trial++)
            {
                int count = rng.Next(1, 6);
                var pairs = new List<KeyValuePair<string, string>>();
                for (int i = 0; i < count; i++)
                    pairs.Add(P(RandomString(rng, palette, 1, 6), RandomString(rng, palette, 0, 6)));

                string built = QueryString.Format(pairs);
                var parsed = QueryString.Parse(built);
                Check.True(pairs.SequenceEqual(parsed),
                    $"trial {trial}: '{built}'");
            }
        }

        private static string RandomString(Random rng, string palette, int minLen, int maxLen)
        {
            int len = rng.Next(minLen, maxLen + 1);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(palette[rng.Next(palette.Length)]);
            return sb.ToString();
        }
    }
}
