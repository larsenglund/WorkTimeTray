using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WorkTimeTray;

/// <summary>
/// Draws the tray icon at runtime: a clock face, green while working, amber when you paused it by
/// hand, grey when it stopped by itself. Amber is its own colour so a forgotten manual pause is
/// obvious at a glance rather than looking like an ordinary locked screen.
/// </summary>
public static class TrayIcons
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly Dictionary<WorkState, Icon> Cache = new();

    public static Icon Get(WorkState state)
    {
        if (!Cache.TryGetValue(state, out var icon)) Cache[state] = icon = Create(state);
        return icon;
    }

    private static Icon Create(WorkState state)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var face = state switch
            {
                WorkState.Working => Color.FromArgb(0x2E, 0xA0, 0x43),
                WorkState.Paused => Color.FromArgb(0xD2, 0x99, 0x22),
                _ => Color.FromArgb(0x7D, 0x84, 0x8C)
            };
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
