using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class MenuBuilderTests
    {
        private sealed class TestCommand : ICommand
        {
            public bool Allowed = true;
            public readonly List<object?> Executed = new List<object?>();
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => Allowed;
            public void Execute(object? parameter) => Executed.Add(parameter);
            public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            public int Subscribers => CanExecuteChanged?.GetInvocationList().Length ?? 0;
        }

        public void Shortcut_ParseAndFormat()
        {
            Check.Equal(Keys.Control | Keys.O, Shortcut.Parse("Ctrl+O"));
            Check.Equal(Keys.Control | Keys.Shift | Keys.S, Shortcut.Parse(" shift + control + s "));
            Check.Equal(Keys.F5, Shortcut.Parse("F5"));
            Check.Equal(Keys.Control | Keys.D1, Shortcut.Parse("Ctrl+1"));
            Check.Equal(Keys.Control | Keys.Oemplus, Shortcut.Parse("Ctrl++"));
            Check.Equal(Keys.Alt | Keys.Delete, Shortcut.Parse("Alt+Del"));
            Check.Equal("Ctrl+Shift+S", Shortcut.Format(Keys.Control | Keys.Shift | Keys.S));
            Check.Equal("Ctrl+1", Shortcut.Format(Keys.Control | Keys.D1));
            foreach (string bad in new[] { "", "Ctrl+", "Hyper+K", "Ctrl+Shift", "Ctrl+65", "Ctrl+Nope" })
                Check.False(Shortcut.TryParse(bad, out _), $"'{bad}'");
        }

        public void PlainText_StripsMnemonics()
        {
            Check.Equal("Save As...", MenuBuilder.PlainText("Save &As..."));
            Check.Equal("R&D", MenuBuilder.PlainText("R&&D"));
        }

        public void Menu_BuildsFindsAndClicks()
        {
            RunSta(() =>
            {
                var log = new List<string>();
                using MenuStrip menu = new MenuBuilder()
                    .Menu("&File", m => m
                        .Item("&Open...", () => log.Add("open"), "Ctrl+O")
                        .Submenu("&Recent", r => r.Item("report.csv", () => log.Add("recent")))
                        .Separator()
                        .Item("E&xit", () => log.Add("exit")))
                    .Menu("&View", m => m.Check("&Grid", true, on => log.Add("grid=" + on)))
                    .Build();

                Check.Equal(2, menu.Items.Count);
                var open = (ToolStripMenuItem)MenuBuilder.FindItem(menu, "File/Open...")!;
                Check.Equal(Keys.Control | Keys.O, open.ShortcutKeys);
                Check.Equal("Ctrl+O", open.ShortcutKeyDisplayString);
                Check.Equal("Open...", open.Name);
                open.PerformClick();
                MenuBuilder.FindItem(menu, "file/recent/REPORT.CSV")!.PerformClick();   // case-insensitive path
                var grid = (ToolStripMenuItem)MenuBuilder.FindItem(menu, "View/Grid")!;
                Check.True(grid.Checked);
                grid.PerformClick();
                Check.Equal("open,recent,grid=False", string.Join(",", log));
                Check.Equal(4, ((ToolStripMenuItem)menu.Items[0]).DropDownItems.Count);   // incl. separator
                Check.Null(MenuBuilder.FindItem(menu, "File/Missing"));
                Check.Null(MenuBuilder.FindItem(menu, "File/Exit/Deeper"));
            });
        }

        public void CommandItems_ExecuteAndTrackCanExecute()
        {
            RunSta(() =>
            {
                var cmd = new TestCommand();
                MenuStrip menu = new MenuBuilder().Menu("Edit", m => m.Item("Delete", cmd, parameter: 42, shortcut: "Del")).Build();
                ToolStripItem delete = MenuBuilder.FindItem(menu, "Edit/Delete")!;
                Check.True(delete.Enabled);
                delete.PerformClick();
                Check.Equal(42, (int)cmd.Executed[0]!);

                cmd.Allowed = false;
                cmd.Raise();
                Check.False(delete.Enabled);
                Check.Equal(1, cmd.Subscribers);
                menu.Dispose();                                                         // unhooks from the command
                Check.Equal(0, cmd.Subscribers);
            });
        }

        public void Toolbar_ButtonsTogglesDropDowns()
        {
            RunSta(() =>
            {
                var log = new List<string>();
                var cmd = new TestCommand();
                using ToolStrip bar = new ToolStripBuilder()
                    .Button("Run", () => log.Add("run"), toolTip: "Run the job")
                    .Button("Stop", cmd)
                    .Separator()
                    .Toggle("Live", false, on => log.Add("live=" + on))
                    .Label("Status")
                    .DropDown("More", m => m.Item("Export", () => log.Add("export")))
                    .Build();
                Check.Equal(6, bar.Items.Count);
                Check.Equal("Run the job", bar.Items["Run"]!.ToolTipText);
                bar.Items["Run"]!.PerformClick();
                bar.Items["Stop"]!.PerformClick();
                bar.Items["Live"]!.PerformClick();
                MenuBuilder.FindItem(bar, "More/Export")!.PerformClick();
                Check.Equal("run,live=True,export", string.Join(",", log));
                Check.Equal(1, cmd.Executed.Count);
            });
        }

        public void Validation()
        {
            Check.Throws<FormatException>(() => Shortcut.Parse("Ctrl+"));
            Check.Throws<ArgumentNullException>(() => new MenuBuilder().Menu("x", null!));
            Check.Throws<ArgumentNullException>(() => new ToolStripBuilder().Button("x", (Action)null!));
            RunSta(() => Check.Throws<ArgumentNullException>(() => new ToolStripBuilder().Button("x", (ICommand)null!)));
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
