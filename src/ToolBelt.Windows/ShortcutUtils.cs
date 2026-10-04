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

        public string TargetPath { get; }
        public string Arguments { get; }
        public string WorkingDirectory { get; }
        public string Description { get; }
        public string IconPath { get; }
        public int IconIndex { get; }

        public override string ToString() =>
            Arguments.Length == 0 ? TargetPath : $"{TargetPath} {Arguments}";
    }

    /// <summary>
    /// Creates and reads Windows shell shortcuts (<c>.lnk</c>) through the shell's <c>IShellLink</c> COM
    /// interface — no WScript / third-party dependency. <see cref="Create"/> writes a shortcut file;
    /// <see cref="Read"/> parses one back into a <see cref="ShortcutInfo"/>.
    /// </summary>
    public static class ShortcutUtils
    {
        private const int MaxPath = 260;
        private const int InfoTipSize = 1024;
        private const uint SlgpRawPath = 0x4;

        /// <summary>Creates (or overwrites) a shortcut at <paramref name="shortcutPath"/> pointing at <paramref name="targetPath"/>.</summary>
        public static void Create(
            string shortcutPath, string targetPath,
            string? arguments = null, string? workingDirectory = null, string? description = null,
            string? iconPath = null, int iconIndex = 0)
        {
            if (shortcutPath is null) throw new ArgumentNullException(nameof(shortcutPath));
            if (targetPath is null) throw new ArgumentNullException(nameof(targetPath));

            IShellLinkW link = NewShellLink();
            try
            {
                link.SetPath(targetPath);
                if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
                if (!string.IsNullOrEmpty(workingDirectory)) link.SetWorkingDirectory(workingDirectory);
                if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
                if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, iconIndex);

                ((IPersistFile)link).Save(shortcutPath, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        /// <summary>Reads the shortcut at <paramref name="shortcutPath"/> into a <see cref="ShortcutInfo"/>.</summary>
        public static ShortcutInfo Read(string shortcutPath)
        {
            if (shortcutPath is null) throw new ArgumentNullException(nameof(shortcutPath));

            IShellLinkW link = NewShellLink();
            try
            {
                ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ */);

                var buffer = new StringBuilder(MaxPath);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, SlgpRawPath);
                string target = buffer.ToString();

                buffer = new StringBuilder(InfoTipSize);
                link.GetArguments(buffer, buffer.Capacity);
                string args = buffer.ToString();

                buffer = new StringBuilder(MaxPath);
                link.GetWorkingDirectory(buffer, buffer.Capacity);
                string workDir = buffer.ToString();

                buffer = new StringBuilder(InfoTipSize);
                link.GetDescription(buffer, buffer.Capacity);
                string description = buffer.ToString();

                buffer = new StringBuilder(MaxPath);
                link.GetIconLocation(buffer, buffer.Capacity, out int iconIndex);
                string iconPath = buffer.ToString();

                return new ShortcutInfo(target, args, workDir, description, iconPath, iconIndex);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

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
