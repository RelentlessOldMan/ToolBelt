// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Text;

namespace ToolBelt.Text
{
    /// <summary>
    /// Produces URL- and filename-safe "slugs": lowercase ASCII words joined by a separator. Accented
    /// letters are folded to their base form (é → e, ü → u); runs of any other character collapse to a
    /// single separator; leading and trailing separators are trimmed. The operation is idempotent.
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

            // FormD splits accented letters into base + combining mark; we then drop the marks.
            string normalized = text.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(normalized.Length);
            bool pendingSeparator = false;

            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue; // stripped diacritic

                char lower = char.ToLowerInvariant(c);
                bool isSlugChar = (lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9');

                if (isSlugChar)
                {
                    if (pendingSeparator && sb.Length > 0)
                        sb.Append(separator);
                    pendingSeparator = false;
                    sb.Append(lower);

                    if (maxLength > 0 && sb.Length >= maxLength)
                        break;
                }
                else if (sb.Length > 0)
                {
                    pendingSeparator = true; // emitted lazily, only if a real char follows
                }
            }

            return sb.ToString();
        }
    }
}
