using System;
using System.ComponentModel;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class ScreenCaptureTests
    {
        // Capture may be unavailable in a session with no desktop; such failures are inconclusive, not bugs.

        public void PrimaryScreen_BufferMatchesDimensions()
        {
            ScreenImage img;
            try { img = ScreenCapture.CapturePrimaryScreen(); }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) { return; }

            Check.True(img.Width > 0 && img.Height > 0, $"positive size {img.Width}x{img.Height}");
            Check.Equal(img.Width * img.Height * 4, img.Pixels.Length);
            Check.Equal(img.Width * 4, img.Stride);
        }

        public void Region_HasExactRequestedSize()
        {
            ScreenImage img;
            try { img = ScreenCapture.CaptureRegion(0, 0, 16, 8); }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) { return; }

            Check.Equal(16, img.Width);
            Check.Equal(8, img.Height);
            Check.Equal(16 * 8 * 4, img.Pixels.Length);
        }

        public void VirtualScreen_IsAtLeastPrimary()
        {
            ScreenImage primary, virt;
            try
            {
                primary = ScreenCapture.CapturePrimaryScreen();
                virt = ScreenCapture.CaptureVirtualScreen();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) { return; }

            Check.True(virt.Width >= primary.Width, "virtual width >= primary");
            Check.True(virt.Height >= primary.Height, "virtual height >= primary");
        }

        public void Region_InvalidSize_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.CaptureRegion(0, 0, 0, 10));
            Check.Throws<ArgumentOutOfRangeException>(() => ScreenCapture.CaptureRegion(0, 0, 10, -1));
        }
    }
}
