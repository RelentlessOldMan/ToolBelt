using System;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class WindowUtilsTests
    {
        // Tolerant of a session with no interactive windows.

        public void TopLevelWindows_AreWellFormed()
        {
            var windows = WindowUtils.TopLevelWindows(visibleTitledOnly: true);
            foreach (var w in windows)
            {
                Check.True(w.Handle != IntPtr.Zero, "handle non-zero");
                Check.True(w.Title.Length > 0, "visible+titled filter -> non-empty title");
                Check.True(w.ProcessId > 0, "has owning process id");
                Check.True(w.IsVisible, "visible filter honored");
            }

            // The unfiltered set is a superset of the filtered one.
            var all = WindowUtils.TopLevelWindows(visibleTitledOnly: false);
            Check.True(all.Count >= windows.Count, "unfiltered >= filtered");
        }

        public void Foreground_ConsistentWithTitleLookup()
        {
            var fg = WindowUtils.ForegroundWindowInfo();
            if (fg is null)
            {
                Check.True(WindowUtils.ForegroundWindow() == IntPtr.Zero, "null info -> zero handle");
                return;
            }
            Check.True(fg.Value.Handle != IntPtr.Zero, "foreground handle non-zero");
            // GetWindowTitle on the same handle agrees with the snapshot.
            Check.Equal(fg.Value.Title, WindowUtils.GetWindowTitle(fg.Value.Handle));
        }

        public void FindByTitle_NullThrows()
        {
            Check.Throws<ArgumentNullException>(() => WindowUtils.FindByTitle(null!));
        }

        public void FindByTitle_NonexistentReturnsNull()
        {
            Check.Null(WindowUtils.FindByTitle(" no such window title  zzzz"));
        }
    }
}
