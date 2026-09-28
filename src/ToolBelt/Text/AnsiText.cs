// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Removes ANSI escape sequences from text and measures its visible width while ignoring them. Needed
    /// as soon as captured output includes a colored child process (which makes it unreadable and breaks
    /// naive string assertions) and by table/column layout so styled cells still align.
    /// </summary>
    public static class AnsiText
    {
        private const char Escape = (char)0x1B; // ESC
        private const char Bell = (char)0x07;   // BEL, terminates an OSC sequence

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
                    // CSI: parameters/intermediates until a final byte in 0x40-0x7E.
                    i++;
                    while (i < text.Length && !(text[i] >= '@' && text[i] <= '~')) i++;
                    if (i < text.Length) i++; // consume the final byte
                }
                else if (next == ']')
                {
                    // OSC: terminated by BEL or by ST (ESC \).
                    i++;
                    while (i < text.Length)
                    {
                        if (text[i] == Bell) { i++; break; }
                        if (text[i] == Escape && i + 1 < text.Length && text[i + 1] == '\\') { i += 2; break; }
                        i++;
                    }
                }
                else
                {
                    i++; // simple two-character escape (e.g. ESC c)
                }
            }
            return sb.ToString();
        }

        /// <summary>The number of visible characters, ignoring escape sequences.</summary>
        public static int VisibleLength(string text) => Strip(text).Length;
    }
}
