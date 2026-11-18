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
    /// Zero-width characters and supplementary symbols are looked up in fixed Unicode 15 tables, so .NET Framework (whose
    /// tables are Unicode 8) and .NET 8 measure them the same.
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
                if (text[i] == '\u001b') { i = SkipEscape(text, i); continue; }
                width += WidthAt(text, i, out int len);
                i += len - 1;
            }
            return width;
        }

        // Columns of the character (or surrogate pair, `len` 2) at `i`. U+FE0F after it, even past escape sequences
        // ("❤" ESC[0m U+FE0F measured 1, and 2 once the escape was stripped), asks for the emoji presentation. Not past a
        // control character inside one: it still acts, and AnsiText.Strip keeps it, so "❤" ESC[3 LF m U+FE0F measured 2
        // and its stripped text 1.
        private static int WidthAt(string text, int i, out int len)
        {
            char c = text[i];
            len = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            int next = i + len;
            bool selector = false;
            while (next < text.Length && text[next] == '\u001b')
            {
                int end = SkipEscape(text, next);
                if (HoldsControl(text, next, end)) { next = -1; break; }
                next = end + 1;
            }
            if (next >= 0) selector = next < text.Length && text[next] == '\uFE0F';
            if (len == 1) return CharWidth(c, selector);
            int w = SupplementaryWidth(text, i);
            // The selector widened BMP symbols only: 🌡️ and 🕉️ measured 1 beside ❤️'s 2. The emoji blocks
            // U+1F000–1FAFF are fixed, so .NET Framework measures them the same.
            if (w == 1 && selector)
            {
                int cp = char.ConvertToUtf32(c, text[i + 1]);
                if (cp >= 0x1F000 && cp <= 0x1FAFF) w = 2;
            }
            return w;
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
        /// An unfinished escape sequence at the end of the text is dropped when padding or an ellipsis goes after it.
        /// </summary>
        public static string Fit(string text, int width, Alignment alignment = Alignment.Left, char pad = ' ', bool truncate = true, string ellipsis = "…")
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must not be negative.");
            // Padding counts characters, so a wide or zero-width pad broke "exactly width columns" ('中' gave 9 for 5).
            // A lone surrogate measured one column and padded the text with invalid UTF-16.
            if (char.IsSurrogate(pad) || DisplayWidth(pad.ToString()) != 1) throw new ArgumentOutOfRangeException(nameof(pad), pad, "The pad character must be one column wide.");
            int current = DisplayWidth(text);
            if (current > width)
            {
                if (!truncate) return text;
                return Cut(DropUnfinishedEscape(text), width, ellipsis ?? "", pad);
            }
            int extra = width - current;
            // A width past the longest string .NET can hold threw OutOfMemoryException after trying to build it.
            if ((long)text.Length + extra > MaxStringLength)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width would make the result longer than a string can be.");
            // An unfinished escape at the end swallows whatever follows it, the padding included ("ab" + ESC + 4 spaces
            // measured 2 columns, not 6): it is dropped when something goes after it.
            if (extra > 0 && alignment != Alignment.Right) text = DropUnfinishedEscape(text);
            switch (alignment)
            {
                case Alignment.Right: return new string(pad, extra) + text;
                case Alignment.Center: return new string(pad, extra / 2) + text + new string(pad, extra - extra / 2);
                default: return text + new string(pad, extra);
            }
        }

        private const int MaxStringLength = 0x3FFFFFDF;

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
                    int end = SkipEscape(text, i, out _, out bool aborted);
                    // A sequence cut short by the next character is left out: kept, and with that character cut away, it
                    // swallowed the padding and ellipsis after it.
                    if (!aborted)
                    {
                        sb.Append(text, i, end - i + 1);
                        sawEscape = true;
                    }
                    i = end;
                    continue;
                }
                int w = WidthAt(text, i, out int len);
                if (i + len < text.Length && text[i + len] == '\uFE0F') len++;   // the emoji selector stays with its base
                if (used + w > budget) break;
                sb.Append(text, i, len);
                used += w;
                i += len - 1;
            }
            sb.Append(pad, budget - used).Append(ellipsis);
            if (sawEscape) sb.Append("\u001b[0m");                 // don't leak a colour past the cut
            return sb.ToString();
        }

        // The text without an escape sequence that runs unfinished to its end, nor the sequences cut short just before it:
        // "ab" ESC ESC lost its last ESC, and the first, then unfinished itself, swallowed the padding ("ab" ESC ESC padded
        // to 6 measured 2).
        private static string DropUnfinishedEscape(string text)
        {
            int abortedRun = -1;   // start of the sequences cut short that end where this one starts
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\u001b') { abortedRun = -1; continue; }
                int end = SkipEscape(text, i, out bool finished, out bool aborted);
                if (!finished) return text.Substring(0, abortedRun >= 0 ? abortedRun : i);
                if (!aborted) abortedRun = -1;
                else if (abortedRun < 0) abortedRun = i;
                i = end;
            }
            return text;
        }

        private static int SkipEscape(string s, int i) => SkipEscape(s, i, out _, out _);

        // Whether the escape sequence s[start..end] keeps a control character: a CSI or plain escape does (AnsiText.Strip
        // leaves it in the text); the OSC-style strings swallow theirs, and their BEL is the terminator.
        private static bool HoldsControl(string s, int start, int end)
        {
            if (start + 1 > end) return false;
            char kind = s[start + 1];
            if (kind == ']' || kind == 'P' || kind == 'X' || kind == '^' || kind == '_') return false;
            for (int j = start + 1; j <= end; j++)
                if (s[j] < ' ' && s[j] != '\u001b') return true;
            return false;
        }

        private static int SkipEscape(string s, int i, out bool finished) => SkipEscape(s, i, out finished, out _);

        // Index of the last character of the escape sequence starting at `i` (CSI "ESC [ ... final"; OSC "ESC ] ... BEL/ST"
        // and likewise the DCS, SOS, PM and APC strings ESC P, X, ^, _; or ESC + intermediate bytes 0x20-0x2F + one final
        // char, e.g. "ESC ( B"). `finished` is false when the sequence runs to the end of the text without its terminator;
        // `aborted` is true when the next character (ESC, CAN, SUB or a non-ASCII one) cut it short.
        private static int SkipEscape(string s, int i, out bool finished, out bool aborted)
        {
            finished = true;
            aborted = false;
            if (i + 1 >= s.Length) { finished = false; return i; }
            char kind = s[i + 1];
            if (kind == '[')
            {
                // A control character inside acts and takes no columns, so it stays in the sequence; ESC, CAN, SUB or a
                // non-ASCII character ends it early ("ESC [3 LF b" counted the 'b').
                int j = i + 2;
                while (j < s.Length && (s[j] < '@' || s[j] == '\u007f') && !Aborts(s[j])) j++;
                if (j < s.Length)
                {
                    if (s[j] >= '@' && s[j] <= '~') return j;
                    aborted = true;
                    return j - 1;
                }
                finished = false;
                return s.Length - 1;
            }
            if (kind == ']' || kind == 'P' || kind == 'X' || kind == '^' || kind == '_')
            {
                for (int j = i + 2; j < s.Length; j++)
                {
                    if (s[j] == '\a') return j;
                    if (s[j] == '\u0018' || s[j] == '\u001a') { aborted = true; return j - 1; }            // CAN and SUB abort it
                    if (s[j] == '\u001b')
                    {
                        if (j + 1 < s.Length && s[j + 1] == '\\') return j + 1;
                        aborted = true;                                                                 // another ESC cancels it
                        return j - 1;
                    }
                }
                finished = false;
                return s.Length - 1;
            }
            int k = i + 1;
            while (k < s.Length && (s[k] < '0' || s[k] == '\u007f') && !Aborts(s[k])) k++;
            if (k < s.Length)
            {
                if (s[k] <= '~' && !Aborts(s[k])) return k;
                aborted = true;   // "ESC ESC [31m" ate the second ESC as a final byte
                return k - 1;
            }
            finished = false;
            return s.Length - 1;
        }

        private static bool Aborts(char c) => c == '\u001b' || c == '\u0018' || c == '\u001a' || c > '\u007f';

        // Width of the surrogate pair at `index`: combining marks, format characters, tags and variation selectors take no
        // columns; emoji and the CJK supplementary planes take two; everything else one. (Multi-code-point emoji built with
        // ZWJ or skin-tone modifiers render as one glyph but count per code point — terminals disagree on those anyway.)
        private static int SupplementaryWidth(string text, int index)
        {
            int cp = char.ConvertToUtf32(text[index], text[index + 1]);
            if (InRanges(ZeroWidthRanges, cp)) return 0;
            if (cp >= 0xE0000 && cp <= 0xE0FFF) return 0;                                   // tags, variation selectors supplement
            // Tangut, Khitan, the kana supplements and Nushu are East Asian Wide too (they counted one).
            bool wide = InRanges(WideSymbolRanges, cp) || (cp >= 0x20000 && cp <= 0x3FFFD)
                || (cp >= 0x16FE0 && cp <= 0x16FE4) || (cp >= 0x16FF0 && cp <= 0x16FF1)
                || (cp >= 0x17000 && cp <= 0x18CD5) || (cp >= 0x18D00 && cp <= 0x18D08)
                || (cp >= 0x1AFF0 && cp <= 0x1AFFE) || (cp >= 0x1B000 && cp <= 0x1B2FB);
            return wide ? 2 : 1;
        }

        // `emojiSelector`: U+FE0F follows, asking for the emoji presentation, which terminals draw two columns wide
        // ("❤️" counted one).
        private static int CharWidth(char c, bool emojiSelector)
        {
            int w = CharWidth(c);
            if (w != 1 || !emojiSelector || c < '\u00A0') return w;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat == UnicodeCategory.OtherSymbol || cat == UnicodeCategory.MathSymbol || cat == UnicodeCategory.OtherPunctuation ? 2 : 1;
        }

        private static int CharWidth(char c)
        {
            if (c < 0x20 || (c >= 0x7F && c < 0xA0)) return 0;
            if (InRanges(ZeroWidthRanges, c)) return 0;
            if (c == '​') return 0;
            // Hangul medial vowels and final consonants join the leading consonant's two columns ("한" decomposed counted 4).
            if ((c >= 0x1160 && c <= 0x11FF) || (c >= 0xD7B0 && c <= 0xD7FF)) return 0;
            bool wide =
                (c >= 0x1100 && c <= 0x115F) ||                    // Hangul Jamo
                (c >= 0x2E80 && c <= 0x303E) ||                    // CJK radicals, punctuation
                (c >= 0x3041 && c <= 0x3247) ||                    // kana, CJK compatibility (U+3248–324F, circled
                (c >= 0x3250 && c <= 0x33FF) ||                    // numbers on black squares, are ambiguous: one)
                (c >= 0x3400 && c <= 0x4DBF) ||                    // CJK extension A
                (c >= 0x4E00 && c <= 0x9FFF) ||                    // CJK unified
                (c >= 0xA000 && c <= 0xA4CF) ||                    // Yi
                (c >= 0xAC00 && c <= 0xD7A3) ||                    // Hangul syllables
                (c >= 0xA960 && c <= 0xA97C) ||                    // Hangul Jamo extended-A
                (c >= 0xF900 && c <= 0xFAFF) ||                    // CJK compatibility ideographs
                (c >= 0xFE10 && c <= 0xFE19) ||                    // vertical forms
                (c >= 0xFE30 && c <= 0xFE4F) ||                    // CJK compatibility forms
                (c >= 0xFE50 && c <= 0xFE6B) ||                    // small form variants
                (c >= 0xFF00 && c <= 0xFF60) ||                    // full-width forms
                (c >= 0xFFE0 && c <= 0xFFE6) ||
                IsWideBmpEmoji(c);
            return wide ? 2 : 1;
        }

        // Unicode 15.0 categories Mn, Me and Cf from U+00A0 up (inclusive pairs), fixed so .NET Framework, whose tables
        // are Unicode 8, measures the same: newer combining marks counted one column there.
        private static readonly int[] ZeroWidthRanges =
        {
            0x00AD, 0x00AD, 0x0300, 0x036F, 0x0483, 0x0489, 0x0591, 0x05BD, 0x05BF, 0x05BF, 0x05C1, 0x05C2,
            0x05C4, 0x05C5, 0x05C7, 0x05C7, 0x0600, 0x0605, 0x0610, 0x061A, 0x061C, 0x061C, 0x064B, 0x065F,
            0x0670, 0x0670, 0x06D6, 0x06DD, 0x06DF, 0x06E4, 0x06E7, 0x06E8, 0x06EA, 0x06ED, 0x070F, 0x070F,
            0x0711, 0x0711, 0x0730, 0x074A, 0x07A6, 0x07B0, 0x07EB, 0x07F3, 0x07FD, 0x07FD, 0x0816, 0x0819,
            0x081B, 0x0823, 0x0825, 0x0827, 0x0829, 0x082D, 0x0859, 0x085B, 0x0890, 0x0891, 0x0898, 0x089F,
            0x08CA, 0x0902, 0x093A, 0x093A, 0x093C, 0x093C, 0x0941, 0x0948, 0x094D, 0x094D, 0x0951, 0x0957,
            0x0962, 0x0963, 0x0981, 0x0981, 0x09BC, 0x09BC, 0x09C1, 0x09C4, 0x09CD, 0x09CD, 0x09E2, 0x09E3,
            0x09FE, 0x09FE, 0x0A01, 0x0A02, 0x0A3C, 0x0A3C, 0x0A41, 0x0A42, 0x0A47, 0x0A48, 0x0A4B, 0x0A4D,
            0x0A51, 0x0A51, 0x0A70, 0x0A71, 0x0A75, 0x0A75, 0x0A81, 0x0A82, 0x0ABC, 0x0ABC, 0x0AC1, 0x0AC5,
            0x0AC7, 0x0AC8, 0x0ACD, 0x0ACD, 0x0AE2, 0x0AE3, 0x0AFA, 0x0AFF, 0x0B01, 0x0B01, 0x0B3C, 0x0B3C,
            0x0B3F, 0x0B3F, 0x0B41, 0x0B44, 0x0B4D, 0x0B4D, 0x0B55, 0x0B56, 0x0B62, 0x0B63, 0x0B82, 0x0B82,
            0x0BC0, 0x0BC0, 0x0BCD, 0x0BCD, 0x0C00, 0x0C00, 0x0C04, 0x0C04, 0x0C3C, 0x0C3C, 0x0C3E, 0x0C40,
            0x0C46, 0x0C48, 0x0C4A, 0x0C4D, 0x0C55, 0x0C56, 0x0C62, 0x0C63, 0x0C81, 0x0C81, 0x0CBC, 0x0CBC,
            0x0CBF, 0x0CBF, 0x0CC6, 0x0CC6, 0x0CCC, 0x0CCD, 0x0CE2, 0x0CE3, 0x0D00, 0x0D01, 0x0D3B, 0x0D3C,
            0x0D41, 0x0D44, 0x0D4D, 0x0D4D, 0x0D62, 0x0D63, 0x0D81, 0x0D81, 0x0DCA, 0x0DCA, 0x0DD2, 0x0DD4,
            0x0DD6, 0x0DD6, 0x0E31, 0x0E31, 0x0E34, 0x0E3A, 0x0E47, 0x0E4E, 0x0EB1, 0x0EB1, 0x0EB4, 0x0EBC,
            0x0EC8, 0x0ECE, 0x0F18, 0x0F19, 0x0F35, 0x0F35, 0x0F37, 0x0F37, 0x0F39, 0x0F39, 0x0F71, 0x0F7E,
            0x0F80, 0x0F84, 0x0F86, 0x0F87, 0x0F8D, 0x0F97, 0x0F99, 0x0FBC, 0x0FC6, 0x0FC6, 0x102D, 0x1030,
            0x1032, 0x1037, 0x1039, 0x103A, 0x103D, 0x103E, 0x1058, 0x1059, 0x105E, 0x1060, 0x1071, 0x1074,
            0x1082, 0x1082, 0x1085, 0x1086, 0x108D, 0x108D, 0x109D, 0x109D, 0x135D, 0x135F, 0x1712, 0x1714,
            0x1732, 0x1733, 0x1752, 0x1753, 0x1772, 0x1773, 0x17B4, 0x17B5, 0x17B7, 0x17BD, 0x17C6, 0x17C6,
            0x17C9, 0x17D3, 0x17DD, 0x17DD, 0x180B, 0x180F, 0x1885, 0x1886, 0x18A9, 0x18A9, 0x1920, 0x1922,
            0x1927, 0x1928, 0x1932, 0x1932, 0x1939, 0x193B, 0x1A17, 0x1A18, 0x1A1B, 0x1A1B, 0x1A56, 0x1A56,
            0x1A58, 0x1A5E, 0x1A60, 0x1A60, 0x1A62, 0x1A62, 0x1A65, 0x1A6C, 0x1A73, 0x1A7C, 0x1A7F, 0x1A7F,
            0x1AB0, 0x1ACE, 0x1B00, 0x1B03, 0x1B34, 0x1B34, 0x1B36, 0x1B3A, 0x1B3C, 0x1B3C, 0x1B42, 0x1B42,
            0x1B6B, 0x1B73, 0x1B80, 0x1B81, 0x1BA2, 0x1BA5, 0x1BA8, 0x1BA9, 0x1BAB, 0x1BAD, 0x1BE6, 0x1BE6,
            0x1BE8, 0x1BE9, 0x1BED, 0x1BED, 0x1BEF, 0x1BF1, 0x1C2C, 0x1C33, 0x1C36, 0x1C37, 0x1CD0, 0x1CD2,
            0x1CD4, 0x1CE0, 0x1CE2, 0x1CE8, 0x1CED, 0x1CED, 0x1CF4, 0x1CF4, 0x1CF8, 0x1CF9, 0x1DC0, 0x1DFF,
            0x200B, 0x200F, 0x202A, 0x202E, 0x2060, 0x2064, 0x2066, 0x206F, 0x20D0, 0x20F0, 0x2CEF, 0x2CF1,
            0x2D7F, 0x2D7F, 0x2DE0, 0x2DFF, 0x302A, 0x302D, 0x3099, 0x309A, 0xA66F, 0xA672, 0xA674, 0xA67D,
            0xA69E, 0xA69F, 0xA6F0, 0xA6F1, 0xA802, 0xA802, 0xA806, 0xA806, 0xA80B, 0xA80B, 0xA825, 0xA826,
            0xA82C, 0xA82C, 0xA8C4, 0xA8C5, 0xA8E0, 0xA8F1, 0xA8FF, 0xA8FF, 0xA926, 0xA92D, 0xA947, 0xA951,
            0xA980, 0xA982, 0xA9B3, 0xA9B3, 0xA9B6, 0xA9B9, 0xA9BC, 0xA9BD, 0xA9E5, 0xA9E5, 0xAA29, 0xAA2E,
            0xAA31, 0xAA32, 0xAA35, 0xAA36, 0xAA43, 0xAA43, 0xAA4C, 0xAA4C, 0xAA7C, 0xAA7C, 0xAAB0, 0xAAB0,
            0xAAB2, 0xAAB4, 0xAAB7, 0xAAB8, 0xAABE, 0xAABF, 0xAAC1, 0xAAC1, 0xAAEC, 0xAAED, 0xAAF6, 0xAAF6,
            0xABE5, 0xABE5, 0xABE8, 0xABE8, 0xABED, 0xABED, 0xFB1E, 0xFB1E, 0xFE00, 0xFE0F, 0xFE20, 0xFE2F,
            0xFEFF, 0xFEFF, 0xFFF9, 0xFFFB, 0x101FD, 0x101FD, 0x102E0, 0x102E0, 0x10376, 0x1037A, 0x10A01, 0x10A03,
            0x10A05, 0x10A06, 0x10A0C, 0x10A0F, 0x10A38, 0x10A3A, 0x10A3F, 0x10A3F, 0x10AE5, 0x10AE6, 0x10D24, 0x10D27,
            0x10EAB, 0x10EAC, 0x10EFD, 0x10EFF, 0x10F46, 0x10F50, 0x10F82, 0x10F85, 0x11001, 0x11001, 0x11038, 0x11046,
            0x11070, 0x11070, 0x11073, 0x11074, 0x1107F, 0x11081, 0x110B3, 0x110B6, 0x110B9, 0x110BA, 0x110BD, 0x110BD,
            0x110C2, 0x110C2, 0x110CD, 0x110CD, 0x11100, 0x11102, 0x11127, 0x1112B, 0x1112D, 0x11134, 0x11173, 0x11173,
            0x11180, 0x11181, 0x111B6, 0x111BE, 0x111C9, 0x111CC, 0x111CF, 0x111CF, 0x1122F, 0x11231, 0x11234, 0x11234,
            0x11236, 0x11237, 0x1123E, 0x1123E, 0x11241, 0x11241, 0x112DF, 0x112DF, 0x112E3, 0x112EA, 0x11300, 0x11301,
            0x1133B, 0x1133C, 0x11340, 0x11340, 0x11366, 0x1136C, 0x11370, 0x11374, 0x11438, 0x1143F, 0x11442, 0x11444,
            0x11446, 0x11446, 0x1145E, 0x1145E, 0x114B3, 0x114B8, 0x114BA, 0x114BA, 0x114BF, 0x114C0, 0x114C2, 0x114C3,
            0x115B2, 0x115B5, 0x115BC, 0x115BD, 0x115BF, 0x115C0, 0x115DC, 0x115DD, 0x11633, 0x1163A, 0x1163D, 0x1163D,
            0x1163F, 0x11640, 0x116AB, 0x116AB, 0x116AD, 0x116AD, 0x116B0, 0x116B5, 0x116B7, 0x116B7, 0x1171D, 0x1171F,
            0x11722, 0x11725, 0x11727, 0x1172B, 0x1182F, 0x11837, 0x11839, 0x1183A, 0x1193B, 0x1193C, 0x1193E, 0x1193E,
            0x11943, 0x11943, 0x119D4, 0x119D7, 0x119DA, 0x119DB, 0x119E0, 0x119E0, 0x11A01, 0x11A0A, 0x11A33, 0x11A38,
            0x11A3B, 0x11A3E, 0x11A47, 0x11A47, 0x11A51, 0x11A56, 0x11A59, 0x11A5B, 0x11A8A, 0x11A96, 0x11A98, 0x11A99,
            0x11C30, 0x11C36, 0x11C38, 0x11C3D, 0x11C3F, 0x11C3F, 0x11C92, 0x11CA7, 0x11CAA, 0x11CB0, 0x11CB2, 0x11CB3,
            0x11CB5, 0x11CB6, 0x11D31, 0x11D36, 0x11D3A, 0x11D3A, 0x11D3C, 0x11D3D, 0x11D3F, 0x11D45, 0x11D47, 0x11D47,
            0x11D90, 0x11D91, 0x11D95, 0x11D95, 0x11D97, 0x11D97, 0x11EF3, 0x11EF4, 0x11F00, 0x11F01, 0x11F36, 0x11F3A,
            0x11F40, 0x11F40, 0x11F42, 0x11F42, 0x13430, 0x13440, 0x13447, 0x13455, 0x16AF0, 0x16AF4, 0x16B30, 0x16B36,
            0x16F4F, 0x16F4F, 0x16F8F, 0x16F92, 0x16FE4, 0x16FE4, 0x1BC9D, 0x1BC9E, 0x1BCA0, 0x1BCA3, 0x1CF00, 0x1CF2D,
            0x1CF30, 0x1CF46, 0x1D167, 0x1D169, 0x1D173, 0x1D182, 0x1D185, 0x1D18B, 0x1D1AA, 0x1D1AD, 0x1D242, 0x1D244,
            0x1DA00, 0x1DA36, 0x1DA3B, 0x1DA6C, 0x1DA75, 0x1DA75, 0x1DA84, 0x1DA84, 0x1DA9B, 0x1DA9F, 0x1DAA1, 0x1DAAF,
            0x1E000, 0x1E006, 0x1E008, 0x1E018, 0x1E01B, 0x1E021, 0x1E023, 0x1E024, 0x1E026, 0x1E02A, 0x1E08F, 0x1E08F,
            0x1E130, 0x1E136, 0x1E2AE, 0x1E2AE, 0x1E2EC, 0x1E2EF, 0x1E4EC, 0x1E4EF, 0x1E8D0, 0x1E8D6, 0x1E944, 0x1E94A,
            0xE0001, 0xE0001, 0xE0020, 0xE007F, 0xE0100, 0xE01EF,
        };

        // Unicode 15.0 East Asian Width W and F in U+1F000–1FAFF (inclusive pairs). The block as a whole counted two,
        // but mahjong tiles, cards, enclosed letters, regional indicators and text-style pictographs are narrow.
        private static readonly int[] WideSymbolRanges =
        {
            0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF, 0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F200, 0x1F202, 0x1F210, 0x1F23B,
            0x1F240, 0x1F248, 0x1F250, 0x1F251, 0x1F260, 0x1F265, 0x1F300, 0x1F320, 0x1F32D, 0x1F335, 0x1F337, 0x1F37C,
            0x1F37E, 0x1F393, 0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0, 0x1F3F4, 0x1F3F4, 0x1F3F8, 0x1F43E,
            0x1F440, 0x1F440, 0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D, 0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F57A, 0x1F57A,
            0x1F595, 0x1F596, 0x1F5A4, 0x1F5A4, 0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC, 0x1F6D0, 0x1F6D2,
            0x1F6D5, 0x1F6D7, 0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0,
            0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA88, 0x1FA90, 0x1FABD,
            0x1FABF, 0x1FAC5, 0x1FACE, 0x1FADB, 0x1FAE0, 0x1FAE8, 0x1FAF0, 0x1FAF8,
        };

        private static bool InRanges(int[] ranges, int cp)
        {
            int lo = 0, hi = ranges.Length / 2 - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (cp < ranges[2 * mid]) hi = mid - 1;
                else if (cp > ranges[2 * mid + 1]) lo = mid + 1;
                else return true;
            }
            return false;
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
