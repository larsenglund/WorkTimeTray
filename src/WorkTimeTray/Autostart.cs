using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace WorkTimeTray;

/// <summary>
/// Run-at-logon registration, per user and without elevation.
///
/// Two mechanisms are used together, because the HKCU Run key alone proved unreliable here: after
/// one reboot Explorer enumerated the key and started every entry in it except this one, with no
/// error anywhere. A shortcut in the Startup folder is a separate path through the shell, so if one
/// is skipped the other still fires. Both pass --autostart, and a second instance started that way
/// exits silently instead of popping the window.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WorkTimeTray";
    public const string Argument = "--autostart";

    public static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrEmpty(path) ? Application.ExecutablePath : path;
        }
    }

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WorkTimeTray.lnk");

    public static bool IsEnabled => HasRunKey || File.Exists(ShortcutPath);

    private static bool HasRunKey
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string s &&
                       s.Contains("WorkTimeTray", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read autostart", ex);
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        SetRunKey(enabled);
        SetShortcut(enabled);
    }

    private static void SetRunKey(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled) key.SetValue(ValueName, $"\"{ExecutablePath}\" {Argument}");
            else if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to change the autostart registry value", ex);
        }
    }

    private static void SetShortcut(bool enabled)
    {
        try
        {
            var path = ShortcutPath;
            if (!enabled)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var link = (IShellLinkW)new ShellLink();
            link.SetPath(ExecutablePath);
            link.SetArguments(Argument);
            link.SetWorkingDirectory(Path.GetDirectoryName(ExecutablePath) ?? "");
            link.SetDescription("Logs work time to a csv file");
            ((IPersistFile)link).Save(path, true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to change the autostart shortcut", ex);
        }
    }

    // ------------------------------------------------- minimal IShellLink

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
