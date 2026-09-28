using System;
using System.Text;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class HexDumpTests
    {
        public void ExactSingleLine()
        {
            // "Hey" = 48 65 79; 3 filled slots + 13 empty slots (13*3 = 39 spaces) then the gutter.
            string expected = "00000000  48 65 79 " + new string(' ', 39) + "|Hey|";
            Check.Equal(expected, HexDump.Format(Encoding.ASCII.GetBytes("Hey")));
        }

        public void Empty_ReturnsEmpty()
        {
            Check.Equal("", HexDump.Format(Array.Empty<byte>()));
        }

        public void MultipleLines_AdvanceOffset()
        {
            var data = new byte[20];
            var lines = HexDump.Format(data).Split('\n');
            Check.Equal(2, lines.Length);
            Check.True(lines[0].StartsWith("00000000", StringComparison.Ordinal));
            Check.True(lines[1].StartsWith("00000010", StringComparison.Ordinal)); // offset 16
        }

        public void NonPrintable_RendersAsDot()
        {
            byte[] data = { 0x00, 0x41, 0x1F, 0x7E, 0x7F };
            string dump = HexDump.Format(data);
            int bar = dump.IndexOf('|');
            string gutter = dump.Substring(bar);
            Check.Equal("|.A.~.|", gutter); // 0x00->., A, 0x1F->., 0x7E->~, 0x7F->.
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => HexDump.Format(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => HexDump.Format(new byte[] { 1 }, 0));
        }

        public void CustomBytesPerLine()
        {
            var lines = HexDump.Format(new byte[10], bytesPerLine: 4).Split('\n');
            Check.Equal(3, lines.Length); // 4 + 4 + 2
        }
    }
}
