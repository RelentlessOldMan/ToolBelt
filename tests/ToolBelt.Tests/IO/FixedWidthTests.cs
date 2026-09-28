using System;
using System.Linq;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class FixedWidthTests
    {
        private static readonly int[] Widths = { 5, 5, 3 };

        public void Parse()
        {
            var fields = FixedWidth.Parse("Ada  30   x  ", Widths);
            Check.True(fields.SequenceEqual(new[] { "Ada", "30", "x" }));
        }

        public void ParseShortLine()
        {
            var fields = FixedWidth.Parse("Ada", Widths);
            Check.True(fields.SequenceEqual(new[] { "Ada", "", "" }));
        }

        public void ParseWithoutTrim()
        {
            var fields = FixedWidth.Parse("Ada  30   ", Widths, trim: false);
            Check.Equal("Ada  ", fields[0]);
            Check.Equal("30   ", fields[1]);
        }

        public void Format()
        {
            Check.Equal("Ada  30   x  ", FixedWidth.Format(new[] { "Ada", "30", "x" }, Widths));
            Check.Equal("  Ada   30  x", FixedWidth.Format(new[] { "Ada", "30", "x" }, Widths, leftAlign: false));
        }

        public void FormatTruncatesOverflow()
        {
            Check.Equal("Adama", FixedWidth.Format(new[] { "Adamant" }, new[] { 5 }));
        }

        public void RoundTrip()
        {
            var original = new[] { "abc", "42", "z" };
            string line = FixedWidth.Format(original, Widths);
            var parsed = FixedWidth.Parse(line, Widths);
            Check.True(parsed.SequenceEqual(original));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => FixedWidth.Parse("x", new[] { 0 }));
            Check.Throws<ArgumentException>(() => FixedWidth.Format(new[] { "a" }, Widths)); // count mismatch
            Check.Throws<ArgumentNullException>(() => FixedWidth.Parse(null!, Widths));
        }
    }
}
