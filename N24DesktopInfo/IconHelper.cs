using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace N24DesktopInfo
{
    /// <summary>
    /// Generates the N24 Desktop Info tray icon programmatically.
    /// A mini chart/pulse icon in accent color on transparent background.
    /// </summary>
    internal static class IconHelper
    {
        public static Icon CreateTrayIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var accentColor = Color.FromArgb(0, 212, 170);   // #00d4aa
            var dimColor = Color.FromArgb(100, 0, 212, 170);  // 40% opacity accent

            // Background: subtle rounded rectangle
            using (var bgBrush = new SolidBrush(Color.FromArgb(180, 20, 20, 36)))
            {
                FillRoundedRect(g, bgBrush, new RectangleF(1, 1, 30, 30), 5);
            }

            // Grid lines (subtle)
            using (var gridPen = new Pen(Color.FromArgb(50, 45, 53, 97), 0.5f))
            {
                g.DrawLine(gridPen, 4, 10, 28, 10);
                g.DrawLine(gridPen, 4, 16, 28, 16);
                g.DrawLine(gridPen, 4, 22, 28, 22);
            }

            // Chart line (in - green accent)
            using (var inPen = new Pen(accentColor, 2.2f))
            {
                inPen.LineJoin = LineJoin.Round;
                inPen.StartCap = LineCap.Round;
                inPen.EndCap = LineCap.Round;
                var inPts = new PointF[]
                {
                    new(4, 24), new(8, 20), new(11, 22), new(14, 12),
                    new(17, 18), new(21, 8), new(25, 14), new(28, 10)
                };
                g.DrawLines(inPen, inPts);
            }

            // Chart line (out - red, dimmer)
            using (var outPen = new Pen(Color.FromArgb(180, 255, 107, 107), 1.5f))
            {
                outPen.LineJoin = LineJoin.Round;
                var outPts = new PointF[]
                {
                    new(4, 26), new(8, 24), new(12, 25), new(16, 20),
                    new(20, 22), new(24, 18), new(28, 19)
                };
                g.DrawLines(outPen, outPts);
            }

            // Glow dot at current value (in)
            using (var glowBrush = new SolidBrush(accentColor))
            {
                g.FillEllipse(glowBrush, 26f, 8f, 4f, 4f);
            }

            // Convert bitmap to icon (clone so we can destroy the native handle)
            IntPtr hIcon = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(hIcon).Clone();
            NativeMethods.DestroyIcon(hIcon);
            return icon;
        }

        private static void FillRoundedRect(Graphics g, Brush brush, RectangleF rect, float radius)
        {
            using var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
