// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Produces URL- and filename-safe "slugs": lowercase ASCII words joined by a separator. Accented
    /// letters are folded to their base form (é → e, ü → u, and ß → ss, æ → ae, ø → o, ł → l, đ → d, þ → th, œ → oe, ı → i,
    /// which have no decomposition); runs of any other character (an unpaired
    /// surrogate included) collapse to a single separator; leading and trailing separators are trimmed.
    /// The operation is idempotent.
    /// </summary>
    public static class Slug
    {
        /// <summary>
        /// Slugifies <paramref name="text"/>. <paramref name="separator"/> joins words (default '-').
        /// If <paramref name="maxLength"/> is positive, the result is truncated to at most that many
        /// characters (never leaving a trailing separator).
        /// </summary>
        public static string Slugify(string text, int maxLength = 0, char separator = '-')
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            // FormD splits accented letters into base + combining mark; we then drop the marks. Normalize throws on an
            // unpaired surrogate, so those become spaces (separators) first.
            string normalized = ReplaceLoneSurrogates(text).Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(normalized.Length);
            bool pendingSeparator = false;

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue; // stripped diacritic

                char lower = char.ToLowerInvariant(c);
                string? folded = Fold(lower);                           // "Straße" gave "stra-e", "Øresund" "resund"
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

        // Latin letters with no canonical decomposition, which FormD leaves whole.
        private static string? Fold(char lower)
        {
            switch (lower)
            {
                case 'ß': return "ss";
                case 'æ': return "ae";
                case 'ø': return "o";
                case 'ł': return "l";
                case 'đ': return "d";
                case 'ð': return "d";
                case 'þ': return "th";
                case 'œ': return "oe";
                case 'ı': return "i";
                default: return null;
            }
        }

        private static string ReplaceLoneSurrogates(string text)
        {
            char[]? chars = null;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                if (char.IsSurrogate(text[i]))
                    (chars ??= text.ToCharArray())[i] = ' ';
            }
            return chars is null ? text : new string(chars);
        }
    }
}
