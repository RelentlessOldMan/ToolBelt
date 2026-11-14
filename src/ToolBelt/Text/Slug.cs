// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Produces URL- and filename-safe "slugs": lowercase ASCII words joined by a separator. Latin letters are folded
    /// to ASCII: accents dropped (é → e, ü → u), compatibility forms expanded (ĳ → ij, ǆ → dz, ſ → s, full-width Ａ → a,
    /// ² → 2), and the letters with no decomposition mapped by hand: ß → ss, æ → ae, œ → oe, þ → th, ŋ → ng, ĸ → q, and
    /// a letter with a stroke, bar, hook or curl to its base letter (ø → o, ł → l, đ → d, ħ → h, ŧ → t, ƀ → b, ƶ → z).
    /// Runs of any other character (other scripts, an unpaired surrogate) collapse to a single separator; leading and
    /// trailing separators are trimmed.
    /// The operation is idempotent.
    /// </summary>
    public static class Slug
    {
        /// <summary>
        /// Slugifies <paramref name="text"/>. <paramref name="separator"/> joins words (default '-'); it must be one of
        /// '-', '_', '.' or '~' (anything else throws <see cref="ArgumentException"/>), so the slug stays URL- and filename-safe
        /// and slugifying it again changes nothing.
        /// If <paramref name="maxLength"/> is positive, the result is truncated to at most that many
        /// characters (never leaving a trailing separator).
        /// </summary>
        public static string Slugify(string text, int maxLength = 0, char separator = '-')
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            // 'É' gave "aÉb", which slugified again to "aeb"; '/' gave a path.
            if (separator != '-' && separator != '_' && separator != '.' && separator != '~')
                throw new ArgumentException("The separator must be '-', '_', '.' or '~'.", nameof(separator));

            // FormKD splits accented letters into base + combining mark (we then drop the marks) and expands compatibility
            // forms: FormD left 'ĳ', 'ŀ', 'ŉ' and 'ǆ' whole, and they were dropped. Normalize throws on an
            // unpaired surrogate and on noncharacters (U+FFFE; on .NET Framework also U+FFFF, U+FDD0 and the astral
            // ones), so those become spaces (separators) first.
            string normalized = NormalizeKD(ReplaceInvalid(text));

            var sb = new StringBuilder(normalized.Length);
            bool pendingSeparator = false;

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue; // stripped diacritic

                char lower = char.ToLowerInvariant(c);
                string? folded = Fold(lower) ?? Fold(c);                // "Straße" gave "stra-e", "Øresund" "resund"
                if (folded != null)
                {
                    bool sep = pendingSeparator && sb.Length > 0;
                    if (maxLength > 0 && sb.Length + (sep ? 1 : 0) + folded.Length > maxLength)
                        break;
                    if (sep)
                        sb.Append(separator);
                    pendingSeparator = false;
                    sb.Append(folded);
                    continue;
                }
                bool isSlugChar = (lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9');

                if (isSlugChar)
                {
                    bool separate = pendingSeparator && sb.Length > 0;
                    if (maxLength > 0 && sb.Length + (separate ? 2 : 1) > maxLength)
                        break;
                    if (separate)
                        sb.Append(separator);
                    pendingSeparator = false;
                    sb.Append(lower);
                }
                else if (sb.Length > 0)
                {
                    pendingSeparator = true; // emitted lazily, only if a real char follows
                }
            }

            return sb.ToString();
        }

        // Latin letters with no decomposition, which FormKD leaves whole: each entry is the letter and its ASCII spelling.
        // Upper-case forms are listed too, for runtimes whose invariant lower-casing predates them. "Ħ" and "ŧ" were dropped.
        private static readonly Dictionary<char, string> Folds = BuildFolds(
            "ßss æae Æae ðd Ðd øo Øo þth Þth đd Đd ħh Ħh ıi ĸq łl Łl ŋng Ŋng œoe Œoe ŧt Ŧt ƀb Ƀb ƃb Ƃb ƈc "
            + "Ƈc ƌd Ƌd ƒf Ƒf ƕhv Ƕhv ƙk Ƙk ƚl Ƚl ƞn Ƞn ƣoi Ƣoi ƥp Ƥp ƫt ƭt Ƭt ƴy Ƴy ƶz Ƶz ǝe Ǝe ǥg Ǥg ȡd ȣou "
            + "Ȣou ȥz Ȥz ȴl ȵn ȶt ȷj ȸdb ȹqp ȼc Ȼc ȿs Ȿs ɀz Ɀz ɇe Ɇe ɉj Ɉj ɋq Ɋq ɍr Ɍr ɏy Ɏy ỻll Ỻll ỽv Ỽv ỿy "
            + "Ỿy ⱡl Ⱡl ⱥa Ⱥa ⱦt Ⱦt ⱨh Ⱨh ⱪk Ⱪk ⱬz Ⱬz ⱱv ⱳw Ⱳw ⱴv ⱸe ⱹr ⱺo ꜩtz Ꜩtz ꜳaa Ꜳaa ꜵao Ꜵao ꜷau Ꜷau ꜹav "
            + "Ꜹav ꜻav Ꜻav ꜽay Ꜽay ꝁk Ꝁk ꝃk Ꝃk ꝅk Ꝅk ꝉl Ꝉl ꝋo Ꝋo ꝍo Ꝍo ꝏoo Ꝏoo ꝑp Ꝑp ꝓp Ꝓp ꝕp Ꝕp ꝗq Ꝗq ꝙq Ꝙq ꝟv "
            + "Ꝟv ꝡvy Ꝡvy ꝫet Ꝫet ꝭis Ꝭis ꝸum ꞁl Ꞁl ꞎl ꞑn Ꞑn ꞓc Ꞓc ꞔc Ꞔc ꞕh ꞗb Ꞗb ꞙf Ꞙf ꞡg Ꞡg ꞣk Ꞣk ꞥn Ꞥn ꞧr Ꞧr "
            + "ꞩs Ꞩs ꞹu Ꞹu ꟈd Ꟈd ꟊs Ꟊs");

        private static Dictionary<char, string> BuildFolds(string table)
        {
            var folds = new Dictionary<char, string>();
            foreach (string entry in table.Split(' '))
                folds[entry[0]] = entry.Substring(1);
            return folds;
        }

        private static string? Fold(char c) => Folds.TryGetValue(c, out string? folded) ? folded : null;

        private static string ReplaceInvalid(string text)
        {
            char[]? chars = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    if (IsNoncharacter(char.ConvertToUtf32(c, text[i + 1])))
                    {
                        chars ??= text.ToCharArray();
                        chars[i] = ' ';
                        chars[i + 1] = ' ';
                    }
                    i++;
                    continue;
                }
                if (char.IsSurrogate(c) || IsNoncharacter(c))
                    (chars ??= text.ToCharArray())[i] = ' ';
            }
            return chars is null ? text : new string(chars);
        }

        private static bool IsNoncharacter(int cp) => (cp >= 0xFDD0 && cp <= 0xFDEF) || (cp & 0xFFFE) == 0xFFFE;

        // Anything else a runtime's normalizer refuses becomes a separator rather than an exception.
        private static string NormalizeKD(string text)
        {
            try { return text.Normalize(NormalizationForm.FormKD); }
            catch (ArgumentException)
            {
                var sb = new StringBuilder(text.Length);
                for (int i = 0; i < text.Length; i++)
                {
                    int n = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                    string one = text.Substring(i, n);
                    try { sb.Append(one.Normalize(NormalizationForm.FormKD)); }
                    catch (ArgumentException) { sb.Append(' ', n); }
                    i += n - 1;
                }
                return sb.ToString();
            }
        }
    }
}
