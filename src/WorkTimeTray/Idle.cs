using System.Runtime.InteropServices;

namespace WorkTimeTray;

/// <summary>
/// Time since the last keyboard or mouse input in this session. Needed because an unlocked
/// machine is not the same thing as a machine somebody is working at - leaving the computer on
/// and unlocked over night would otherwise log the whole night as work.
/// </summary>
public static class Idle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan Since()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        // Both values are 32 bit tick counts, so an unchecked subtraction survives the 49 day wrap.
        var milliseconds = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
