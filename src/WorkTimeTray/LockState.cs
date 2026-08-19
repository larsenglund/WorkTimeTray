using System.Runtime.InteropServices;

namespace WorkTimeTray;

/// <summary>
/// Answers "is the session locked right now?" by asking whether we may open the desktop that
/// currently receives input. While the machine is locked that is the Winlogon desktop, which a
/// process running inside the user session is not allowed to open.
///
/// WTSQuerySessionInformation(WTSSessionInfoEx).SessionFlags looks like the obvious API for this
/// and it is deliberately not used: on this Windows 11 build the flag was observed to still read
/// WTS_SESSIONSTATE_LOCK minutes after a UAC secure desktop had come and gone, with the session
/// wide open the whole time - it reports a remembered transition, not the current state. (The
/// documentation also notes that the two flag values were swapped on older Windows versions.)
///
/// The one weakness of the desktop check is that a UAC prompt takes over the input desktop too and
/// therefore looks like a lock; SessionWatcher debounces a few seconds before believing it.
/// </summary>
public static class LockState
{
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    public static bool IsLocked()
    {
        var desktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (desktop == IntPtr.Zero) return true;
        CloseDesktop(desktop);
        return false;
    }
}
