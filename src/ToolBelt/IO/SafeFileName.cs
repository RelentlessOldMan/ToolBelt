// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>
    /// Turns an arbitrary string into a filename that is safe across platforms: control characters and
    /// <c>" * / : &lt; &gt; ? \ |</c> are replaced on every OS (so a name made on Linux still works on Windows),
    /// trailing dots and spaces (which Windows strips) are trimmed, Windows reserved device names (CON, PRN,
    /// AUX, NUL, COM0-9, LPT0-9 and their superscript-digit forms, CONIN$, CONOUT$, also with an extension or
    /// trailing spaces before it) are escaped, and the result is capped at <c>maxLength</c> UTF-8 bytes (the Unix
    /// limit, which also keeps it within Windows' limit in characters) without splitting a surrogate pair. An empty
    /// result becomes "_".
    /// </summary>
    public static class SafeFileName
    {
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "COM\u00B9", "COM\u00B2", "COM\u00B3",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
        };

        // The Windows set, which is a superset of every other platform's (Unix forbids only '/' and NUL).
        private static bool IsInvalid(char c) => c < 32 || "\"*/:<>?\\|".IndexOf(c) >= 0;

        // Capped in UTF-8 bytes: Linux and macOS limit a name to 255 bytes, so 255 UTF-16 characters of CJK (765 bytes)
        // failed there. A UTF-8 length is never less than the UTF-16 one, so this caps Windows' 255 characters too.
        private static string Truncate(string s, int maxLength)
        {
            int bytes = 0, cut = 0;
            while (cut < s.Length)
            {
                bool pair = char.IsHighSurrogate(s[cut]) && cut + 1 < s.Length && char.IsLowSurrogate(s[cut + 1]);
                int size = pair ? 4 : s[cut] < 0x80 ? 1 : s[cut] < 0x800 ? 2 : 3;   // a lone surrogate is written as U+FFFD
                if (bytes + size > maxLength) break;
                bytes += size;
                cut += pair ? 2 : 1;
            }
            return cut == s.Length ? s : s.Substring(0, cut).TrimEnd(' ', '.');
        }

        public static string MakeSafe(string name, char replacement = '_', int maxLength = 255)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (maxLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Max length must be positive.");

            if (IsInvalid(replacement) || replacement == ' ' || replacement == '.')
                throw new ArgumentException("Replacement must itself be a valid filename character.", nameof(replacement));

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(IsInvalid(c) ? replacement : c);

            // Windows trims trailing dots and spaces.
            string result = sb.ToString().TrimEnd(' ', '.');

            // Cap length BEFORE the reserved-name check, so truncation cannot afterwards expose a
            // reserved stem (e.g. "CONsole" capped to 3 would otherwise become the device name "CON").
            result = Truncate(result, maxLength);
            if (result.Length == 0)
                result = replacement.ToString();

            // Escape reserved device names (matched on the part before the first dot). The escape prefix
            // is a valid filename char that no reserved name starts with, so a re-truncation below can't
            // recreate a reserved stem.
            int dot = result.IndexOf('.');
            string stem = (dot < 0 ? result : result.Substring(0, dot)).TrimEnd(' ');   // Windows ignores "CON .txt"'s space
            if (Reserved.Contains(stem))
                result = Truncate(replacement + result, maxLength);

            return result.Length == 0 ? replacement.ToString() : result;
        }
    }
}
