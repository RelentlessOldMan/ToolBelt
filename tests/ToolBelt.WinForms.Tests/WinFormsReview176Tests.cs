using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 176 review regressions (WinForms, tooling and docs, tenth pass).</summary>
    public sealed class WinFormsReview176Tests
    {
        // ---------- PlotPictureBox ----------

        // A right, middle or X release during a left-button drag ended the pan: OnMouseUp cleared it for any button, and
        // WinForms' release of capture after that button-up ended it again in OnMouseCaptureChanged.
        public void PlotPictureBox_OtherButtonReleaseDuringADrag_KeepsThePan()
        {
            RunSta(() =>
            {
                using var form = new Form { ClientSize = new Size(400, 300), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
                using var image = new Bitmap(100, 100);
                var pb = new PlotPictureBox { Dock = DockStyle.Fill, Image = image };
                form.Controls.Add(pb);
                form.Show();
                Application.DoEvents();
                var rng = new Random(1761);
                // (down, up, MK_ flag, X button in the high word)
                var buttons = new[] { (0x204, 0x205, 0x02, 0), (0x207, 0x208, 0x10, 0), (0x20B, 0x20C, 0x20, 1), (0x20B, 0x20C, 0x40, 2) };
                for (int trial = 0; trial < 200; trial++)
                {
                    var (down, up, flag, x) = buttons[rng.Next(buttons.Length)];
                    bool leftHeld = rng.Next(4) != 0;
                    int sx = rng.Next(10, 200), sy = rng.Next(10, 200), dx = rng.Next(-50, 51), dy = rng.Next(-50, 51);
                    IntPtr at = (IntPtr)((sy << 16) | sx);
                    SendMessage(pb.Handle, 0x201 /* WM_LBUTTONDOWN */, (IntPtr)1, at);
                    SendMessage(pb.Handle, down, (IntPtr)((x << 16) | 1 | flag), at);
                    SendMessage(pb.Handle, up, (IntPtr)((x << 16) | (leftHeld ? 1 : 0)), at);
                    string shown = "button 0x" + up.ToString("X") + (leftHeld ? " with the left held" : " with the left up");
                    Check.Equal(leftHeld, pb.Capture, "capture after the release of " + shown);
                    PointF before = pb.Offset;
                    Invoke(pb, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, sx + dx, sy + dy, 0));
                    PointF expected = leftHeld ? new PointF(before.X + dx, before.Y + dy) : before;
                    Check.Equal(expected, pb.Offset, shown);
                    SendMessage(pb.Handle, 0x202 /* WM_LBUTTONUP */, IntPtr.Zero, at);
                    Check.False(pb.Capture, "capture after the left release");
                    Check.True(DragFrom(pb) is null, "drag after the left release");
                }
                pb.Capture = false;
                form.Close();
            });
        }

        // ZoomAt(p, 1) with the zoom above a lowered MaxZoom (or below a raised MinZoom) moved the view to the limit and
        // raised ViewChanged: a factor of 1 has no direction, so it must leave the view as it is.
        public void PlotPictureBox_ZoomByOne_NeverMovesTheView()
        {
            RunSta(() =>
            {
                var rng = new Random(1762);
                for (int trial = 0; trial < 300; trial++)
                {
                    using var pb = new PlotPictureBox { Size = new Size(200, 150), MinZoom = 0.01, MaxZoom = 100 };
                    pb.ZoomAt(new PointF(rng.Next(200), rng.Next(150)), Math.Pow(10, rng.NextDouble() * 3 - 1.5));
                    double a = Math.Pow(10, rng.NextDouble() * 4 - 2), b = Math.Pow(10, rng.NextDouble() * 4 - 2);
                    pb.MinZoom = Math.Min(a, b);
                    pb.MaxZoom = Math.Max(a, b);
                    double zoom = pb.Zoom;
                    PointF offset = pb.Offset;
                    int changed = 0;
                    pb.ViewChanged += (_, __) => changed++;
                    pb.ZoomAt(new PointF(rng.Next(200), rng.Next(150)), 1);
                    string shown = "zoom " + zoom + " with limits " + pb.MinZoom + ".." + pb.MaxZoom;
                    Check.Equal(zoom, pb.Zoom, shown);
                    Check.Equal(offset, pb.Offset, shown);
                    Check.Equal(0, changed, shown);
                    var (z, o) = PlotPictureBox.ZoomAbout(zoom, offset, new PointF(rng.Next(200), rng.Next(150)), 1, pb.MinZoom, pb.MaxZoom);
                    Check.Equal(zoom, z, shown);
                    Check.Equal(offset, o, shown);
                }
            });
        }

        // ---------- ComboBoxEnumExtensions ----------

        // In an editable box with nothing selected, SetSelectedEnum with a value not in the list left the typed text in the
        // box while GetSelectedEnum returned null.
        public void SetSelectedEnum_ClearsTypedTextWhenNothingMatches()
        {
            RunSta(() =>
            {
                using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
                var combo = new ComboBox();
                form.Controls.Add(combo);
                form.Show();
                combo.BindEnum<DayOfWeek>();
                var rng = new Random(1763);
                var styles = new[] { ComboBoxStyle.DropDown, ComboBoxStyle.Simple, ComboBoxStyle.DropDownList };
                for (int trial = 0; trial < 400; trial++)
                {
                    combo.DropDownStyle = styles[rng.Next(styles.Length)];
                    if (rng.Next(2) == 0) combo.SelectedIndex = rng.Next(-1, 7);
                    if (combo.DropDownStyle != ComboBoxStyle.DropDownList && rng.Next(2) == 0) combo.Text = "typed" + rng.Next(10);
                    var value = (DayOfWeek)rng.Next(-2, 10);
                    bool listed = Enum.IsDefined(value);
                    combo.SetSelectedEnum(value);
                    string shown = combo.DropDownStyle + " set to " + value;
                    Check.Equal(listed ? value : (DayOfWeek?)null, combo.GetSelectedEnum<DayOfWeek>(), shown);
                    Check.Equal(listed ? value.ToString() : "", combo.Text, shown);
                }
                form.Close();
            });
        }

        // ---------- DataGridViewExtensions ----------

        // On a grid whose AutoSizeColumnsMode is Fill, AutoSizeColumns sized nothing to content: the fill weights won, so
        // the widths stayed as filled and a cap pinned every column at the cap, the narrow ones too.
        public void AutoSizeColumns_OnAFillGrid_SizesToContent()
        {
            RunSta(() =>
            {
                using var form = new Form { ClientSize = new Size(600, 300), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
                var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
                var reference = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
                form.Controls.Add(grid);
                form.Controls.Add(reference);
                form.Show();
                var rng = new Random(1764);
                for (int trial = 0; trial < 40; trial++)
                {
                    int columns = rng.Next(1, 5), rows = rng.Next(1, 6);
                    string[,] cells = new string[rows, columns];
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < columns; c++)
                            cells[r, c] = new string('x', rng.Next(1, 40));
                    Fill(grid, cells, DataGridViewAutoSizeColumnsMode.Fill, fillColumn: rng.Next(-1, columns));
                    Fill(reference, cells, DataGridViewAutoSizeColumnsMode.None, fillColumn: -1);
                    int? cap = rng.Next(2) == 0 ? null : rng.Next(30, 200);
                    grid.AutoSizeColumns(cap);
                    // The reference: plain content sizing on a grid that never filled, then the cap.
                    reference.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
                    if (cap is int m)
                        foreach (DataGridViewColumn c in reference.Columns)
                            c.Width = Math.Min(c.Width, Math.Max(m, c.MinimumWidth));
                    Application.DoEvents();
                    string widths = string.Join(",", grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Width));
                    string expected = string.Join(",", reference.Columns.Cast<DataGridViewColumn>().Select(c => c.Width));
                    Check.Equal(expected, widths, "cap " + (cap?.ToString() ?? "none"));
                }
                form.Close();
            });
        }

        private static void Fill(DataGridView grid, string[,] cells, DataGridViewAutoSizeColumnsMode mode, int fillColumn)
        {
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.Rows.Clear();
            grid.Columns.Clear();
            for (int c = 0; c < cells.GetLength(1); c++)
                grid.Columns.Add("c" + c, "H" + c);
            if (fillColumn >= 0) grid.Columns[fillColumn].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            for (int r = 0; r < cells.GetLength(0); r++)
                grid.Rows.Add(Enumerable.Range(0, cells.GetLength(1)).Select(c => (object)cells[r, c]).ToArray());
            grid.AutoSizeColumnsMode = mode;
        }

        private static object? DragFrom(PlotPictureBox pb)
            => typeof(PlotPictureBox).GetField("_dragFrom", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pb);

        private static void Invoke(Control control, string method, EventArgs e)
            => control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { e });

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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
