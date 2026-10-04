// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 clipboard API).
using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ToolBelt.Windows
{
    /// <summary>
    /// Reads and writes Unicode text on the Windows clipboard via the raw Win32 clipboard API — no WinForms
    /// / WPF dependency. Each open-based operation runs on a short-lived STA thread (the reliable apartment
    /// for clipboard work), and because the clipboard is a single shared OS resource, opening briefly
    /// retries (up to ~1s) when another process holds it. The operations are not safe to call concurrently
    /// from multiple threads in one process.
    /// </summary>
    public static class ClipboardUtils
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        // The clipboard is contended on modern Windows — the Clipboard History / cloud-clipboard service
        // opens it to read every change — so retry (up to ~1s, matching WinForms) before giving up.
        private const int OpenAttempts = 20;
        private const int OpenRetryDelayMs = 50;

        /// <summary>True if the clipboard currently holds Unicode text.</summary>
        public static bool ContainsText() => IsClipboardFormatAvailable(CF_UNICODETEXT);

        /// <summary>Returns the clipboard's Unicode text, or null if it holds no text.</summary>
        public static string? GetText() => RunSta(GetTextCore);

        /// <summary>Replaces the clipboard contents with <paramref name="text"/> (an empty string is valid).</summary>
        public static void SetText(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            RunSta<object?>(() => { SetTextCore(text); return null; });
        }

        /// <summary>Empties the clipboard.</summary>
        public static void Clear() => RunSta<object?>(() => { ClearCore(); return null; });

        private static string? GetTextCore()
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                return null;

            OpenOrThrow();
            try
            {
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                    return null;
                IntPtr ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero)
                    return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }

        private static void SetTextCore(string text)
        {
            // UTF-16 payload including the terminating null.
            byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            if (hGlobal == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalAlloc failed.");

            bool ownershipTransferred = false;
            try
            {
                IntPtr target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalLock failed.");
                try { Marshal.Copy(bytes, 0, target, bytes.Length); }
                finally { GlobalUnlock(hGlobal); }

                OpenOrThrow();
                try
                {
                    EmptyClipboard();
                    if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetClipboardData failed.");
                    ownershipTransferred = true; // the clipboard now owns hGlobal; must NOT free it
                }
                finally { CloseClipboard(); }
            }
            finally
            {
                if (!ownershipTransferred)
                    GlobalFree(hGlobal);
            }
        }

        private static void ClearCore()
        {
            OpenOrThrow();
            try { EmptyClipboard(); }
            finally { CloseClipboard(); }
        }

        // Clipboard work is most reliable on an STA thread; run each operation on a fresh one.
        private static T RunSta<T>(Func<T> func)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return func();

            T result = default!;
            ExceptionDispatchInfo? captured = null;
            var thread = new Thread(() =>
            {
                try { result = func(); }
                catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
            })
            { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            captured?.Throw();
            return result;
        }

        private static void OpenOrThrow()
        {
            for (int attempt = 0; attempt < OpenAttempts; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                    return;
                Thread.Sleep(OpenRetryDelayMs); // another process holds the clipboard; back off briefly
            }
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the clipboard (held by another process).");
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);
    }
}
