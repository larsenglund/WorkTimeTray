using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WorkTimeTray;

/// <summary>Draws the tray icon at runtime: a clock face, green while working, grey when paused.</summary>
public static class TrayIcons
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon? _working;
    private static Icon? _paused;

    public static Icon Get(bool working) =>
        working ? _working ??= Create(true) : _paused ??= Create(false);

    private static Icon Create(bool working)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var face = working ? Color.FromArgb(0x2E, 0xA0, 0x43) : Color.FromArgb(0x7D, 0x84, 0x8C);
            var rim = ControlPaint.Dark(face, 0.15f);

            var r = new Rectangle(1, 1, size - 3, size - 3);
            using (var brush = new SolidBrush(face)) g.FillEllipse(brush, r);
            using (var pen = new Pen(rim, 2f)) g.DrawEllipse(pen, r);

            using var hands = new Pen(Color.White, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var c = new PointF(size / 2f, size / 2f);
            g.DrawLine(hands, c, new PointF(c.X, c.Y - 8f));            // 12 o'clock
            g.DrawLine(hands, c, new PointF(c.X + 6.5f, c.Y + 2.5f));   // ~4 o'clock
        }

        var handle = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(handle);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
