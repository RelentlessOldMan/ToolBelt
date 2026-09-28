// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// American Soundex phonetic coding: a one-letter + three-digit key that groups words by rough
    /// English pronunciation ("Robert" and "Rupert" both code to R163). Non-letters are ignored, casing
    /// is folded, and the classic <c>h</c>/<c>w</c> bridging rule is applied (letters separated only by
    /// <c>h</c> or <c>w</c> are treated as adjacent). Input with no letters yields an empty string.
    /// </summary>
    public static class Soundex
    {
        public static string Encode(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));

            var sb = new StringBuilder(4);
            int previousCode = -1;
            bool haveFirst = false;

            foreach (char raw in value)
            {
                if (!char.IsLetter(raw))
                    continue;

                char c = char.ToUpperInvariant(raw);

                if (!haveFirst)
                {
                    sb.Append(c);
                    previousCode = Code(c);
                    haveFirst = true;
                    continue;
                }

                int code = Code(c);
                if (code != 0)
                {
                    if (code != previousCode)
                    {
                        sb.Append((char)('0' + code));
                        if (sb.Length == 4)
                            break;
                    }
                    previousCode = code;
                }
                else if (c != 'H' && c != 'W')
                {
                    // A vowel (or Y) resets the run so the consonants around it are both counted;
                    // H and W do NOT reset, so they bridge equal-coded consonants.
                    previousCode = 0;
                }
            }

            if (!haveFirst)
                return string.Empty;

            while (sb.Length < 4)
                sb.Append('0');
            return sb.ToString();
        }

        private static int Code(char upper)
        {
            switch (upper)
            {
                case 'B': case 'F': case 'P': case 'V': return 1;
                case 'C': case 'G': case 'J': case 'K': case 'Q': case 'S': case 'X': case 'Z': return 2;
                case 'D': case 'T': return 3;
                case 'L': return 4;
                case 'M': case 'N': return 5;
                case 'R': return 6;
                default: return 0; // A E I O U Y H W
            }
        }
    }
}
