using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class GlobalHotkeysTests
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_HOTKEY = 0x0312;

        // Obscure combinations nobody binds; the first free one is used so another app can't make the test flaky.
        private static readonly string[] Candidates =
        {
            "Ctrl+Alt+Shift+F24", "Ctrl+Alt+Shift+F23", "Ctrl+Alt+Shift+F22", "Ctrl+Alt+Win+F21",
        };

        private static HotkeyGesture FreeGesture(GlobalHotkeys hk, Action callback, out int id)
        {
            foreach (string c in Candidates)
                if (hk.TryRegister(HotkeyGesture.Parse(c), callback, out id)) return HotkeyGesture.Parse(c);
            throw new InvalidOperationException("No candidate hotkey was free.");
        }

        public void Gesture_ParseAndCanonicalText()
        {
            HotkeyGesture g = HotkeyGesture.Parse("Ctrl+Alt+K");
            Check.Equal(ModifierKeys.Control | ModifierKeys.Alt, g.Modifiers);
            Check.Equal(Key.K, g.Key);
            Check.Equal("Ctrl+Shift+F5", HotkeyGesture.Parse("  shift + CONTROL + f5 ").ToString());
            Check.Equal("Ctrl+Alt+Shift+Win+F24", HotkeyGesture.Parse("win+shift+alt+ctrl+F24").ToString());
            Check.Equal(Key.D1, HotkeyGesture.Parse("Ctrl+1").Key);
            Check.Equal("Ctrl+1", HotkeyGesture.Parse("Ctrl+1").ToString());
            Check.Equal(Key.Space, HotkeyGesture.Parse("Win+Space").Key);
            Check.True(HotkeyGesture.Parse("ctrl+k") == HotkeyGesture.Parse("Control+K"));
        }

        public void Gesture_RejectsNonsense()
        {
            foreach (string bad in new[] { "", "   ", "Ctrl+", "Hyper+K", "Ctrl+Shift", "Ctrl+NotAKey", "+" })
                Check.False(HotkeyGesture.TryParse(bad, out _), $"'{bad}' should not parse");
            Check.Throws<FormatException>(() => HotkeyGesture.Parse("Ctrl+"));
            Check.Throws<ArgumentException>(() => new HotkeyGesture(ModifierKeys.Control, Key.LeftShift));
        }

        public void Register_FireAndUnregister()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                using var hk = new GlobalHotkeys();
                int fired = 0;
                FreeGesture(hk, () => fired++, out int id);
                Check.Equal(1, hk.Count);

                SendMessage(hk.Handle, WM_HOTKEY, new IntPtr(id), IntPtr.Zero);   // what Windows sends on the key press
                Check.Equal(1, fired);
                SendMessage(hk.Handle, WM_HOTKEY, new IntPtr(id + 1000), IntPtr.Zero); // unknown id: ignored
                Check.Equal(1, fired);

                Check.True(hk.Unregister(id));
                Check.False(hk.Unregister(id));
                SendMessage(hk.Handle, WM_HOTKEY, new IntPtr(id), IntPtr.Zero);
                Check.Equal(1, fired);
                Check.Equal(0, hk.Count);
            });
        }

        public void TakenCombination_IsRefusedUntilReleased()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var first = new GlobalHotkeys();
                HotkeyGesture g = FreeGesture(first, () => { }, out _);
                using var second = new GlobalHotkeys();
                Check.False(second.TryRegister(g, () => { }, out _));
                Check.Throws<Win32Exception>(() => second.Register(g, () => { }));

                first.Dispose();                                                   // releases its hotkeys
                Check.True(second.TryRegister(g, () => { }, out _));
                Check.Throws<ObjectDisposedException>(() => first.Register(g, () => { }));
            });
        }
    }
}
