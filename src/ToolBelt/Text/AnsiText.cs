// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Removes ANSI escape sequences from text and measures its visible width while ignoring them. Needed
    /// as soon as captured output includes a colored child process (which makes it unreadable and breaks
    /// naive string assertions) and by table/column layout so styled cells still align. Sequences are read as a VT
    /// terminal reads them: a control character inside one still acts and is kept ("ESC [ 3 LF m" leaves the line feed),
    /// and CAN, SUB, another ESC or a non-ASCII character ends one early.
    /// </summary>
    public static class AnsiText
    {
        private const char Escape = (char)0x1B; // ESC
        private const char Bell = (char)0x07;   // BEL, terminates an OSC sequence

        // ESC, CAN and SUB abort a sequence, as does anything past DEL (it is text).
        private static bool Aborts(char c) => c == Escape || c == (char)0x18 || c == (char)0x1A || c > (char)0x7F;

        /// <summary>Returns the text with all ANSI escape sequences removed.</summary>
        public static string Strip(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (text.IndexOf(Escape) < 0) return text; // fast path: nothing to strip

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c != Escape) { sb.Append(c); i++; continue; }

                i++; // consume ESC
                if (i >= text.Length) break;

                char next = text[i];
                if (next == '[')
                {
                    // CSI: parameters/intermediates until a final byte in 0x40-0x7E. A control character inside was taken
                    // as a parameter, so "a ESC [3 LF b" lost the line feed; it still acts in a terminal, so it is kept.
                    i++;
                    while (i < text.Length && !Aborts(text[i]))
                    {
                        char ch = text[i++];
                        if (ch >= '@' && ch <= '~') break;           // the final byte
                        if (ch < ' ') sb.Append(ch);
                    }
                }
                else if (next == ']' || next == 'P' || next == 'X' || next == '^' || next == '_')
                {
                    // OSC, and the DCS/SOS/PM/APC strings (whose payload leaked into the text): terminated by BEL or by
                    // ST (ESC \).
                    i++;
                    while (i < text.Length)
                    {
                        if (text[i] == Bell) { i++; break; }
                        if (text[i] == (char)0x18 || text[i] == (char)0x1A) break;   // CAN and SUB abort it
                        if (text[i] == Escape)                              // ST ends it; any other ESC cancels it and starts anew
                        {                                                   // (it swallowed the rest of the text)
                            if (i + 1 < text.Length && text[i + 1] == '\\') i += 2;
                            break;
                        }
                        i++;
                    }
                }
                else
                {
                    // Other escapes: optional intermediate bytes (0x20-0x2F, e.g. the '(' of ESC ( B), then one final byte.
                    // "ESC ESC [31m" ate the second ESC as a final byte.
                    while (i < text.Length && !Aborts(text[i]))
                    {
                        char ch = text[i++];
                        if (ch >= '0' && ch <= '~') break;           // the final byte
                        if (ch < ' ') sb.Append(ch);
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The number of UTF-16 code units left once escape sequences are removed (the length of <see cref="Strip"/>): a
        /// surrogate pair counts two and a combining mark one. For terminal columns use <c>TextAlign.DisplayWidth</c>.
        /// </summary>
        public static int VisibleLength(string text) => Strip(text).Length;
    }
}
