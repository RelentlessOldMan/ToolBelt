// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (COM IShellLink).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ToolBelt.Windows
{
    /// <summary>The contents of a Windows <c>.lnk</c> shortcut.</summary>
    public readonly struct ShortcutInfo
    {
        internal ShortcutInfo(string target, string arguments, string workingDirectory, string description, string iconPath, int iconIndex)
        {
            TargetPath = target;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            Description = description;
            IconPath = iconPath;
            IconIndex = iconIndex;
        }

        /// <summary>
        /// The target: a file path, or for a URL or shell-namespace target (<c>https://…</c>, the Recycle Bin) its shell
        /// parsing name (<c>https://…</c>, <c>::{645FF040-…}</c>), which <see cref="ShortcutUtils.Create"/> takes back.
        /// </summary>
        public string TargetPath { get; }
        public string Arguments { get; }
        public string WorkingDirectory { get; }
        public string Description { get; }
        public string IconPath { get; }
        public int IconIndex { get; }

        public override string ToString() =>
            string.IsNullOrEmpty(Arguments) ? TargetPath ?? "" : $"{TargetPath} {Arguments}";
    }

    /// <summary>
    /// Creates and reads Windows shell shortcuts (<c>.lnk</c>) through the shell's <c>IShellLink</c> COM
    /// interface — no WScript / third-party dependency. <see cref="Create"/> writes a shortcut file;
    /// <see cref="Read"/> parses one back into a <see cref="ShortcutInfo"/>. The shell stores at most 259 characters
    /// of target path and 260 of each other field (longer values come back truncated or garbled), so
    /// <see cref="Create"/> rejects anything longer; the shortcut file's own full path is limited to 259 characters too.
    /// Paths must not be empty or blank, and no value may contain a NUL (the shell
    /// ends each one there).
    /// </summary>
    public static class ShortcutUtils
    {
        private const int MaxPath = 260;                // the shell's per-field limit (a target must also leave room for its NUL)
        private const int ReadBuffer = 1024;
        private const uint SlgpRawPath = 0x4;

        private static void CheckPath(string path, string paramName)
        {
            // A blank path threw COM's bare "Value does not fall within the expected range" (Read: ParamName "path").
            if (path.Trim().Length == 0) throw new ArgumentException("The path must not be empty or blank.", paramName);
            CheckNoNul(path, paramName);
        }

        // The shortcut file itself: IPersistFile cuts its name at MAX_PATH, so Save of a 395-character path returned
        // normally after overwriting whatever file the first 259 characters named (and Load read that file).
        private static void CheckShortcutPath(string path)
        {
            CheckPath(path, "shortcutPath");
            string full;
            try { full = System.IO.Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.IO.PathTooLongException)
            {
                throw new ArgumentException("The shortcut path is not valid: " + ex.Message, "shortcutPath", ex);
            }
            if (full.Length >= MaxPath) throw new ArgumentException($"The shell reads and writes a shortcut file path of at most {MaxPath - 1} characters.", "shortcutPath");
        }

        // The shell rooted a relative target at the drive root (sub\x.exe, ..\x.exe and \sub\x.exe all became C:\...) and
        // a bare name it could not find on the PATH at the Desktop.
        private static string ResolveTarget(string target)
        {
            // The shell refuses the extended-length prefix (E_INVALIDARG): \\?\C:\x.exe is C:\x.exe, \\?\UNC\s\x is \\s\x.
            if (target.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) target = @"\\" + target.Substring(8);
            else if (target.StartsWith(@"\\?\", StringComparison.Ordinal) && target.Length > 6 && target[5] == ':') target = target.Substring(4);
            // A shell item's parsing name, as Read reports one (::{645FF040-...}), was resolved as a relative file name.
            if (target.IndexOf('%') >= 0 || target.StartsWith("::", StringComparison.Ordinal) || System.IO.Path.IsPathFullyQualified(target))
                return target;
            // A scheme or a shell namespace ("shell:", "https://") is not a file path. A colon after a separator is not a
            // scheme: sub\a:b (a stream of a relative file) was kept as written, and the shell refused it.
            int colon = target.IndexOf(':');
            if (colon > 1 && target.IndexOfAny(new[] { '\\', '/' }, 0, colon) < 0) return target;
            string full;
            try { full = System.IO.Path.GetFullPath(target); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.IO.PathTooLongException)
            {
                throw new ArgumentException("The target path is not valid: " + ex.Message, "targetPath", ex);
            }
            bool bare = target.IndexOfAny(new[] { '\\', '/' }) < 0 && colon < 0;
            return !bare || System.IO.File.Exists(full) || System.IO.Directory.Exists(full) ? full : target;
        }

        private static void CheckNoNul(string? value, string paramName)
        {
            if (value != null && value.IndexOf('\0') >= 0) throw new ArgumentException("The value must not contain a NUL character.", paramName);
        }

        /// <summary>
        /// Creates (or overwrites) a shortcut at <paramref name="shortcutPath"/> pointing at <paramref name="targetPath"/>.
        /// An <paramref name="iconIndex"/> without an <paramref name="iconPath"/> picks that icon from the target. A
        /// relative target is resolved against the current directory, as <c>Process.Start</c> does (the shell put
        /// <c>sub\x.exe</c> at the drive root); a bare file name is too when that file exists, and is otherwise left for
        /// the shell to find on the PATH. A target holding <c>%</c> is kept as written for the shell to expand.
        /// </summary>
        public static void Create(
            string shortcutPath, string targetPath,
            string? arguments = null, string? workingDirectory = null, string? description = null,
            string? iconPath = null, int iconIndex = 0)
        {
            if (shortcutPath is null) throw new ArgumentNullException(nameof(shortcutPath));
            if (targetPath is null) throw new ArgumentNullException(nameof(targetPath));
            // An empty target wrote a shortcut to nothing; an empty path threw DirectoryNotFoundException; a NUL cut any value
            // short (a shortcut path with one wrote the file named by its first part).
            CheckShortcutPath(shortcutPath);
            CheckPath(targetPath, nameof(targetPath));
            CheckNoNul(arguments, nameof(arguments));
            CheckNoNul(workingDirectory, nameof(workingDirectory));
            CheckNoNul(description, nameof(description));
            CheckNoNul(iconPath, nameof(iconPath));
            targetPath = ResolveTarget(targetPath);
            if (targetPath.Length >= MaxPath) throw TooLong(nameof(targetPath), MaxPath - 1);
            if (arguments?.Length > MaxPath) throw TooLong(nameof(arguments), MaxPath);
            if (workingDirectory?.Length > MaxPath) throw TooLong(nameof(workingDirectory), MaxPath);
            if (description?.Length > MaxPath) throw TooLong(nameof(description), MaxPath);
            if (iconPath?.Length > MaxPath) throw TooLong(nameof(iconPath), MaxPath);

            IShellLinkW link = NewShellLink();
            try
            {
                // The shell's own refusal (E_INVALIDARG) came out as an ArgumentException naming no parameter.
                try { link.SetPath(targetPath); }
                catch (ArgumentException ex) { throw new ArgumentException("The shell does not accept this target: " + ex.Message, nameof(targetPath), ex); }
                if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
                if (!string.IsNullOrEmpty(workingDirectory)) link.SetWorkingDirectory(workingDirectory);
                if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
                // An index alone was dropped (it read back as 0); the shell's default icon file is the target.
                if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, iconIndex);
                else if (iconIndex != 0) link.SetIconLocation(targetPath, iconIndex);

                ((IPersistFile)link).Save(shortcutPath, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        /// <summary>
        /// Reads the shortcut at <paramref name="shortcutPath"/> into a <see cref="ShortcutInfo"/>. Throws
        /// <see cref="System.IO.InvalidDataException"/> if the file is not a readable shortcut.
        /// </summary>
        public static ShortcutInfo Read(string shortcutPath)
        {
            if (shortcutPath is null) throw new ArgumentNullException(nameof(shortcutPath));
            CheckShortcutPath(shortcutPath);

            // A file shorter than the 76-byte link header (an empty one after a failed write) loaded as a shortcut with no
            // target, where text or random bytes are refused.
            var file = new System.IO.FileInfo(shortcutPath);
            if (file.Exists && file.Length < 0x4C) throw new System.IO.InvalidDataException("The file is not a readable shortcut.");
            IShellLinkW link = NewShellLink();
            try
            {
                // A corrupt file came out as a bare COMException: E_FAIL, or ERROR_NOT_FOUND for a valid header over zeros.
                // A missing or locked file is not a COMException (FileNotFoundException, IOException) and passes through.
                try { ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ */); }
                catch (Exception ex) when (ex is COMException || ex is ArgumentException)
                {
                    throw new System.IO.InvalidDataException("The file is not a readable shortcut.", ex);
                }

                // Buffers larger than the stored limit: a MAX_PATH buffer silently cut a 260-character field short.
                var buffer = new StringBuilder(ReadBuffer);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, SlgpRawPath);
                string target = buffer.ToString();
                // A URL or shell-namespace target is stored as an item ID list with no file path: https://x read back as "".
                if (target.Length == 0) target = ParsingName(link) ?? "";

                buffer = new StringBuilder(ReadBuffer);
                link.GetArguments(buffer, buffer.Capacity);
                string args = buffer.ToString();

                buffer = new StringBuilder(ReadBuffer);
                link.GetWorkingDirectory(buffer, buffer.Capacity);
                string workDir = buffer.ToString();

                buffer = new StringBuilder(ReadBuffer);
                link.GetDescription(buffer, buffer.Capacity);
                string description = buffer.ToString();

                buffer = new StringBuilder(ReadBuffer);
                link.GetIconLocation(buffer, buffer.Capacity, out int iconIndex);
                string iconPath = buffer.ToString();

                return new ShortcutInfo(target, args, workDir, description, iconPath, iconIndex);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        private static string? ParsingName(IShellLinkW link)
        {
            IntPtr pidl = IntPtr.Zero, name = IntPtr.Zero;
            try
            {
                link.GetIDList(out pidl);
                if (pidl == IntPtr.Zero || SHGetNameFromIDList(pidl, SigdnDesktopAbsoluteParsing, out name) != 0 || name == IntPtr.Zero)
                    return null;
                return Marshal.PtrToStringUni(name);
            }
            catch (COMException) { return null; }
            finally
            {
                if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            }
        }

        private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

        private static ArgumentException TooLong(string param, int max) =>
            new ArgumentException($"A shortcut stores at most {max} characters here.", param);

        private static IShellLinkW NewShellLink()
        {
            Type? t = Type.GetTypeFromCLSID(CLSID_ShellLink);
            if (t is null)
                throw new InvalidOperationException("Shell link COM class is unavailable.");
            return (IShellLinkW)Activator.CreateInstance(t)!;
        }

        private static readonly Guid CLSID_ShellLink = new Guid("00021401-0000-0000-C000-000000000046");

        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile(out IntPtr ppszFileName);
        }
    }
}
