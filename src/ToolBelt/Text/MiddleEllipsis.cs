// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Text
{
    /// <summary>
    /// Shortens a string by removing characters from the middle and inserting an ellipsis, keeping both
    /// the start and the end visible — useful for paths and identifiers ("verylongfilename.txt" →
    /// "very…name.txt"). The result never exceeds <c>maxLength</c> characters. Input that already fits is
    /// returned unchanged.
    /// </summary>
    /// <remarks>Length is measured in UTF-16 code units, so the head/tail cuts can fall between the halves
    /// of a surrogate pair. Not intended for astral-plane text without grapheme-aware pre-processing.</remarks>
    public static class MiddleEllipsis
    {
        public static string Apply(string text, int maxLength, string ellipsis = "…")
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (ellipsis is null) throw new ArgumentNullException(nameof(ellipsis));
            if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Max length must not be negative.");

            if (text.Length <= maxLength)
                return text;
            if (maxLength <= ellipsis.Length)
                return ellipsis.Substring(0, maxLength);

            int budget = maxLength - ellipsis.Length;
            int head = (budget + 1) / 2; // head gets the extra char when the budget is odd
            int tail = budget - head;

            return text.Substring(0, head) + ellipsis + text.Substring(text.Length - tail);
        }
    }
}
