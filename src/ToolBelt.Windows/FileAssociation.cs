// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 shlwapi).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ToolBelt.Windows
{
    /// <summary>
    /// Looks up the program associated with a file extension via the Win32 <c>AssocQueryString</c> API.
    /// Read-only — it reports the current association without changing it. A method returns null only when
    /// the shell yields no value; note that for <i>unregistered</i> extensions Windows commonly supplies a
    /// fallback (the "Open With" chooser for the executable, or a default property list) rather than failing,
    /// so a non-null result does not by itself prove a real association. The extension may be given with or
    /// without a leading dot.
    /// </summary>
    public static class FileAssociation
    {
        /// <summary>The full path of the executable that opens files of this extension (the "Open With" chooser if unregistered).</summary>
        public static string? GetAssociatedExecutable(string extension) => Query(ASSOCSTR_EXECUTABLE, extension);

        /// <summary>The friendly display name of the default app (e.g. "Notepad"), or null if none.</summary>
        public static string? GetFriendlyAppName(string extension) => Query(ASSOCSTR_FRIENDLYAPPNAME, extension);

        /// <summary>The command line used to open the file (with argument placeholders), or null if none.</summary>
        public static string? GetOpenCommand(string extension) => Query(ASSOCSTR_COMMAND, extension);

        /// <summary>The ProgID associated with the extension (e.g. "txtfile"), or null if none.</summary>
        public static string? GetProgId(string extension) => Query(ASSOCSTR_PROGID, extension);

        private static string? Query(int str, string extension)
        {
            if (extension is null) throw new ArgumentNullException(nameof(extension));
            if (extension.Length == 0) throw new ArgumentException("Extension must not be empty.", nameof(extension));
            string assoc = extension[0] == '.' ? extension : "." + extension;

            uint length = 0;
            // First call: ask for the required buffer length.
            int hr = AssocQueryString(ASSOCF_NONE, str, assoc, null, null, ref length);
            if (length == 0)
                return null; // no association
            var sb = new StringBuilder((int)length);
            hr = AssocQueryString(ASSOCF_NONE, str, assoc, null, sb, ref length);
            if (hr != 0)
                return null; // S_OK == 0; anything else means not found / not available
            string value = sb.ToString();
            return value.Length == 0 ? null : value;
        }

        private const int ASSOCF_NONE = 0;
        private const int ASSOCSTR_COMMAND = 1;
        private const int ASSOCSTR_EXECUTABLE = 2;
        private const int ASSOCSTR_FRIENDLYAPPNAME = 4;
        private const int ASSOCSTR_PROGID = 11;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int AssocQueryString(
            int flags, int str, string pszAssoc, string? pszExtra, StringBuilder? pszOut, ref uint pcchOut);
    }
}
