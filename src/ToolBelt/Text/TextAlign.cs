// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>Where <see cref="TextAlign.Fit"/> places text in its field.</summary>
    public enum Alignment
    {
        Left,
        Right,
        Center,
    }

    /// <summary>
    /// Padding and centring by <em>display</em> width rather than <see cref="string.Length"/>: ANSI escape sequences take no
    /// columns, combining marks and zero-width characters take none, and East Asian wide characters and emoji take two —
    /// so columns of coloured or CJK text still line up in a terminal. <see cref="string.PadLeft(int)"/> gets all three wrong.
    /// </summary>
    public static class TextAlign
    {
        /// <summary>Terminal columns the text occupies.</summary>
        public static int DisplayWidth(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            int width = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\u001b') { i = SkipEscape(text, i); continue; }
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    width += SupplementaryWidth(text, i);
                    i++;
                    continue;
                }
                width += CharWidth(c);
            }
            return width;
        }

        /// <summary>Pads on the left to <paramref name="width"/> display columns (text already that wide is returned unchanged).</summary>
        public static string PadLeft(string text, int width, char pad = ' ') => Fit(text, width, Alignment.Right, pad, truncate: false);

        /// <summary>Pads on the right to <paramref name="width"/> display columns.</summary>
        public static string PadRight(string text, int width, char pad = ' ') => Fit(text, width, Alignment.Left, pad, truncate: false);

        /// <summary>Centres in <paramref name="width"/> display columns; an odd leftover column goes on the right.</summary>
        public static string Center(string text, int width, char pad = ' ') => Fit(text, width, Alignment.Center, pad, truncate: false);

        /// <summary>
        /// Exactly <paramref name="width"/> columns: padded per <paramref name="alignment"/>, and with <paramref name="truncate"/>
        /// cut (on a character boundary, never inside an escape sequence or surrogate pair) and ended with
        /// <paramref name="ellipsis"/> when too wide. A wide character that would straddle the edge is replaced by padding.
        /// </summary>
        public static string Fit(string text, int width, Alignment alignment = Alignment.Left, char pad = ' ', bool truncate = true, string ellipsis = "…")
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must not be negative.");
            // Padding counts characters, so a wide or zero-width pad broke "exactly width columns" ('中' gave 9 for 5).
            if (DisplayWidth(pad.ToString()) != 1) throw new ArgumentOutOfRangeException(nameof(pad), pad, "The pad character must be one column wide.");
            int current = DisplayWidth(text);
            if (current > width)
            {
                if (!truncate) return text;
                return Cut(text, width, ellipsis ?? "", pad);
            }
            int extra = width - current;
            switch (alignment)
            {
                case Alignment.Right: return new string(pad, extra) + text;
                case Alignment.Center: return new string(pad, extra / 2) + text + new string(pad, extra - extra / 2);
                default: return text + new string(pad, extra);
            }
        }

        private static string Cut(string text, int width, string ellipsis, char pad)
        {
            int ellipsisWidth = DisplayWidth(ellipsis);
            if (ellipsisWidth > width) { ellipsis = ""; ellipsisWidth = 0; }
            int budget = width - ellipsisWidth, used = 0;
            var sb = new StringBuilder();
            bool sawEscape = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\u001b')
                {
                    int end = SkipEscape(text, i);
                    sb.Append(text, i, end - i + 1);
                    sawEscape = true;
                    i = end;
                    continue;
                }
                int len = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                int w = DisplayWidth(text.Substring(i, len));
                if (used + w > budget) break;
                sb.Append(text, i, len);
                used += w;
                i += len - 1;
            }
            sb.Append(pad, budget - used).Append(ellipsis);
            if (sawEscape) sb.Append("\u001b[0m");                 // don't leak a colour past the cut
            return sb.ToString();
        }

        // Index of the last character of the escape sequence starting at `i` (CSI "ESC [ ... final", OSC "ESC ] ... BEL/ST",
        // or ESC + intermediate bytes 0x20-0x2F + one final char, e.g. "ESC ( B").
        private static int SkipEscape(string s, int i)
        {
            if (i + 1 >= s.Length) return i;
            char kind = s[i + 1];
            if (kind == '[')
            {
                int j = i + 2;
                while (j < s.Length && !(s[j] >= '@' && s[j] <= '~') && s[j] != '\u001b') j++;
                if (j < s.Length) return s[j] == '\u001b' ? j - 1 : j;   // an ESC cancels the sequence and starts a new one
                return Math.Min(j, s.Length - 1);
            }
            if (kind == ']')
            {
                for (int j = i + 2; j < s.Length; j++)
                {
                    if (s[j] == '\a') return j;
                    if (s[j] == '\u001b') return j + 1 < s.Length && s[j + 1] == '\\' ? j + 1 : j - 1;   // another ESC cancels it
                }
                return s.Length - 1;
            }
            int k = i + 1;
            while (k < s.Length && s[k] >= ' ' && s[k] <= '/') k++;
            if (k < s.Length && s[k] == '\u001b') return k - 1;         // "ESC ESC [31m" ate the second ESC as a final byte
            return Math.Min(k, s.Length - 1);
        }

        // Width of the surrogate pair at `index`: combining marks, format characters, tags and variation selectors take no
        // columns; emoji and the CJK supplementary planes take two; everything else one. (Multi-code-point emoji built with
        // ZWJ or skin-tone modifiers render as one glyph but count per code point — terminals disagree on those anyway.)
        private static int SupplementaryWidth(string text, int index)
        {
            int cp = char.ConvertToUtf32(text[index], text[index + 1]);
            var cat = CharUnicodeInfo.GetUnicodeCategory(text, index);
            if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark || cat == UnicodeCategory.Format) return 0;
            if (cp >= 0xE0000 && cp <= 0xE0FFF) return 0;                                   // tags, variation selectors supplement
            return (cp >= 0x1F000 && cp <= 0x1FAFF) || (cp >= 0x20000 && cp <= 0x3FFFD) ? 2 : 1;
        }

        private static int CharWidth(char c)
        {
            if (c < 0x20 || (c >= 0x7F && c < 0xA0)) return 0;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark || cat == UnicodeCategory.Format) return 0;
            if (c == '​') return 0;
            bool wide =
                (c >= 0x1100 && c <= 0x115F) ||                    // Hangul Jamo
                (c >= 0x2E80 && c <= 0x303E) ||                    // CJK radicals, punctuation
                (c >= 0x3041 && c <= 0x33FF) ||                    // kana, CJK compatibility
                (c >= 0x3400 && c <= 0x4DBF) ||                    // CJK extension A
                (c >= 0x4E00 && c <= 0x9FFF) ||                    // CJK unified
                (c >= 0xA000 && c <= 0xA4CF) ||                    // Yi
                (c >= 0xAC00 && c <= 0xD7A3) ||                    // Hangul syllables
                (c >= 0xF900 && c <= 0xFAFF) ||                    // CJK compatibility ideographs
                (c >= 0xFE30 && c <= 0xFE4F) ||                    // CJK compatibility forms
                (c >= 0xFF00 && c <= 0xFF60) ||                    // full-width forms
                (c >= 0xFFE0 && c <= 0xFFE6) ||
                IsWideBmpEmoji(c);
            return wide ? 2 : 1;
        }

        // BMP symbols with East Asian Width "W" (default emoji presentation), e.g. ⭐ U+2B50, ✅ U+2705, ⌚ U+231A.
        private static readonly char[] WideEmojiRanges =
        {
            '\u231A', '\u231B', '\u2329', '\u232A', '\u23E9', '\u23EC', '\u23F0', '\u23F0', '\u23F3', '\u23F3',
            '\u25FD', '\u25FE', '\u2614', '\u2615', '\u2648', '\u2653', '\u267F', '\u267F', '\u2693', '\u2693',
            '\u26A1', '\u26A1', '\u26AA', '\u26AB', '\u26BD', '\u26BE', '\u26C4', '\u26C5', '\u26CE', '\u26CE',
            '\u26D4', '\u26D4', '\u26EA', '\u26EA', '\u26F2', '\u26F3', '\u26F5', '\u26F5', '\u26FA', '\u26FA',
            '\u26FD', '\u26FD', '\u2705', '\u2705', '\u270A', '\u270B', '\u2728', '\u2728', '\u274C', '\u274C',
            '\u274E', '\u274E', '\u2753', '\u2755', '\u2757', '\u2757', '\u2795', '\u2797', '\u27B0', '\u27B0',
            '\u27BF', '\u27BF', '\u2B1B', '\u2B1C', '\u2B50', '\u2B50', '\u2B55', '\u2B55',
        };

        private static bool IsWideBmpEmoji(char c)
        {
            if (c < '\u231A' || c > '\u2B55') return false;
            for (int i = 0; i < WideEmojiRanges.Length; i += 2)
                if (c >= WideEmojiRanges[i] && c <= WideEmojiRanges[i + 1]) return true;
            return false;
        }
    }
}
