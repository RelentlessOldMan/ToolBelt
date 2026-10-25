// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 GDI P/Invoke).
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>
    /// A raw 32-bit image: a screen capture, or an image to or from the clipboard. Pixels are stored top-down, one row
    /// after another, four bytes each in <b>BGRA</b> order. Screen captures leave alpha unused/zero (GDI has no real
    /// alpha channel). Row <c>r</c>, column <c>c</c> starts at byte index <c>(r * Width + c) * 4</c>.
    /// </summary>
    public readonly struct ScreenImage
    {
        /// <summary>Wraps a BGRA top-down buffer of exactly <c>width * height * 4</c> bytes.</summary>
        public ScreenImage(int width, int height, byte[] pixels)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "Size must be positive.");
            if (pixels is null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != (long)width * height * 4) throw new ArgumentException("Buffer length must be width * height * 4.", nameof(pixels));
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>The BGRA pixel bytes, length <c>Width * Height * 4</c>, top-down.</summary>
        public byte[] Pixels { get; }

        /// <summary>Bytes per row (<c>Width * 4</c>).</summary>
        public int Stride => Width * 4;

        public override string ToString() => $"{Width}x{Height} ({Pixels.Length} bytes BGRA)";
    }

    /// <summary>
    /// Captures the screen (or a region) to a raw pixel buffer using GDI's <c>BitBlt</c> — no
    /// <c>System.Drawing</c> dependency, so the satellite stays package-free. Returns a
    /// <see cref="ScreenImage"/> the caller can save or inspect however they like. Sizes and coordinates are
    /// <b>physical pixels</b> (what <see cref="MonitorInfo"/> and <see cref="DpiInfo"/> report) whatever the calling
    /// process's DPI awareness: each capture runs per-monitor-v2 aware, so a DPI-unaware app on a 200% display gets the
    /// whole 3840×2160 screen rather than its top-left 1920×1080 quarter.
    /// </summary>
    public static class ScreenCapture
    {
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int BI_RGB = 0;
        private const int DIB_RGB_COLORS = 0;
        private const uint SRCCOPY = 0x00CC0020;
        private const uint CAPTUREBLT = 0x40000000;

        /// <summary>Captures the primary monitor.</summary>
        public static ScreenImage CapturePrimaryScreen()
        {
            IntPtr previous = EnterPhysicalPixels();
            try
            {
                int w = GetSystemMetrics(SM_CXSCREEN);
                int h = GetSystemMetrics(SM_CYSCREEN);
                return Capture(0, 0, w, h);
            }
            finally { LeavePhysicalPixels(previous); }
        }

        /// <summary>Captures the full virtual desktop (the bounding box of all monitors).</summary>
        public static ScreenImage CaptureVirtualScreen()
        {
            IntPtr previous = EnterPhysicalPixels();
            try
            {
                int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
                int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
                int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
                return Capture(x, y, w, h);
            }
            finally { LeavePhysicalPixels(previous); }
        }

        /// <summary>Captures a rectangular region in virtual-screen coordinates (physical pixels).</summary>
        public static ScreenImage CaptureRegion(int x, int y, int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            if ((long)width * height > int.MaxValue / 4) throw new ArgumentOutOfRangeException(nameof(width), width, "The region is too large for one image.");
            IntPtr previous = EnterPhysicalPixels();
            try { return Capture(x, y, width, height); }
            finally { LeavePhysicalPixels(previous); }
        }

        // Per-monitor-v2 for the duration, so GetSystemMetrics and BitBlt agree on physical pixels. Returns the previous
        // context (zero if the switch is unavailable, in which case the thread's own awareness is used). Before Windows 10
        // 1607 (Server 2012 R2) the function doesn't exist, which is the same case.
        private static IntPtr EnterPhysicalPixels()
        {
            try { return SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { return IntPtr.Zero; }
        }

        private static void LeavePhysicalPixels(IntPtr previous)
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }

        private static ScreenImage Capture(int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("The screen reported a non-positive size (no usable desktop).");
            if ((long)width * height > int.MaxValue / 4)
                throw new InvalidOperationException("The screen is too large for one image.");

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed (no desktop in this session).");

            IntPtr memDc = IntPtr.Zero, bmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
            try
            {
                memDc = CreateCompatibleDC(screenDc);
                bmp = CreateCompatibleBitmap(screenDc, width, height);
                if (memDc == IntPtr.Zero || bmp == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a compatible DC/bitmap.");

                oldBmp = SelectObject(memDc, bmp);
                if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY | CAPTUREBLT))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt failed.");
                SelectObject(memDc, oldBmp);                                       // GetDIBits needs the bitmap deselected
                oldBmp = IntPtr.Zero;

                var header = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // negative => top-down rows
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                };
                var pixels = new byte[width * height * 4];
                if (GetDIBits(memDc, bmp, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDIBits failed.");

                return new ScreenImage(width, height, pixels);
            }
            finally
            {
                if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
                if (bmp != IntPtr.Zero) DeleteObject(bmp);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbi, int uUsage);
    }
}
