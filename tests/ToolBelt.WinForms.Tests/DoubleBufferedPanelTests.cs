using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class DoubleBufferedPanelTests
    {
        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }

        public void Styles_AreFlickerFree()
        {
            RunSta(() =>
            {
                using var panel = new DoubleBufferedPanel();
                Check.True(panel.IsFlickerFree);
                Check.True(panel.IsDoubleBuffered(), "DoubleBuffered property set too");
                Check.True(panel.AntiAlias);
            });
        }

        public void Render_DrawsIntoTheControl()
        {
            RunSta(() =>
            {
                using var panel = new DoubleBufferedPanel { Width = 40, Height = 30, BackColor = Color.White, AntiAlias = false };
                Rectangle seen = Rectangle.Empty;
                panel.Render = (g, r) =>
                {
                    seen = r;
                    using var brush = new SolidBrush(Color.FromArgb(255, 10, 200, 30));
                    g.FillRectangle(brush, 5, 5, 10, 10);
                };
                using var bmp = new Bitmap(40, 30);
                panel.DrawToBitmap(bmp, new Rectangle(0, 0, 40, 30));
                Check.Equal(new Rectangle(0, 0, 40, 30), seen);
                Check.Equal(Color.FromArgb(255, 10, 200, 30).ToArgb(), bmp.GetPixel(8, 8).ToArgb());
                Check.Equal(Color.White.ToArgb(), bmp.GetPixel(30, 20).ToArgb());
            });
        }
    }
}
