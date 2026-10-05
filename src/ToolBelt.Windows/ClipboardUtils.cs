// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), Win32 clipboard API; also copy ScreenCapture.cs (ScreenImage).
using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ToolBelt.Windows
{
    /// <summary>
    /// Reads and writes Unicode text, file lists (Explorer copy/paste) and images on the Windows clipboard via the raw
    /// Win32 clipboard API — no WinForms/WPF
    /// dependency. Each open-based operation runs on a short-lived STA thread (the reliable apartment
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

        // ---------- file lists (CF_HDROP) ----------

        /// <summary>True if the clipboard holds a file list (files copied in Explorer).</summary>
        public static bool ContainsFileDropList() => IsClipboardFormatAvailable(CF_HDROP);

        /// <summary>The copied file and folder paths, or null if the clipboard holds no file list.</summary>
        public static string[]? GetFileDropList() => RunSta(() =>
        {
            if (!IsClipboardFormatAvailable(CF_HDROP)) return null;
            OpenOrThrow();
            try
            {
                IntPtr hDrop = GetClipboardData(CF_HDROP);
                if (hDrop == IntPtr.Zero) return null;
                uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                var files = new string[count];
                for (uint i = 0; i < count; i++)
                {
                    uint len = DragQueryFile(hDrop, i, null, 0);
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFile(hDrop, i, sb, len + 1);
                    files[i] = sb.ToString();
                }
                return files;
            }
            finally { CloseClipboard(); }
        });

        /// <summary>
        /// Puts a file list on the clipboard so Explorer's Paste copies those files (marked as a copy, not a cut). Paths
        /// should be absolute.
        /// </summary>
        public static void SetFileDropList(System.Collections.Generic.IEnumerable<string> paths)
        {
            if (paths is null) throw new ArgumentNullException(nameof(paths));
            var list = new System.Collections.Generic.List<string>();
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p) || p.IndexOf('\0') >= 0) throw new ArgumentException("Paths must be non-empty and contain no NUL.", nameof(paths));
                list.Add(p);
            }
            if (list.Count == 0) throw new ArgumentException("Need at least one path.", nameof(paths));
            // DROPFILES { DWORD pFiles; POINT pt; BOOL fNC; BOOL fWide; } then double-NUL-terminated UTF-16 paths.
            byte[] names = Encoding.Unicode.GetBytes(string.Join("\0", list) + "\0\0");
            var data = new byte[20 + names.Length];
            BitConverter.GetBytes(20).CopyTo(data, 0);
            BitConverter.GetBytes(1).CopyTo(data, 16);
            names.CopyTo(data, 20);
            uint dropEffect = RegisterClipboardFormat("Preferred DropEffect");
            RunSta<object?>(() =>
            {
                SetData(new[] { (CF_HDROP, data), (dropEffect, BitConverter.GetBytes(1)) });   // DROPEFFECT_COPY
                return null;
            });
        }

        // ---------- images (CF_DIB) ----------

        /// <summary>True if the clipboard holds a bitmap image.</summary>
        public static bool ContainsImage() => IsClipboardFormatAvailable(CF_DIB);

        /// <summary>
        /// The clipboard image as BGRA top-down pixels, or null if there is none. Reads 24- and 32-bit DIBs (what screenshots
        /// and most apps put there); alpha is kept only when the source declares an alpha mask, otherwise set to 255.
        /// </summary>
        public static ScreenImage? GetImage() => RunSta<ScreenImage?>(() =>
        {
            if (!IsClipboardFormatAvailable(CF_DIB)) return null;
            OpenOrThrow();
            try
            {
                IntPtr h = GetClipboardData(CF_DIB);
                if (h == IntPtr.Zero) return null;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try
                {
                    int size = (int)GlobalSize(h);
                    var dib = new byte[size];
                    Marshal.Copy(p, dib, 0, size);
                    return DecodeDib(dib);
                }
                finally { GlobalUnlock(h); }
            }
            finally { CloseClipboard(); }
        });

        /// <summary>Puts an image (BGRA top-down) on the clipboard as a 32-bit DIB, pasteable into Paint, Word, Teams, etc.</summary>
        public static void SetImage(ScreenImage image)
        {
            if (image.Pixels is null) throw new ArgumentException("Image has no pixels.", nameof(image));
            int w = image.Width, h = image.Height;
            var dib = new byte[40 + w * h * 4];
            BitConverter.GetBytes(40).CopyTo(dib, 0);                                // BITMAPINFOHEADER
            BitConverter.GetBytes(w).CopyTo(dib, 4);
            BitConverter.GetBytes(h).CopyTo(dib, 8);                                 // positive = bottom-up (most compatible)
            BitConverter.GetBytes((short)1).CopyTo(dib, 12);
            BitConverter.GetBytes((short)32).CopyTo(dib, 14);
            BitConverter.GetBytes(w * h * 4).CopyTo(dib, 20);
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(image.Pixels, y * w * 4, dib, 40 + (h - 1 - y) * w * 4, w * 4);
            RunSta<object?>(() => { SetData(new[] { (CF_DIB, dib) }); return null; });
        }

        /// <summary>Decodes a CF_DIB payload (BITMAPINFOHEADER + pixels; 24/32-bit, BI_RGB or BI_BITFIELDS) to BGRA top-down.</summary>
        public static ScreenImage DecodeDib(byte[] dib)
        {
            if (dib.Length < 40) throw new InvalidOperationException("Clipboard DIB is truncated.");
            int headerSize = BitConverter.ToInt32(dib, 0), w = BitConverter.ToInt32(dib, 4), rawH = BitConverter.ToInt32(dib, 8);
            int bpp = BitConverter.ToInt16(dib, 14), compression = BitConverter.ToInt32(dib, 16), colorsUsed = BitConverter.ToInt32(dib, 32);
            if (w <= 0 || rawH == 0) throw new InvalidOperationException("Clipboard DIB has an invalid size.");
            if (bpp != 24 && bpp != 32) throw new NotSupportedException($"Clipboard DIBs of {bpp} bits per pixel are not supported.");
            if (compression != 0 && compression != 3) throw new NotSupportedException("Compressed clipboard DIBs are not supported.");
            int h = Math.Abs(rawH);
            int offset = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0) + colorsUsed * 4;
            bool hasAlpha = headerSize >= 56 && compression == 3 && BitConverter.ToUInt32(dib, 52) == 0xFF000000;
            int stride = (w * bpp / 8 + 3) & ~3;
            if (offset + (long)stride * h > dib.Length) throw new InvalidOperationException("Clipboard DIB is truncated.");
            var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int src = offset + (rawH > 0 ? h - 1 - y : y) * stride;
                for (int x = 0; x < w; x++)
                {
                    int s = src + x * (bpp / 8), d = (y * w + x) * 4;
                    pixels[d] = dib[s]; pixels[d + 1] = dib[s + 1]; pixels[d + 2] = dib[s + 2];
                    pixels[d + 3] = bpp == 32 && hasAlpha ? dib[s + 3] : (byte)255;
                }
            }
            return new ScreenImage(w, h, pixels);
        }

        // Replaces the clipboard contents with the given formats in one open/empty/set/close.
        private static void SetData((uint Format, byte[] Bytes)[] items)
        {
            var handles = new IntPtr[items.Length];
            try
            {
                for (int i = 0; i < items.Length; i++)
                {
                    handles[i] = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)items[i].Bytes.Length);
                    if (handles[i] == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalAlloc failed.");
                    IntPtr target = GlobalLock(handles[i]);
                    try { Marshal.Copy(items[i].Bytes, 0, target, items[i].Bytes.Length); }
                    finally { GlobalUnlock(handles[i]); }
                }
                OpenOrThrow();
                try
                {
                    EmptyClipboard();
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (SetClipboardData(items[i].Format, handles[i]) == IntPtr.Zero)
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetClipboardData failed.");
                        handles[i] = IntPtr.Zero;                                       // owned by the clipboard now
                    }
                }
                finally { CloseClipboard(); }
            }
            finally
            {
                foreach (IntPtr h in handles) if (h != IntPtr.Zero) GlobalFree(h);
            }
        }

        private const uint CF_DIB = 8, CF_HDROP = 15;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint index, StringBuilder? file, uint length);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

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
