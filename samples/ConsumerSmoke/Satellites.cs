// The Windows, WPF and WinForms satellites as an external consumer sees them; compiled only for net8.0-windows.
#if WINDOWS_SATELLITES
using System;
using System.Windows.Forms;
using ToolBelt.Windows;
using ToolBelt.WinForms;
using ToolBelt.Wpf;

internal static partial class Program
{
    private static void Satellites()
    {
        Expect("OsVersionInfo is Windows 10+", OsVersionInfo.Get().IsWindows10OrGreater);
        Expect("MemoryStatus total", MemoryStatus.Query().TotalPhysicalBytes > 0);

        int runs = 0;
        var command = new RelayCommand(() => runs++);
        if (command.CanExecute(null)) command.Execute(null);
        Expect("RelayCommand runs", runs == 1);

        Expect("MenuShortcut round-trips", MenuShortcut.Format(MenuShortcut.Parse("Ctrl+Shift+S")) == "Ctrl+Shift+S");
        using (var combo = new ComboBox())
        {
            combo.BindEnum<DayOfWeek>();
            Expect("BindEnum fills the combo", combo.Items.Count == 7);
        }
        ExpectThrows<FormatException>("MenuShortcut bad text", () => MenuShortcut.Parse("Ctrl+Nope"));
    }
}
#endif
