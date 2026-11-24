// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ToolBelt.Wpf
{
    /// <summary>A UI theme. <see cref="System"/> follows the Windows "app mode" setting.</summary>
    public enum AppTheme
    {
        Light,
        Dark,
        System,
    }

    /// <summary>
    /// Switches light/dark themes at runtime by swapping one theme <see cref="ResourceDictionary"/> in a target's
    /// <c>MergedDictionaries</c> (normally <c>Application.Current.Resources</c>). Register a dictionary per theme —
    /// an instance or a XAML <see cref="Uri"/> — then <see cref="Apply"/>: the previously applied theme dictionary is
    /// replaced in place, so other merged dictionaries and their order are untouched, and controls that reference
    /// theme keys with <c>DynamicResource</c> restyle immediately. <see cref="AppTheme.System"/> resolves through
    /// <see cref="GetSystemTheme"/>; with <see cref="FollowSystem"/> on, a change in Windows settings re-applies it
    /// (marshalled to the dispatcher of the thread that created the manager; created on a non-STA thread that has none,
    /// the application's, and turning it on with neither, or after that thread has ended, throws
    /// <see cref="InvalidOperationException"/>; if the thread ends while it is on, it turns itself off). <see cref="CreateDefaultPalette"/>
    /// offers a minimal starter set of brushes. Dispose to stop listening for system changes.
    /// </summary>
    public sealed class ThemeManager : IDisposable
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private readonly ResourceDictionary _target;
        private readonly Dispatcher? _dispatcher;
        private ResourceDictionary? _light, _dark, _applied;
        private Uri? _lightUri, _darkUri;
        private bool _followSystem, _subscribed, _disposed;

        public ThemeManager(ResourceDictionary target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            // Dispatcher.CurrentDispatcher made a dispatcher nothing pumped on a thread without one, so following the
            // system theme silently did nothing and the queued operations piled up.
            // An STA thread may become a UI thread, so it still gets its own dispatcher.
            _dispatcher = Dispatcher.FromThread(Thread.CurrentThread)
                ?? (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA ? Dispatcher.CurrentDispatcher : Application.Current?.Dispatcher);
        }

        /// <summary>A manager for the running application's resources.</summary>
        public static ThemeManager ForApplication()
            => new ThemeManager((Application.Current ?? throw new InvalidOperationException("No WPF Application is running.")).Resources);

        /// <summary>The theme last passed to <see cref="Apply"/> (may be <see cref="AppTheme.System"/>).</summary>
        public AppTheme Requested { get; private set; } = AppTheme.Light;

        /// <summary>The light or dark theme actually in effect, or null before the first <see cref="Apply"/>.</summary>
        public AppTheme? Effective { get; private set; }

        /// <summary>Raised after the effective theme changes.</summary>
        public event EventHandler<AppTheme>? ThemeChanged;

        /// <summary>
        /// Raised on the dispatcher when re-applying <see cref="AppTheme.System"/> after a Windows setting change fails (a
        /// broken or missing dictionary, or a throwing <see cref="ThemeChanged"/> handler). The current theme stays; with
        /// no handler the error goes to <see cref="Trace"/>. It no longer escapes as an unhandled dispatcher exception.
        /// </summary>
        public event EventHandler<Exception>? SystemThemeApplyFailed;

        /// <summary>Re-apply <see cref="AppTheme.System"/> when the Windows setting changes.</summary>
        public bool FollowSystem
        {
            get => _followSystem;
            set
            {
                ThrowIfDisposed();
                // queued to a dispatcher nothing pumped: none, or the dispatcher of an STA thread that has ended
                if (value && (_dispatcher is null || !_dispatcher.Thread.IsAlive || _dispatcher.HasShutdownStarted))
                    throw new InvalidOperationException("Following the system theme needs a dispatcher: create the manager on a UI thread.");
                _followSystem = value;
                if (value && !_subscribed) { SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; _subscribed = true; }
                else if (!value && _subscribed) { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; _subscribed = false; }
            }
        }

        /// <summary>Registers the dictionary for <see cref="AppTheme.Light"/> or <see cref="AppTheme.Dark"/>.</summary>
        public ThemeManager Register(AppTheme theme, ResourceDictionary dictionary)
        {
            if (dictionary is null) throw new ArgumentNullException(nameof(dictionary));
            // Merging the target into itself made the next missing-key lookup recurse until the process died.
            if (Reaches(dictionary, _target))
                throw new ArgumentException("The dictionary is, or merges, the target dictionary: applying it would merge the target into itself.", nameof(dictionary));
            switch (theme)
            {
                case AppTheme.Light: _light = dictionary; _lightUri = null; break;
                case AppTheme.Dark: _dark = dictionary; _darkUri = null; break;
                default: throw new ArgumentException("Register Light or Dark; System resolves to one of them.", nameof(theme));
            }
            return this;
        }

        /// <summary>
        /// Registers a XAML resource dictionary (pack or file URI), loaded when first applied. A file is read once and
        /// released, so it can be edited or replaced while the app runs.
        /// </summary>
        public ThemeManager Register(AppTheme theme, Uri source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            switch (theme)
            {
                case AppTheme.Light: _lightUri = source; _light = null; break;
                case AppTheme.Dark: _darkUri = source; _dark = null; break;
                default: throw new ArgumentException("Register Light or Dark; System resolves to one of them.", nameof(theme));
            }
            return this;
        }

        /// <summary>Applies a theme. Re-applying the theme already in effect changes nothing and raises no event.</summary>
        public void Apply(AppTheme theme)
        {
            ThrowIfDisposed();
            CheckTheme(theme);   // (AppTheme)42 applied Light and reported 42 as the effective theme
            AppTheme effective = theme == AppTheme.System ? GetSystemTheme() : theme;
            ResourceDictionary dictionary = Resolve(effective);   // throws for an unregistered theme: change nothing first
            // Checked again here: the dictionary may have merged the target since it was registered, or been loaded from a Uri.
            if (Reaches(dictionary, _target))
                throw new InvalidOperationException("The theme dictionary is, or merges, the target dictionary: applying it would merge the target into itself.");
            Requested = theme;
            if (ReferenceEquals(dictionary, _applied))
            {
                if (Effective == effective) return;
                Effective = effective;                            // one dictionary registered for both themes
                ThemeChanged?.Invoke(this, effective);
                return;
            }

            var merged = _target.MergedDictionaries;
            int index = _applied is null ? -1 : merged.IndexOf(_applied);
            // On the first Apply, adopt a registered dictionary the app already merged (App.xaml): adding a second copy
            // left the first behind it, so it outlived every later switch. A theme registered by Uri is matched by the
            // merged copy's Source (it is a different instance).
            // An app that merged both themes kept the other one behind the slot it was given: Light reported Light and
            // showed Dark, and after Dark the list held two Darks, so Light could never show again. The first theme
            // dictionary found is the slot; the others are removed.
            if (_applied is null)
            {
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    ResourceDictionary d = merged[i];
                    bool known = d != null && (ReferenceEquals(d, dictionary) || ReferenceEquals(d, _light) || ReferenceEquals(d, _dark)
                        || SameSource(d.Source, _lightUri) || SameSource(d.Source, _darkUri));
                    if (!known) continue;
                    if (index >= 0) merged.RemoveAt(index);
                    index = i;
                }
            }
            if (index < 0) merged.Add(dictionary);
            else if (!ReferenceEquals(merged[index], dictionary)) merged[index] = dictionary;
            _applied = dictionary;
            Effective = effective;
            ThemeChanged?.Invoke(this, effective);
        }

        /// <summary>The Windows app-mode setting (Settings ▸ Personalization ▸ Colors); Light when unavailable.</summary>
        public static AppTheme GetSystemTheme()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return ThemeFromRegistryValue(key?.GetValue("AppsUseLightTheme"));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return AppTheme.Light;
            }
        }

        /// <summary>
        /// Interprets the <c>AppsUseLightTheme</c> registry value: an integer 0 (a DWORD or a QWORD) means dark; anything
        /// else (or missing) light.
        /// </summary>
        public static AppTheme ThemeFromRegistryValue(object? value)
        {
            // Only an int (DWORD) 0 was dark: a QWORD comes back as a long, and its 0 gave Light.
            bool zero = value switch
            {
                int i => i == 0,
                long l => l == 0,
                uint u => u == 0,
                ulong u => u == 0,
                short s => s == 0,
                ushort s => s == 0,
                byte b => b == 0,
                sbyte b => b == 0,
                _ => false,
            };
            return zero ? AppTheme.Dark : AppTheme.Light;
        }

        /// <summary>
        /// A minimal starter palette with frozen brushes under the keys <c>ThemeBackgroundBrush</c>,
        /// <c>ThemeSurfaceBrush</c>, <c>ThemeForegroundBrush</c>, <c>ThemeMutedForegroundBrush</c>, <c>ThemeBorderBrush</c>
        /// and <c>ThemeAccentBrush</c> — reference them with <c>DynamicResource</c>.
        /// </summary>
        public static ResourceDictionary CreateDefaultPalette(AppTheme theme)
        {
            CheckTheme(theme);
            bool dark = (theme == AppTheme.System ? GetSystemTheme() : theme) == AppTheme.Dark;
            var d = new ResourceDictionary
            {
                ["ThemeBackgroundBrush"] = Brush(dark ? 0x1E1E1E : 0xFFFFFF),
                ["ThemeSurfaceBrush"] = Brush(dark ? 0x2B2B2B : 0xF4F4F4),
                ["ThemeForegroundBrush"] = Brush(dark ? 0xF0F0F0 : 0x1A1A1A),
                ["ThemeMutedForegroundBrush"] = Brush(dark ? 0xA0A0A0 : 0x5F5F5F),
                ["ThemeBorderBrush"] = Brush(dark ? 0x3F3F3F : 0xD0D0D0),
                ["ThemeAccentBrush"] = Brush(dark ? 0x4CC2FF : 0x0067C0),
            };
            return d;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_subscribed) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _subscribed = false;
            _disposed = true;
        }

        // Whether target is root or anywhere in its merged tree (the tree itself may already hold a cycle).
        private static bool Reaches(ResourceDictionary root, ResourceDictionary target)
        {
            var seen = new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<ResourceDictionary>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ResourceDictionary d = stack.Pop();
                if (ReferenceEquals(d, target)) return true;
                if (!seen.Add(d)) continue;
                foreach (ResourceDictionary child in d.MergedDictionaries)
                    if (child != null) stack.Push(child);
            }
            return false;
        }

        private ResourceDictionary Resolve(AppTheme effective)
        {
            if (effective == AppTheme.Dark)
            {
                if (_dark is null && _darkUri != null) _dark = Load(_darkUri);
                return _dark ?? throw new InvalidOperationException("No dictionary is registered for the Dark theme.");
            }
            if (_light is null && _lightUri != null) _light = Load(_lightUri);
            return _light ?? throw new InvalidOperationException("No dictionary is registered for the Light theme.");
        }

        // Loose files are read through a stream we close ourselves: WPF's own loader leaves a file:// source locked for
        // the life of the process, so a theme file could not be edited or replaced while the app runs. Pack URIs (themes
        // compiled into the assembly) go through the normal Source path.
        private static ResourceDictionary Load(Uri uri)
        {
            if (!uri.IsAbsoluteUri || !uri.IsFile) return new ResourceDictionary { Source = uri };
            using var stream = new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            object loaded = XamlReader.Load(stream, new ParserContext { BaseUri = uri });
            return loaded as ResourceDictionary
                ?? throw new InvalidOperationException($"'{uri}' does not contain a ResourceDictionary.");
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return; // app-mode changes arrive as General
            // The manager's STA thread ended: nothing will run what is queued, which piled up for the life of the process.
            if (!_dispatcher!.Thread.IsAlive || _dispatcher.HasShutdownStarted)
            {
                if (_subscribed) { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; _subscribed = false; }
                _followSystem = false;
                Trace.TraceWarning("ThemeManager stopped following the system theme: its dispatcher's thread has ended.");
                return;
            }
            _dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!_disposed && _followSystem && Requested == AppTheme.System) Apply(AppTheme.System);
                }
                catch (Exception ex)
                {
                    EventHandler<Exception>? failed = SystemThemeApplyFailed;
                    if (failed != null) failed(this, ex);
                    else Trace.TraceError("ThemeManager could not follow the system theme: " + ex);
                }
            }));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ThemeManager));
        }

        private static void CheckTheme(AppTheme theme)
        {
            if (theme != AppTheme.Light && theme != AppTheme.Dark && theme != AppTheme.System)
                throw new ArgumentOutOfRangeException(nameof(theme), theme, "Not a defined theme.");
        }

        // App.xaml's Source="Themes/Light.xaml" and a registered pack URI name the same dictionary: compare them resolved
        // against the application's pack base, ignoring case.
        private static bool SameSource(Uri? merged, Uri? registered)
        {
            if (merged is null || registered is null) return false;
            return string.Equals(Absolute(merged), Absolute(registered), StringComparison.OrdinalIgnoreCase);
        }

        private static string Absolute(Uri uri)
        {
            if (uri.IsAbsoluteUri) return uri.AbsoluteUri;
            try { return new Uri(new Uri("pack://application:,,,/"), uri).AbsoluteUri; }
            catch (UriFormatException) { return uri.OriginalString; }
        }

        private static SolidColorBrush Brush(int rgb)
        {
            var b = new SolidColorBrush(Color.FromRgb(unchecked((byte)(rgb >> 16)), unchecked((byte)(rgb >> 8)), unchecked((byte)rgb)));   // checked builds threw
            b.Freeze();
            return b;
        }
    }
}
