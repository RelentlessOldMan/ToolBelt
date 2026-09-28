// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Text
{
    /// <summary>
    /// Masks sensitive strings for display, replacing all but a few characters with a mask character.
    /// The output length always matches the input. <see cref="Trailing"/> keeps the last
    /// <c>visibleCount</c> characters (e.g. a card number → <c>************3456</c>);
    /// <see cref="Leading"/> keeps the first. If <c>visibleCount</c> is at least the length, the string
    /// is returned unchanged.
    /// </summary>
    /// <remarks>The visible/masked boundary is counted in UTF-16 code units, so it can fall between the
    /// halves of a surrogate pair. Not intended for astral-plane text without grapheme-aware handling.</remarks>
    public static class Mask
    {
        /// <summary>Masks all but the last <paramref name="visibleCount"/> characters.</summary>
        public static string Trailing(string value, int visibleCount, char maskChar = '*')
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (visibleCount < 0) throw new ArgumentOutOfRangeException(nameof(visibleCount), visibleCount, "Visible count must not be negative.");

            int maskLength = value.Length - visibleCount;
            if (maskLength <= 0)
                return value;
            return new string(maskChar, maskLength) + value.Substring(maskLength);
        }

        /// <summary>Masks all but the first <paramref name="visibleCount"/> characters.</summary>
        public static string Leading(string value, int visibleCount, char maskChar = '*')
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (visibleCount < 0) throw new ArgumentOutOfRangeException(nameof(visibleCount), visibleCount, "Visible count must not be negative.");

            int maskLength = value.Length - visibleCount;
            if (maskLength <= 0)
                return value;
            return value.Substring(0, visibleCount) + new string(maskChar, maskLength);
        }
    }
}
