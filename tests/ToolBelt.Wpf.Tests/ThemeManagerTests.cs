using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class ThemeManagerTests
    {
        private static ResourceDictionary Dict(string key, object value) => new ResourceDictionary { [key] = value };

        public void RegistryValueInterpretation()
        {
            Check.Equal(AppTheme.Dark, ThemeManager.ThemeFromRegistryValue(0));
            Check.Equal(AppTheme.Light, ThemeManager.ThemeFromRegistryValue(1));
            Check.Equal(AppTheme.Light, ThemeManager.ThemeFromRegistryValue(null));
            Check.Equal(AppTheme.Light, ThemeManager.ThemeFromRegistryValue("0"));     // wrong type: not trusted
            AppTheme sys = ThemeManager.GetSystemTheme();
            Check.True(sys == AppTheme.Light || sys == AppTheme.Dark);
        }

        public void Apply_SwapsInPlaceAndKeepsOtherDictionaries()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var target = new ResourceDictionary();
                var baseDict = Dict("Base", 1);
                target.MergedDictionaries.Add(baseDict);
                var light = Dict("Background", Brushes.White);
                var dark = Dict("Background", Brushes.Black);
                var events = new List<AppTheme>();

                var tm = new ThemeManager(target).Register(AppTheme.Light, light).Register(AppTheme.Dark, dark);
                tm.ThemeChanged += (_, t) => events.Add(t);
                Check.Null(tm.Effective);

                tm.Apply(AppTheme.Dark);
                Check.True(ReferenceEquals(Brushes.Black, target["Background"]));
                Check.Equal(2, target.MergedDictionaries.Count);

                target.MergedDictionaries.Add(Dict("Later", 2));                    // added after the theme
                tm.Apply(AppTheme.Light);
                Check.True(ReferenceEquals(Brushes.White, target["Background"]));
                Check.Equal(3, target.MergedDictionaries.Count);
                Check.True(ReferenceEquals(baseDict, target.MergedDictionaries[0]));
                Check.True(ReferenceEquals(light, target.MergedDictionaries[1]));    // replaced in place
                Check.Equal(2, target["Later"]);

                tm.Apply(AppTheme.Light);                                          // no-op: no event
                Check.Equal("Dark,Light", string.Join(",", events));
                Check.Equal(AppTheme.Light, tm.Effective);
            });
        }

        public void Apply_SystemResolvesToTheWindowsSetting()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var tm = new ThemeManager(new ResourceDictionary())
                    .Register(AppTheme.Light, Dict("K", "light")).Register(AppTheme.Dark, Dict("K", "dark"));
                tm.Apply(AppTheme.System);
                Check.Equal(AppTheme.System, tm.Requested);
                Check.Equal(ThemeManager.GetSystemTheme(), tm.Effective);
                tm.FollowSystem = true;                                            // subscribes without throwing
                tm.Dispose();
                Check.Throws<ObjectDisposedException>(() => tm.Apply(AppTheme.Light));
            });
        }

        public void Register_FromXamlUri()
        {
            string path = Path.Combine(Path.GetTempPath(), "toolbelt-theme-" + Guid.NewGuid().ToString("N") + ".xaml");
            File.WriteAllText(path,
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
                + "xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"><sys:String x:Key=\"Name\" "
                + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">Dark from XAML</sys:String></ResourceDictionary>");
            try
            {
                FileDropBehaviorTests.RunSta(() =>
                {
                    var target = new ResourceDictionary();
                    var tm = new ThemeManager(target).Register(AppTheme.Dark, new Uri(path));
                    tm.Apply(AppTheme.Dark);
                    Check.Equal("Dark from XAML", target["Name"]);
                });
            }
            finally { File.Delete(path); }
        }

        public void UnregisteredThemeAndBadRegistrations()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var tm = new ThemeManager(new ResourceDictionary()).Register(AppTheme.Light, Dict("K", 1));
                Check.Throws<InvalidOperationException>(() => tm.Apply(AppTheme.Dark));
                Check.Throws<ArgumentException>(() => tm.Register(AppTheme.System, new ResourceDictionary()));
                Check.Throws<ArgumentNullException>(() => new ThemeManager(null!));
            });
        }

        public void DefaultPalette_HasFrozenBrushesThatDiffer()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                var light = ThemeManager.CreateDefaultPalette(AppTheme.Light);
                var dark = ThemeManager.CreateDefaultPalette(AppTheme.Dark);
                foreach (string key in new[] { "ThemeBackgroundBrush", "ThemeSurfaceBrush", "ThemeForegroundBrush",
                                               "ThemeMutedForegroundBrush", "ThemeBorderBrush", "ThemeAccentBrush" })
                {
                    var l = (SolidColorBrush)light[key];
                    var d = (SolidColorBrush)dark[key];
                    Check.True(l.IsFrozen && d.IsFrozen, key);
                    Check.False(l.Color == d.Color, key);
                }
                Check.Equal(Colors.White, ((SolidColorBrush)light["ThemeBackgroundBrush"]).Color);
            });
        }
    }
}
