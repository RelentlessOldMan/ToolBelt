using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class CsvLineTests
    {
        public void SimpleFields()
        {
            Check.True(CsvLine.Parse("a,b,c").SequenceEqual(new[] { "a", "b", "c" }));
        }

        public void QuotedFieldWithDelimiter()
        {
            Check.True(CsvLine.Parse("a,\"b,c\",d").SequenceEqual(new[] { "a", "b,c", "d" }));
        }

        public void EscapedQuotes()
        {
            Check.True(CsvLine.Parse("\"he said \"\"hi\"\"\"").SequenceEqual(new[] { "he said \"hi\"" }));
        }

        public void EmptyFields()
        {
            Check.True(CsvLine.Parse("a,,c").SequenceEqual(new[] { "a", "", "c" }));
            Check.True(CsvLine.Parse("").SequenceEqual(new[] { "" }));
        }

        public void QuotedFieldWithNewline()
        {
            Check.True(CsvLine.Parse("\"line1\nline2\",x").SequenceEqual(new[] { "line1\nline2", "x" }));
        }

        public void Format_QuotesWhenNeeded()
        {
            Check.Equal("a,\"b,c\",d", CsvLine.Format(new[] { "a", "b,c", "d" }));
            Check.Equal("\"he \"\"q\"\"\"", CsvLine.Format(new[] { "he \"q\"" }));
            Check.Equal("plain", CsvLine.Format(new[] { "plain" }));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => CsvLine.Parse(null!));
            Check.Throws<ArgumentNullException>(() => CsvLine.Format(null!));
        }

        // Property: Parse(Format(fields)) == fields, even with delimiters, quotes, and newlines inside.
        public void RoundTrip_OverRandomFields()
        {
            var rng = new Random(1010);
            const string palette = "ab,\"\n ";
            for (int trial = 0; trial < 2000; trial++)
            {
                int fieldCount = rng.Next(1, 6);
                var fields = new List<string>();
                for (int f = 0; f < fieldCount; f++)
                {
                    int len = rng.Next(0, 6);
                    var sb = new StringBuilder(len);
                    for (int k = 0; k < len; k++)
                        sb.Append(palette[rng.Next(palette.Length)]);
                    fields.Add(sb.ToString());
                }

                string formatted = CsvLine.Format(fields);
                var parsed = CsvLine.Parse(formatted);
                Check.True(fields.SequenceEqual(parsed),
                    $"trial {trial}: [{string.Join("|", fields)}] -> '{formatted}' -> [{string.Join("|", parsed)}]");
            }
        }
    }
}
