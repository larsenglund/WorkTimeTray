using Microsoft.Win32;

namespace WorkTimeTray;

/// <summary>Small light/dark palette that follows the Windows app theme.</summary>
public static class Theme
{
    /// <summary>Read once at startup; restart the app after switching the Windows theme.</summary>
    public static bool IsDark { get; } = ReadIsDark();

    private static bool ReadIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static Color Background => IsDark ? Rgb(0x1E1F22) : Rgb(0xF7F8FA);
    public static Color Surface => IsDark ? Rgb(0x26282C) : Color.White;
    public static Color SurfaceDim => IsDark ? Rgb(0x202226) : Rgb(0xEFF1F4);
    public static Color Border => IsDark ? Rgb(0x3A3D42) : Rgb(0xDBDEE3);
    public static Color Text => IsDark ? Rgb(0xE8E8E8) : Rgb(0x1F2328);
    public static Color TextDim => IsDark ? Rgb(0x9AA0A6) : Rgb(0x6E7781);
    public static Color Accent => IsDark ? Rgb(0x4C8DFF) : Rgb(0x2F6FEB);
    public static Color AccentSoft => IsDark ? Rgb(0x2C3E60) : Rgb(0xDDE8FD);
    public static Color Working => IsDark ? Rgb(0x3FB950) : Rgb(0x1A7F37);
    public static Color Ahead => Working;
    public static Color Behind => IsDark ? Rgb(0xF85149) : Rgb(0xC1121F);
    public static Color Paused => IsDark ? Rgb(0x8B949E) : Rgb(0x8C959F);
    public static Color Weekend => IsDark ? Rgb(0x2A2C31) : Rgb(0xF4F5F7);

    private static Color Rgb(int rgb) => Color.FromArgb(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF);

    public static void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = Surface;
        b.ForeColor = Text;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
    }
}
