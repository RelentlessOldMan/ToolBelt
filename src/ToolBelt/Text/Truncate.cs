// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Text
{
    /// <summary>
    /// Truncates strings to a maximum length, appending an ellipsis to mark elision. The returned string
    /// never exceeds <c>maxLength</c> characters (the ellipsis is counted within the budget). Input that
    /// already fits is returned unchanged.
    /// </summary>
    /// <remarks>Length is measured in UTF-16 code units, so a cut can fall between the halves of a
    /// surrogate pair. Not intended for astral-plane text without grapheme-aware pre-processing.</remarks>
    public static class Truncate
    {
        /// <summary>Truncates at an exact character budget, appending <paramref name="ellipsis"/> if elided.</summary>
        public static string WithEllipsis(string text, int maxLength, string ellipsis = "…")
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (ellipsis is null) throw new ArgumentNullException(nameof(ellipsis));
            if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Max length must not be negative.");

            if (text.Length <= maxLength)
                return text;
            // Not enough room for text + ellipsis: return as much of the ellipsis as fits.
            if (maxLength <= ellipsis.Length)
                return ellipsis.Substring(0, maxLength);

            return text.Substring(0, maxLength - ellipsis.Length) + ellipsis;
        }

        /// <summary>
        /// Like <see cref="WithEllipsis"/> but avoids cutting mid-word: after reserving room for the
        /// ellipsis, the cut is pulled back to the last whitespace when one exists in the kept span.
        /// </summary>
        public static string WithEllipsisOnWord(string text, int maxLength, string ellipsis = "…")
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (ellipsis is null) throw new ArgumentNullException(nameof(ellipsis));
            if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Max length must not be negative.");

            if (text.Length <= maxLength)
                return text;
            if (maxLength <= ellipsis.Length)
                return ellipsis.Substring(0, maxLength);

            int budget = maxLength - ellipsis.Length;
            string head = text.Substring(0, budget);

            int lastSpace = head.LastIndexOf(' ');
            if (lastSpace > 0)
                head = head.Substring(0, lastSpace);

            return head + ellipsis;
        }
    }
}
