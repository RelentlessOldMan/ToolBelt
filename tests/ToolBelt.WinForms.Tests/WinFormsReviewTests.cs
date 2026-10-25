using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 17 review regressions (WinForms satellite).</summary>
    public sealed class WinFormsReviewTests
    {
        private sealed class TestCommand : ICommand
        {
            public bool Throw;
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => Throw ? throw new InvalidOperationException("CanExecute") : true;
            public void Execute(object? parameter) { }
            public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            public int Subscribers => CanExecuteChanged?.GetInvocationList().Length ?? 0;
        }

        private enum Aliased { First = 1, Primary = 1, Second = 2 }

        private sealed class Row
        {
            public Row(string name) { Name = name; }
            public string Name { get; }
        }

        // ---------- FormStatePersistence ----------

        public void EnsureOnScreen_OversizedBoundsAreShrunkOntoTheScreen()
        {
            var area = new Rectangle(0, 0, 1920, 1040);
            Rectangle r = FormStatePersistence.EnsureOnScreen(new Rectangle(-2400, 100, 4000, 900), new[] { area });
            Check.True(area.Contains(r), $"{r} must lie inside {area}");
            // A window straddling two monitors that fits their union is still left alone.
            var left = new Rectangle(0, 0, 1920, 1040);
            var right = new Rectangle(1920, 0, 1920, 1040);
            var straddle = new Rectangle(1500, 100, 1000, 600);
            Check.Equal(straddle, FormStatePersistence.EnsureOnScreen(straddle, new[] { left, right }));
        }

        public void FormState_Constructor_NormalisesMinimized()
        {
            var s = new FormState(new Rectangle(10, 20, 300, 200), FormWindowState.Minimized);
            Check.Equal(FormWindowState.Normal, s.WindowState);
            Check.True(FormState.TryParse(s.Serialize(), out FormState back));
            Check.Equal(s.Bounds, back.Bounds);
        }

        // ---------- ControlExtensions ----------

        public void ControlExtensions_DisposedControl_IsNotTouchedInline()
        {
            RunSta(() =>
            {
                var c = new Control();
                c.CreateControl();
                c.Dispose();
                int ran = 0;
                c.BeginInvokeIfRequired(() => ran++);
                Check.Equal(0, ran);
                Check.Throws<ObjectDisposedException>(() => c.InvokeIfRequired(() => ran++));
                Check.Throws<ObjectDisposedException>(() => c.InvokeIfRequired(() => ++ran));
                Check.Equal(0, ran);
            });
        }

        // ---------- PlotPictureBox ----------

        public void PlotPictureBox_RejectsBadViewSettings()
        {
            RunSta(() =>
            {
                using var box = new PlotPictureBox();
                foreach (double bad in new[] { 0.0, -1.2, double.NaN, double.PositiveInfinity })
                {
                    Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => box.WheelFactor = bad).ParamName);
                    Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => box.MinZoom = bad).ParamName);
                    Check.Equal("value", Check.Throws<ArgumentOutOfRangeException>(() => box.MaxZoom = bad).ParamName);
                }
                Check.Throws<ArgumentOutOfRangeException>(() => box.PanBy(float.NaN, 0));
                Check.Throws<ArgumentOutOfRangeException>(() => box.PanBy(0, float.PositiveInfinity));
                Check.Equal(1.2, box.WheelFactor);
            });
        }

        public void PlotPictureBox_ZoomAbout_NeverReachesZero()
        {
            var (zoom, _) = PlotPictureBox.ZoomAbout(1e-300, PointF.Empty, new PointF(5, 5), 1e-300);
            Check.True(zoom > 0, $"zoom {zoom}");
        }

        // ---------- WaitCursorScope ----------

        public void WaitCursorScope_OutOfOrderDispose_StillRestores()
        {
            RunSta(() =>
            {
                Application.UseWaitCursor = false;
                var a = WaitCursorScope.Begin();
                var b = WaitCursorScope.Begin();
                a.Dispose();
                Check.True(Application.UseWaitCursor, "b is still open");
                b.Dispose();
                Check.False(Application.UseWaitCursor, "all scopes closed");
            });
        }

        // ---------- MenuBuilder ----------

        public void CommandBinding_ThrowingCanExecute_LeavesNoSubscription()
        {
            RunSta(() =>
            {
                var cmd = new TestCommand { Throw = true };
                Check.Throws<InvalidOperationException>(() => new MenuBuilder().Menu("File", m => m.Item("Go", cmd)).Build());
                Check.Equal(0, cmd.Subscribers);
            });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference BuildBoundItem(ICommand cmd)
        {
            var strip = new MenuBuilder().Menu("File", m => m.Item("Go", cmd)).Build();
            return new WeakReference(strip.Items[0]);
        }

        public void CommandBinding_LongLivedCommand_DoesNotKeepItemsAlive()
        {
            RunSta(() =>
            {
                var cmd = new TestCommand();
                WeakReference item = BuildBoundItem(cmd);   // built, never disposed (e.g. a closed modal dialog)
                for (int i = 0; i < 3 && item.IsAlive; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                Check.False(item.IsAlive, "the command must not root the menu");
                cmd.Raise();                                // a dead binding is harmless
            });
        }

        public void PublicTypes_DoNotClashWithWinFormsTypeNames()
        {
            var winForms = new HashSet<string>(typeof(Form).Assembly.GetExportedTypes()
                .Where(t => t.Namespace == "System.Windows.Forms").Select(t => t.Name));
            string[] clashes = typeof(PlotPictureBox).Assembly.GetExportedTypes()
                .Where(t => winForms.Contains(t.Name)).Select(t => t.FullName!).ToArray();
            Check.Equal("", string.Join(",", clashes));
        }

        public void Shortcut_RejectsWhatAMenuItemRejects()
        {
            foreach (string bad in new[] { "A", "Shift+A", "Esc", "Ctrl+A,B", "Ctrl+LButton", "Alt+XButton1", "Ctrl+None" })
                Check.False(MenuShortcut.TryParse(bad, out _), $"'{bad}'");
            foreach (string good in new[] { "F5", "Del", "Insert", "Shift+F3", "Ctrl+Shift+A", "Alt+Del" })
                Check.True(MenuShortcut.TryParse(good, out _), $"'{good}'");
            RunSta(() => Check.Throws<FormatException>(() => new MenuBuilder().Menu("File", m => m.Item("X", () => { }, "A")).Build()));
        }

        public void Shortcut_FormatUsesFriendlyNamesThatParseBack()
        {
            Check.Equal("Ctrl+PageDown", MenuShortcut.Format(Keys.Control | Keys.PageDown));
            Check.Equal("Ctrl+PageUp", MenuShortcut.Format(Keys.Control | Keys.PageUp));
            Check.Equal("Ctrl+Enter", MenuShortcut.Format(Keys.Control | Keys.Enter));
            Check.Equal("Ctrl+/", MenuShortcut.Format(Keys.Control | Keys.OemQuestion));
            Check.Equal("Ctrl+,", MenuShortcut.Format(Keys.Control | Keys.Oemcomma));
            foreach (Keys k in new[] { Keys.PageDown, Keys.PageUp, Keys.Enter, Keys.OemQuestion, Keys.Oemcomma, Keys.OemPeriod,
                Keys.OemSemicolon, Keys.OemQuotes, Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe, Keys.Oemtilde,
                Keys.Back, Keys.Capital, Keys.Oemplus, Keys.OemMinus, Keys.D7, Keys.F12 })
            {
                Keys combo = Keys.Control | k;
                Check.Equal(combo, MenuShortcut.Parse(MenuShortcut.Format(combo)));
            }
        }

        // ---------- DataGridViewExtensions ----------

        public void AutoSizeColumns_CapHoldsWhenTheGridAutoSizesContinuously()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { AllowUserToAddRows = false, Dock = DockStyle.Fill };
                form.Controls.Add(grid);
                form.CreateControl();
                grid.BindList(new[] { new Row(new string('W', 200)) });
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
                grid.AutoSizeColumns(maxWidth: 30);
                Check.True(grid.Columns.Cast<DataGridViewColumn>().All(c => c.Width <= 30),
                    string.Join(",", grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Width)));
            });
        }

        public void SaveCsv_NullGrid_LeavesTheFileAlone()
        {
            string path = Path.Combine(Path.GetTempPath(), "tbwf_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(path, "precious");
            try
            {
                Check.Throws<ArgumentNullException>(() => ((DataGridView)null!).SaveCsv(path));
                Check.Equal("precious", File.ReadAllText(path));
            }
            finally { File.Delete(path); }
        }

        // ---------- ComboBoxEnumExtensions ----------

        public void BindEnum_AliasedValuesAppearOnce()
        {
            RunSta(() =>
            {
                using var combo = new ComboBox();
                combo.BindEnum<Aliased>();
                Check.Equal(2, combo.Items.Count);
            });
        }

        public void SetSelectedEnum_ValueNotInTheList_ClearsTheSelection()
        {
            RunSta(() =>
            {
                using var combo = new ComboBox();
                combo.BindEnum<DayOfWeek>();
                combo.SetSelectedEnum(DayOfWeek.Tuesday);
                combo.SetSelectedEnum((DayOfWeek)42);
                Check.Null(combo.GetSelectedEnum<DayOfWeek>());
            });
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
