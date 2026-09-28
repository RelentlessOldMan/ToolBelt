using System;
using System.IO;
using System.Linq;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class LineReaderTests
    {
        private static TextLine[] Read(string content, Encoding? enc = null)
        {
            byte[] bytes = (enc ?? Encoding.UTF8).GetBytes(content);
            using var ms = new MemoryStream(bytes);
            return LineReader.ReadLines(ms, enc).ToArray();
        }

        public void MixedLineEndings()
        {
            // a\n  b\r\n  c\r  d(no newline)
            var lines = Read("a\nb\r\nc\rd");
            Check.Equal(4, lines.Length);
            Check.Equal("a", lines[0].Text);
            Check.Equal("b", lines[1].Text);
            Check.Equal("c", lines[2].Text);
            Check.Equal("d", lines[3].Text);
        }

        public void ByteOffsets()
        {
            var lines = Read("a\nb\r\nc\rd");
            Check.Equal(0L, lines[0].Offset); // a
            Check.Equal(2L, lines[1].Offset); // b (after "a\n")
            Check.Equal(5L, lines[2].Offset); // c (after "b\r\n")
            Check.Equal(7L, lines[3].Offset); // d (after "c\r")
        }

        public void EmptyLinesPreserved()
        {
            var lines = Read("a\n\nb");
            Check.Equal(3, lines.Length);
            Check.Equal("a", lines[0].Text);
            Check.Equal("", lines[1].Text);
            Check.Equal("b", lines[2].Text);
        }

        public void TrailingNewlineNoExtraLine()
        {
            var lines = Read("a\nb\n");
            Check.Equal(2, lines.Length);
        }

        public void EmptyStream()
        {
            Check.Equal(0, Read("").Length);
        }

        public void MultiByteOffsets()
        {
            // 'é' is two UTF-8 bytes, so the next line's offset accounts for both.
            var lines = Read("é\nx");
            Check.Equal("é", lines[0].Text);
            Check.Equal(0L, lines[0].Offset);
            Check.Equal("x", lines[1].Text);
            Check.Equal(3L, lines[1].Offset); // 2 bytes for é + 1 for '\n'
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => LineReader.ReadLines(null!).ToArray());
        }
    }
}
