using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 88 review regressions (WinForms satellite, fifth pass).</summary>
    public sealed class WinFormsReview88Tests
    {
        public void MenuShortcut_SidedModifierKeysAreRejected()
        {
            foreach (string s in new[] { "Ctrl+LShiftKey", "Ctrl+RShiftKey", "Alt+LControlKey", "Alt+RControlKey", "Ctrl+LMenu", "Ctrl+RMenu" })
                Check.False(MenuShortcut.TryParse(s, out _), s);                       // all parsed; Windows never delivers them as the key
            Check.Throws<FormatException>(() => MenuShortcut.Parse("Ctrl+LShiftKey"));
            Check.Equal(Keys.Control | Keys.Shift | Keys.S, MenuShortcut.Parse("Ctrl+Shift+S"));
        }

        public void MenuShortcut_FormatWithNoKeyCode_WritesOnlyTheModifiers()
        {
            Check.Equal("", MenuShortcut.Format(Keys.None));                            // was "None"
            Check.Equal("Ctrl", MenuShortcut.Format(Keys.Control));                     // was "Ctrl+None"
            Check.Equal("Ctrl+Shift", MenuShortcut.Format(Keys.Control | Keys.Shift));
            Check.Equal("Alt+F4", MenuShortcut.Format(Keys.Alt | Keys.F4));
        }

        public void AutoSizeColumns_HeaderModeWithHiddenHeaders_StillCaps()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { ColumnHeadersVisible = false };
                form.Controls.Add(grid);
                grid.Columns.Add("a", "a");
                grid.Columns[0].Width = 300;
                grid.AutoSizeColumns(100, DataGridViewAutoSizeColumnsMode.ColumnHeader); // threw InvalidOperationException
                Check.Equal(100, grid.Columns[0].Width);
            });
        }

        public void EnsureOnScreen_EmptyOrOverflowingWorkingAreasAreIgnored()
        {
            var primary = new Rectangle(0, 0, 1920, 1040);
            var lost = new Rectangle(6000, 6000, 800, 600);
            // The empty area is nearest by centre: the window came back 0×0 there.
            Check.Equal(new Rectangle(1120, 440, 800, 600),
                FormStatePersistence.EnsureOnScreen(lost, new[] { new Rectangle(5000, 5000, 0, 0), primary }));
            Check.Equal(new Rectangle(1120, 440, 800, 600),
                FormStatePersistence.EnsureOnScreen(lost, new[] { new Rectangle(5600, 5600, -100, 500), primary })); // was −100 wide
            // An area whose Right passes int.MaxValue: Right wrapped negative and the window was clamped off it.
            Check.Equal(new Rectangle(1120, 440, 800, 600),
                FormStatePersistence.EnsureOnScreen(lost, new[] { new Rectangle(int.MaxValue - 100, 6000, 1000, 1000), primary }));
            // Nothing usable: unchanged, as with no areas at all.
            Check.Equal(lost, FormStatePersistence.EnsureOnScreen(lost, new[] { Rectangle.Empty }));
        }

        public void BindEnum_ReplacesADataSource()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var combo = new ComboBox();
                form.Controls.Add(combo);
                combo.DataSource = new[] { "a", "b" };
                combo.BindEnum<DayOfWeek>();                                            // threw ArgumentException
                Check.True(combo.DataSource is null);
                Check.Equal(7, combo.Items.Count);
                Check.Equal(DayOfWeek.Sunday, (DayOfWeek)combo.Items[0]!);
            });
        }

        public void PlotPictureBox_LostCaptureEndsTheDrag()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(100, 100);
                using var pb = new PlotPictureBox { Size = new Size(100, 100), MaxZoom = 4, Image = image };
                pb.ZoomAt(new PointF(50, 50), 4);
                PointF start = pb.ContentToView(new PointF(50, 50));
                Invoke(pb, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
                Invoke(pb, "OnMouseCaptureChanged", EventArgs.Empty);                  // Alt+Tab: no MouseUp follows
                Invoke(pb, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 60, 70, 0));
                Check.Equal(start, pb.ContentToView(new PointF(50, 50)));               // panned by (50, 60) from the stale start
            });
        }

        public void MenuItem_ANullCommandIsCheckedBeforeTheShortcut()
        {
            var ex = Check.Throws<ArgumentNullException>(() =>
                new MenuBuilder().Menu("File", m => m.Item("Open", (System.Windows.Input.ICommand)null!, shortcut: "Ctrl+Nope")).Build());
            Check.Equal("command", ex.ParamName);                                       // was FormatException for the shortcut
        }

        public void MenuBuilder_ApplyToAddsNothingWhenAMenuThrows()
        {
            using var strip = new MenuStrip();
            var builder = new MenuBuilder()
                .Menu("File", m => m.Item("Open", () => { }))
                .Menu("Edit", m => m.Item("Bad", () => { }, shortcut: "Ctrl+Nope"));
            Check.Throws<FormatException>(() => builder.ApplyTo(strip));
            Check.Equal(0, strip.Items.Count);                                          // "File" stayed on the strip
        }

        public void ToolStripBuilder_ApplyToDisposesWhatItMadeWhenAnItemThrows()
        {
            var good = new Command();
            var bad = new Command { Throw = true };
            var builder = new ToolStripBuilder().Button("A", good).Button("B", bad);
            using var strip = new ToolStrip();
            Check.Throws<InvalidOperationException>(() => builder.ApplyTo(strip));
            Check.Equal(0, strip.Items.Count);
            Check.Equal(0, good.Subscribers);                                           // "A" was left undisposed, still subscribed
        }

        public void CommandBinding_PrunesOutsideItsLock()
        {
            // The command's remove accessor binds the command again from another thread and waits for it; under the
            // binding's lock that waited forever.
            var command = new ReentrantCommand();
            BindAndDrop(command);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            command.OnRemove = () =>
            {
                var t = new Thread(() => new MenuBuilder().Menu("M", m => m.Item("Other", command)).Build().Dispose()) { IsBackground = true };
                t.Start();
                return t.Join(TimeSpan.FromSeconds(5));
            };
            using MenuStrip strip = new MenuBuilder().Menu("M", m => m.Item("Again", command)).Build();
            Check.True(command.RemoveRan, "no dead subscription was pruned");
            Check.True(command.OtherThreadFinished, "the other thread's Bind waited on the lock");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void BindAndDrop(System.Windows.Input.ICommand command)
            => new MenuBuilder().Menu("M", m => m.Item("Dropped", command)).Build(); // never disposed, so its subscription only dies with it

        private static void Invoke(Control control, string method, EventArgs e)
            => control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { e });

        private sealed class Command : System.Windows.Input.ICommand
        {
            private EventHandler? _changed;
            public bool Throw;
            public event EventHandler? CanExecuteChanged { add => _changed += value; remove => _changed -= value; }
            public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
            public bool CanExecute(object? parameter) => Throw ? throw new InvalidOperationException("CanExecute") : true;
            public void Execute(object? parameter) { }
        }

        private sealed class ReentrantCommand : System.Windows.Input.ICommand
        {
            private EventHandler? _changed;
            public Func<bool>? OnRemove;
            public volatile bool RemoveRan, OtherThreadFinished;
            public event EventHandler? CanExecuteChanged
            {
                add => _changed += value;
                remove
                {
                    _changed -= value;
                    Func<bool>? once = Interlocked.Exchange(ref OnRemove, null);
                    if (once is null) return;
                    RemoveRan = true;
                    OtherThreadFinished = once();
                }
            }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { }
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
