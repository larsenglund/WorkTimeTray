using System.Globalization;

namespace WorkTimeTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppSettings settings;
        try
        {
            settings = AppSettings.Load();
        }
        catch (SettingsUnavailableException ex)
        {
            // Better not to run at all than to run pointed at the wrong folder. The watchdog task
            // comes back within five minutes, and by then the folder has always been readable.
            Log.Error("Not starting: " + ex.Message);
            return;
        }

        Log.FilePath = Path.Combine(settings.StateDirectory, "worktimetray.log");

        // Scripted switches, handled without starting the tray.
        if (Has(args, "--autostart-on")) { Autostart.Set(true); return; }
        if (Has(args, "--autostart-off")) { Autostart.Set(false); return; }

        var quitting = Has(args, "--quit");
        var fromAutostart = Has(args, Autostart.Argument);

        using var mutex = new Mutex(true, @"Local\WorkTimeTray.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running. Autostart fires twice by design (Run key and Startup shortcut), and
            // the loser of that race must stay quiet instead of popping the window.
            if (!fromAutostart) SingleInstance.Signal(quit: quitting);
            return;
        }

        if (quitting) return; // nothing was running

        // One line per launch: the only reliable way to tell afterwards whether Windows started us.
        Log.Info($"Started ({(fromAutostart ? "autostart" : "manual")}), " +
                 $"settings {settings.SettingsPath}, log -> {settings.LogDirectory}");

        // The whole UI is written in English, so date names are too - otherwise a Swedish Windows
        // gives "onsdag 19 augusti" next to "Working since", which reads like a bug.
        var english = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
        Thread.CurrentThread.CurrentCulture = english;
        Thread.CurrentThread.CurrentUICulture = english;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
        catch (Exception ex)
        {
            Log.Error("SetHighDpiMode failed", ex);
        }

        Application.ThreadException += (_, e) => Fatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);

        try
        {
            Application.Run(new TrayApp(settings, args));
        }
        catch (Exception ex)
        {
            Fatal(ex);
        }

        GC.KeepAlive(mutex);
    }

    private static bool Has(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    private static void Fatal(Exception? ex)
    {
        Log.Error("Unhandled exception", ex);
        MessageBox.Show("WorkTimeTray hit an unexpected error:\n\n" + ex?.Message +
                        "\n\nDetails were written to\n" + Log.FilePath,
            "WorkTimeTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
