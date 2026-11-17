using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 105 review regressions (WinForms, tooling and docs, sixth pass).</summary>
    public sealed class WinFormsReview105Tests
    {
        // ---------- PlotPictureBox ----------

        // The wheel was never marked handled, so it went on to the parent: a scrolling panel scrolled while the plot zoomed.
        public void PlotWheel_IsMarkedHandled()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(10, 10);
                using var pb = new PlotPictureBox { Size = new Size(200, 200), Image = image };
                var wheel = typeof(PlotPictureBox).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var e = new HandledMouseEventArgs(MouseButtons.None, 0, 100, 100, 120);
                wheel.Invoke(pb, new object[] { e });
                Check.True(e.Handled);
            });
        }

        // A ViewChanged handler that re-fits the view (a snap-back rule) was undone: ZoomAt turned auto-fit off again after
        // the event returned, so a resize kept the old zoom.
        public void PlotViewChangedHandler_ThatRefits_KeepsAutoFit()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(100, 100);
                using var pb = new PlotPictureBox { Size = new Size(400, 300), Image = image };
                pb.FitToView();
                Check.Equal(3.0, pb.Zoom);
                bool fitting = false;
                EventHandler snapBack = (_, __) => { if (!fitting && pb.Zoom > 4) { fitting = true; pb.FitToView(); fitting = false; } };
                pb.ViewChanged += snapBack;
                pb.ZoomAt(new PointF(10, 10), 2);
                Check.Equal(3.0, pb.Zoom);
                pb.Size = new Size(800, 600);
                Check.Equal(6.0, pb.Zoom);                                                   // was 3: auto-fit was off

                pb.ViewChanged -= snapBack;
                pb.PanBy(5, 5);                                                             // a plain pan still turns it off
                pb.Size = new Size(400, 300);
                Check.Equal(6.0, pb.Zoom);
            });
        }

        // The plot took capture after the MouseDown handlers ran, so one that released it (as a modal dialog does) left the
        // plot holding capture with no button down.
        public void PlotMouseDownHandler_ThatReleasesCapture_LeavesItReleased()
        {
            RunSta(() =>
            {
                using var form = new Form { ClientSize = new Size(400, 300), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
                var pb = new PlotPictureBox { Dock = DockStyle.Fill };
                form.Controls.Add(pb);
                form.Show();
                Application.DoEvents();
                pb.MouseDown += (_, __) => pb.Capture = false;
                SendMessage(pb.Handle, 0x201 /* WM_LBUTTONDOWN */, (IntPtr)1, (IntPtr)((20 << 16) | 20));
                bool held = pb.Capture;
                object? from = typeof(PlotPictureBox).GetField("_dragFrom", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pb);
                pb.Capture = false;
                form.Close();
                Check.False(held, "capture");
                Check.True(from is null, "drag start");
            });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // ---------- FormStatePersistence ----------

        // The working areas' union overflowed int, so its width went negative and a window straddling two adjacent
        // monitors was moved.
        public void EnsureOnScreen_FarApartAreas_LeaveAStraddlingWindowAlone()
        {
            var areas = new List<Rectangle>
            {
                new Rectangle(int.MinValue, 0, 1000, 1000), new Rectangle(0, 0, 1000, 1000),
                new Rectangle(1000, 0, 1000, 1000), new Rectangle(int.MaxValue - 1000, 0, 1000, 1000),
            };
            var bounds = new Rectangle(900, 100, 200, 200);
            Check.Equal(bounds, FormStatePersistence.EnsureOnScreen(bounds, areas));            // was {800,100,200,200}
        }

        // ---------- MenuBuilder / ToolStripBuilder ----------

        // FindItem read DropDownItems on the leaf it found, which made an empty drop-down: a lookup changed the item.
        public void FindItem_LeavesTheLeafWithoutADropDown()
        {
            RunSta(() =>
            {
                using var strip = new MenuBuilder().Menu("&File", m => m.Item("&Open", () => { })).Build();
                var open = (ToolStripMenuItem)((ToolStripMenuItem)strip.Items[0]).DropDownItems[0];
                Check.False(open.HasDropDown);
                Check.True(ReferenceEquals(open, MenuBuilder.FindItem(strip, "File/Open")));
                Check.False(open.HasDropDown, "after the lookup");
                Check.True(MenuBuilder.FindItem(strip, "File/Open/More") is null);
                Check.False(open.HasDropDown, "after a lookup past it");
            });
        }

        // CanExecute succeeds; subscribing throws after the binding has made its handler, which holds the item weakly, so
        // the test can reach the item that was being set up.
        private sealed class ThrowingSubscription : ICommand
        {
            public ToolStripItem? Item;
            public Func<bool> Disposed = () => false;
            public event EventHandler? CanExecuteChanged
            {
                add
                {
                    var target = value!.Target!;
                    var weak = (WeakReference<ToolStripItem>)target.GetType().GetField("_item", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
                    weak.TryGetTarget(out Item);
                    if (Item != null) Disposed = Watch(Item);
                    throw new InvalidOperationException("subscribe");
                }
                remove { }
            }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { }
        }

        // ToolStripItem.IsDisposed stays false after Dispose on .NET 8 (an item disposed through its owner reads true), so
        // disposal is watched by the Disposed event.
        private static Func<bool> Watch(ToolStripItem item)
        {
            bool disposed = false;
            item.Disposed += (_, __) => disposed = true;
            return () => disposed;
        }

        private static ToolStripItemCollection ItemsOf(MenuItemsBuilder b)
            => (ToolStripItemCollection)typeof(MenuItemsBuilder).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(b)!;

        // A submenu whose callback threw (a bad shortcut) was made but never reached the parent, so it and its dropdown
        // stayed undisposed.
        public void Submenu_ThatThrows_IsDisposed()
        {
            RunSta(() =>
            {
                ToolStripMenuItem? sub = null, inner = null;
                Func<bool> subGone = () => false, innerGone = () => false;
                var builder = new MenuBuilder().Menu("M", m => m.Submenu("S", s =>
                {
                    s.Item("A", () => { });
                    inner = (ToolStripMenuItem)ItemsOf(s)[0];
                    sub = (ToolStripMenuItem)inner.OwnerItem!;
                    subGone = Watch(sub); innerGone = Watch(inner);
                    s.Item("X", () => { }, shortcut: "Ctrl+Bogus");
                }));
                using var strip = new MenuStrip();
                Check.Throws<FormatException>(() => builder.ApplyTo(strip));
                Check.Equal(0, strip.Items.Count);
                Check.True(subGone(), "submenu");
                Check.True(innerGone(), "its item");
            });
        }

        // A command item whose binding threw was made but never added, so it stayed undisposed.
        public void CommandItem_ThatThrows_IsDisposed()
        {
            RunSta(() =>
            {
                var command = new ThrowingSubscription();
                var builder = new MenuBuilder().Menu("M", m => m.Item("C", command));
                using var strip = new MenuStrip();
                Check.Throws<InvalidOperationException>(() => builder.ApplyTo(strip));
                Check.Equal(0, strip.Items.Count);
                Check.True(command.Item is not null && command.Disposed(), "command item");
            });
        }

        // A dropdown button whose items callback threw, and a button whose command binding threw, were left undisposed:
        // created[i] is set only once the factory returns.
        public void ToolStripFactories_ThatThrow_DisposeTheirItem()
        {
            RunSta(() =>
            {
                ToolStripItem? dd = null;
                Func<bool> ddGone = () => false;
                var builder = new ToolStripBuilder().DropDown("D", m =>
                {
                    m.Item("A", () => { });
                    dd = ItemsOf(m)[0].OwnerItem;
                    ddGone = Watch(dd!);
                    throw new InvalidOperationException("items");
                });
                using var strip = new ToolStrip();
                Check.Throws<InvalidOperationException>(() => builder.ApplyTo(strip));
                Check.True(dd is ToolStripDropDownButton && ddGone(), "dropdown button");

                var command = new ThrowingSubscription();
                Check.Throws<InvalidOperationException>(() => new ToolStripBuilder().Button("B", command).ApplyTo(strip));
                Check.Equal(0, strip.Items.Count);
                Check.True(command.Item is ToolStripButton && command.Disposed(), "command button");
            });
        }

        // ---------- README ----------

        // The discovery table named Identifiers.Ulid, which is not a type (the type is UlidGenerator). Each Namespace.Type
        // name in a discovery row must be a declared type, and each name in parentheses a member found in the sources.
        public void Readme_DiscoveryNamesExist()
        {
            string root = RepoRoot();
            var sources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Select(File.ReadAllText).ToArray();
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (string s in sources)
                foreach (Match m in Regex.Matches(s, @"\b(?:class|struct|enum|interface|record)\s+([A-Z]\w*)"))
                    types.Add(m.Groups[1].Value);
            var files = new HashSet<string>(Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetFileNameWithoutExtension(f)!), StringComparer.Ordinal);
            var bad = new List<string>();
            foreach (string line in File.ReadAllLines(Path.Combine(root, "README.md")).Take(150))
            {
                if (!line.StartsWith("|", StringComparison.Ordinal)) continue;
                bool category = line.StartsWith("| **", StringComparison.Ordinal);
                foreach (Match m in Regex.Matches(line, @"`([^`]*)`"))
                {
                    string token = Regex.Replace(m.Groups[1].Value, "<[^>]*>", "");
                    if (!Regex.IsMatch(token, @"^[A-Z]\w*(\.[A-Z]\w*)*$")) continue;
                    string[] segs = token.Split('.');
                    if (category) { if (!files.Contains(token) && !types.Contains(token)) bad.Add(token); continue; }
                    if (segs.Any(types.Contains)) continue;
                    // A bare member name, as in "(Preflight, EstimateBytes)".
                    if (segs.Length == 1 && sources.Any(s => Regex.IsMatch(s, @"\b" + token + @"\s*[(<]"))) continue;
                    bad.Add(token);
                }
            }
            Check.Equal("", string.Join(", ", bad));
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "README.md")) && Directory.Exists(Path.Combine(dir.FullName, "src", "ToolBelt")))
                    return dir.FullName;
            throw new InvalidOperationException("The repository root was not found.");
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
