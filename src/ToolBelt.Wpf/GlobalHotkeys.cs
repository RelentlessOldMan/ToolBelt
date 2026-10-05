// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF + user32).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;

namespace ToolBelt.Wpf
{
    /// <summary>A key plus modifiers, parsed from and printed as text such as <c>"Ctrl+Alt+K"</c>.</summary>
    public readonly struct HotkeyGesture : IEquatable<HotkeyGesture>
    {
        public HotkeyGesture(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None || IsModifierKey(key)) throw new ArgumentException("The key must be a non-modifier key.", nameof(key));
            Modifiers = modifiers;
            Key = key;
        }

        public ModifierKeys Modifiers { get; }
        public Key Key { get; }

        /// <summary>
        /// Parses "Ctrl+Shift+S", "Alt+F4", "Win+Space", "Ctrl+1" (case-insensitive; Control, Win/Windows accepted).
        /// </summary>
        public static HotkeyGesture Parse(string text)
            => TryParse(text, out HotkeyGesture g) ? g : throw new FormatException($"'{text}' is not a valid hotkey.");

        public static bool TryParse(string? text, out HotkeyGesture gesture)
        {
            gesture = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text!.Split('+');
            var mods = ModifierKeys.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "shift": mods |= ModifierKeys.Shift; break;
                    case "win": case "windows": mods |= ModifierKeys.Windows; break;
                    default: return false;
                }
            }
            string keyText = parts[parts.Length - 1].Trim();
            if (keyText.Length == 0) return false;
            Key key;
            if (keyText.Length == 1 && keyText[0] >= '0' && keyText[0] <= '9')
                key = Key.D0 + (keyText[0] - '0');
            else
            {
                try { key = (Key)(new KeyConverter().ConvertFromInvariantString(keyText) ?? Key.None); }
                catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is FormatException) { return false; }
            }
            if (key == Key.None || IsModifierKey(key)) return false;
            gesture = new HotkeyGesture(mods, key);
            return true;
        }

        /// <summary>Canonical text: modifiers in Ctrl, Alt, Shift, Win order, then the key ("1" rather than "D1").</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            if ((Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            if ((Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((Modifiers & ModifierKeys.Windows) != 0) sb.Append("Win+");
            sb.Append(Key >= Key.D0 && Key <= Key.D9 ? ((int)(Key - Key.D0)).ToString(CultureInfo.InvariantCulture) : Key.ToString());
            return sb.ToString();
        }

        public bool Equals(HotkeyGesture other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object? obj) => obj is HotkeyGesture g && Equals(g);
        public override int GetHashCode() => ((int)Modifiers << 16) ^ (int)Key;
        public static bool operator ==(HotkeyGesture a, HotkeyGesture b) => a.Equals(b);
        public static bool operator !=(HotkeyGesture a, HotkeyGesture b) => !a.Equals(b);

        private static bool IsModifierKey(Key k)
            => k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt
            || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin || k == Key.System;
    }

    /// <summary>
    /// System-wide hotkeys (they fire even when the app is not focused) via Win32 <c>RegisterHotKey</c>, delivered to
    /// a hidden message-only window and raised as callbacks on the thread that created this object — create it on the
    /// UI thread, which already pumps messages. A combination another app has taken cannot be registered:
    /// <see cref="Register(HotkeyGesture, Action, bool)"/> throws and <see cref="TryRegister"/> returns false. Disposing unregisters everything;
    /// do so on exit (the OS also releases them when the process ends). For shortcuts that only apply while a window
    /// is focused, use ordinary WPF <c>InputBindings</c> instead.
    /// </summary>
    public sealed class GlobalHotkeys : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

        private readonly HwndSource _window;
        private readonly Dictionary<int, (HotkeyGesture Gesture, Action Callback)> _hotkeys = new Dictionary<int, (HotkeyGesture, Action)>();
        private int _nextId = 1;
        private bool _disposed;

        public GlobalHotkeys()
        {
            var p = new HwndSourceParameters("ToolBelt.GlobalHotkeys")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE: a message-only window, never shown
                Width = 0,
                Height = 0,
                WindowStyle = 0,
            };
            _window = new HwndSource(p);
            _window.AddHook(WndProc);
        }

        /// <summary>The message window's handle (WM_HOTKEY is delivered here).</summary>
        public IntPtr Handle => _window.Handle;

        /// <summary>The number of hotkeys currently registered.</summary>
        public int Count => _hotkeys.Count;

        /// <summary>Registers <paramref name="gesture"/>; returns an id for <see cref="Unregister"/>. Throws if it is taken.</summary>
        public int Register(HotkeyGesture gesture, Action callback, bool noRepeat = true)
        {
            if (!TryRegister(gesture, callback, out int id, noRepeat))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error == 0 ? ERROR_HOTKEY_ALREADY_REGISTERED : error,
                    $"Could not register hotkey {gesture}; it is probably in use by another application.");
            }
            return id;
        }

        /// <summary>Convenience: <c>Register("Ctrl+Alt+K", ...)</c>.</summary>
        public int Register(string gesture, Action callback, bool noRepeat = true) => Register(HotkeyGesture.Parse(gesture), callback, noRepeat);

        /// <summary>Registers <paramref name="gesture"/> if it is free. <paramref name="noRepeat"/> suppresses auto-repeat while held.</summary>
        public bool TryRegister(HotkeyGesture gesture, Action callback, out int id, bool noRepeat = true)
        {
            ThrowIfDisposed();
            if (callback is null) throw new ArgumentNullException(nameof(callback));
            id = 0;
            uint mods = (uint)gesture.Modifiers | (noRepeat ? MOD_NOREPEAT : 0); // ModifierKeys values match MOD_ALT/CONTROL/SHIFT/WIN
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
            int candidate = _nextId;
            if (!RegisterHotKey(_window.Handle, candidate, mods, vk)) return false;
            _nextId++;
            _hotkeys[candidate] = (gesture, callback);
            id = candidate;
            return true;
        }

        /// <summary>Unregisters a hotkey by id. Returns false if the id is unknown.</summary>
        public bool Unregister(int id)
        {
            ThrowIfDisposed();
            if (!_hotkeys.Remove(id)) return false;
            UnregisterHotKey(_window.Handle, id);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (int id in _hotkeys.Keys) UnregisterHotKey(_window.Handle, id);
            _hotkeys.Clear();
            _window.RemoveHook(WndProc);
            _window.Dispose();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && _hotkeys.TryGetValue(wParam.ToInt32(), out var entry))
            {
                entry.Callback();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GlobalHotkeys));
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
