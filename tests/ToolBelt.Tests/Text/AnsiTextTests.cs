using System;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class AnsiTextTests
    {
        private static readonly string Esc = ((char)0x1B).ToString();
        private static readonly string Bel = ((char)0x07).ToString();

        public void StripsColorCodes()
        {
            string colored = Esc + "[31mred" + Esc + "[0m";
            Check.Equal("red", AnsiText.Strip(colored));
        }

        public void StripsMultipleAndCursorMoves()
        {
            string s = Esc + "[1;32mgreen" + Esc + "[0m and " + Esc + "[2Aup";
            Check.Equal("green and up", AnsiText.Strip(s));
        }

        public void StripsOscSequence()
        {
            // OSC set-title terminated by BEL, then visible text.
            string s = Esc + "]0;window title" + Bel + "visible";
            Check.Equal("visible", AnsiText.Strip(s));
        }

        public void PlainTextUnchanged()
        {
            Check.Equal("no escapes here", AnsiText.Strip("no escapes here"));
            Check.Equal("", AnsiText.Strip(""));
        }

        public void VisibleLength()
        {
            string colored = Esc + "[31mhello" + Esc + "[0m";
            Check.Equal(5, AnsiText.VisibleLength(colored));
            Check.Equal(5, AnsiText.VisibleLength("plain"));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => AnsiText.Strip(null!));
        }
    }
}
