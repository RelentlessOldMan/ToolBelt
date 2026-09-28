// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Turns an arbitrary string into a filename that is safe across platforms: characters returned by
    /// <see cref="Path.GetInvalidFileNameChars"/> are replaced, trailing dots and spaces (which Windows
    /// strips) are trimmed, Windows reserved device names (CON, PRN, NUL, COM1-9, LPT1-9) are escaped,
    /// and the result is capped in length. An empty result becomes "_".
    /// </summary>
    public static class SafeFileName
    {
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public static string MakeSafe(string name, char replacement = '_', int maxLength = 255)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (maxLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Max length must be positive.");

            var invalid = Path.GetInvalidFileNameChars();
            var invalidSet = new HashSet<char>(invalid);
            if (invalidSet.Contains(replacement))
                throw new ArgumentException("Replacement must itself be a valid filename character.", nameof(replacement));

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(invalidSet.Contains(c) ? replacement : c);

            // Windows trims trailing dots and spaces.
            string result = sb.ToString().TrimEnd(' ', '.');

            // Cap length BEFORE the reserved-name check, so truncation cannot afterwards expose a
            // reserved stem (e.g. "CONsole" capped to 3 would otherwise become the device name "CON").
            if (result.Length > maxLength)
                result = result.Substring(0, maxLength).TrimEnd(' ', '.');
            if (result.Length == 0)
                result = replacement.ToString();

            // Escape reserved device names (matched on the part before the first dot). The escape prefix
            // is a valid filename char that no reserved name starts with, so a re-truncation below can't
            // recreate a reserved stem.
            int dot = result.IndexOf('.');
            string stem = dot < 0 ? result : result.Substring(0, dot);
            if (Reserved.Contains(stem))
            {
                result = replacement + result;
                if (result.Length > maxLength)
                    result = result.Substring(0, maxLength).TrimEnd(' ', '.');
            }

            return result.Length == 0 ? replacement.ToString() : result;
        }
    }
}
