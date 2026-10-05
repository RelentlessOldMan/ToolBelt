// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.IO;
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
    /// (marshalled to the dispatcher of the thread that created the manager). <see cref="CreateDefaultPalette"/>
    /// offers a minimal starter set of brushes. Dispose to stop listening for system changes.
    /// </summary>
    public sealed class ThemeManager : IDisposable
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private readonly ResourceDictionary _target;
        private readonly Dispatcher _dispatcher;
        private ResourceDictionary? _light, _dark, _applied;
        private Uri? _lightUri, _darkUri;
        private bool _followSystem, _subscribed, _disposed;

        public ThemeManager(ResourceDictionary target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _dispatcher = Dispatcher.CurrentDispatcher;
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

        /// <summary>Re-apply <see cref="AppTheme.System"/> when the Windows setting changes.</summary>
        public bool FollowSystem
        {
            get => _followSystem;
            set
            {
                ThrowIfDisposed();
                _followSystem = value;
                if (value && !_subscribed) { SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; _subscribed = true; }
                else if (!value && _subscribed) { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; _subscribed = false; }
            }
        }

        /// <summary>Registers the dictionary for <see cref="AppTheme.Light"/> or <see cref="AppTheme.Dark"/>.</summary>
        public ThemeManager Register(AppTheme theme, ResourceDictionary dictionary)
        {
            if (dictionary is null) throw new ArgumentNullException(nameof(dictionary));
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
            Requested = theme;
            AppTheme effective = theme == AppTheme.System ? GetSystemTheme() : theme;
            ResourceDictionary dictionary = Resolve(effective);
            if (ReferenceEquals(dictionary, _applied)) return;

            var merged = _target.MergedDictionaries;
            int index = _applied is null ? -1 : merged.IndexOf(_applied);
            if (index >= 0) merged[index] = dictionary;
            else merged.Add(dictionary);
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

        /// <summary>Interprets the <c>AppsUseLightTheme</c> registry value: 0 means dark; anything else (or missing) light.</summary>
        public static AppTheme ThemeFromRegistryValue(object? value) => value is int i && i == 0 ? AppTheme.Dark : AppTheme.Light;

        /// <summary>
        /// A minimal starter palette with frozen brushes under the keys <c>ThemeBackgroundBrush</c>,
        /// <c>ThemeSurfaceBrush</c>, <c>ThemeForegroundBrush</c>, <c>ThemeMutedForegroundBrush</c>, <c>ThemeBorderBrush</c>
        /// and <c>ThemeAccentBrush</c> — reference them with <c>DynamicResource</c>.
        /// </summary>
        public static ResourceDictionary CreateDefaultPalette(AppTheme theme)
        {
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
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_disposed && _followSystem && Requested == AppTheme.System) Apply(AppTheme.System);
            }));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ThemeManager));
        }

        private static SolidColorBrush Brush(int rgb)
        {
            var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            return b;
        }
    }
}
