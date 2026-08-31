using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkTimeTray;

/// <summary>
/// User settings, persisted as settings.json in the application data folder.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Folder the worktime-YYYY.csv files are written to.</summary>
    public string LogDirectory { get; set; } = "";

    /// <summary>Sessions shorter than this are discarded (they would round to 0.00 h).</summary>
    public int MinSessionSeconds { get; set; } = 30;

    /// <summary>
    /// Stop the clock after this many minutes without keyboard or mouse input, and backdate the
    /// stop to the last input. 0 disables it, and then an unlocked machine counts as work even
    /// when nobody is at it. Long enough to sit through a video without being marked away.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>Hours you are expected to work on a working day.</summary>
    public double ExpectedHoursPerDay { get; set; } = 5.6;

    /// <summary>The days that carry that expectation. Everything else expects nothing.</summary>
    public DayOfWeek[] WorkDays { get; set; } =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    };

    /// <summary>Week starts on Monday. When false the current culture decides.</summary>
    public bool WeekStartsMonday { get; set; } = true;

    /// <summary>Show the main window when the app starts.</summary>
    public bool ShowWindowOnStartup { get; set; }

    [JsonIgnore]
    public string SettingsPath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }   // WorkDays reads as names, not numbers
    };

    /// <summary>
    /// Resolution order for the data folder:
    ///   1. %WORKTIMETRAY_DIR%
    ///   2. settings.json next to the executable (portable install)
    ///   3. %LOCALAPPDATA%\WorkTimeTray
    /// </summary>
    /// <summary>How many times to look for a settings file before concluding there is not one.</summary>
    private const int LoadAttempts = 6;
    private const int LoadRetryMs = 400;

    /// <summary>
    /// True when no settings file could be read and defaults were invented. Worth shouting about:
    /// it means the log is going somewhere other than where it went last time.
    /// </summary>
    [JsonIgnore]
    public bool CreatedFromDefaults { get; private set; }

    /// <summary>
    /// Reads the settings file, retrying before it gives up.
    ///
    /// At logon the profile folder can be briefly unavailable, and File.Exists answers false for
    /// *any* failure, not only for a missing file. Treating that as "no settings" made the app adopt
    /// its default folder and log there silently - which split the history in two on two separate
    /// mornings, with nothing written anywhere to say it had happened, because the fallback's own
    /// Save() and log writes were failing in the same moment. So a file that cannot be read is now
    /// carefully distinguished from a file that is not there.
    /// </summary>
    public static AppSettings Load()
    {
        for (var attempt = 1; attempt <= LoadAttempts; attempt++)
        {
            var sawUnreadable = false;

            foreach (var dir in CandidateDirectories())
            {
                var path = Path.Combine(dir, "settings.json");
                var text = TryRead(path, out var missing);

                if (text is null)
                {
                    if (!missing) sawUnreadable = true;   // present but not readable yet - worth waiting for
                    continue;
                }

                try
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(text, JsonOptions) ?? new AppSettings();
                    s.SettingsPath = path;
                    if (string.IsNullOrWhiteSpace(s.LogDirectory)) s.LogDirectory = dir;
                    s.Normalize();
                    return s;
                }
                catch (Exception ex)
                {
                    sawUnreadable = true;                 // corrupt, or half written by an installer
                    Log.Error($"Attempt {attempt}: cannot parse {path}", ex);
                }
            }

            // Every candidate was genuinely absent, so waiting will not conjure one up.
            if (!sawUnreadable) break;
            if (attempt < LoadAttempts) Thread.Sleep(LoadRetryMs * attempt);
        }

        var fallback = Environment.GetEnvironmentVariable("WORKTIMETRAY_DIR");
        if (string.IsNullOrWhiteSpace(fallback)) fallback = DefaultDataDirectory();
        var fresh = new AppSettings
        {
            SettingsPath = Path.Combine(fallback, "settings.json"),
            LogDirectory = fallback,
            CreatedFromDefaults = true
        };
        fresh.Normalize();
        fresh.Save();
        Log.Error($"No settings file could be read after {LoadAttempts} attempts. " +
                  $"Falling back to defaults and logging to {fresh.LogDirectory}");
        return fresh;
    }

    /// <summary>
    /// Reads a file, saying whether it is absent or merely unreadable right now - the distinction
    /// File.Exists cannot make, and the one this whole class turns on.
    /// </summary>
    private static string? TryRead(string path, out bool missing)
    {
        missing = false;
        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            missing = true;
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            missing = true;
            return null;
        }
        catch
        {
            return null;   // locked, denied, offline: all worth another attempt
        }
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var env = Environment.GetEnvironmentVariable("WORKTIMETRAY_DIR");
        if (!string.IsNullOrWhiteSpace(env)) yield return env;

        var exeDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(exeDir)) yield return exeDir.TrimEnd(Path.DirectorySeparatorChar);

        yield return DefaultDataDirectory();
    }

    public static string DefaultDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkTimeTray");

    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(LogDirectory)) LogDirectory = DefaultDataDirectory();
        LogDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(LogDirectory));
        if (MinSessionSeconds < 0) MinSessionSeconds = 0;
        Directory.CreateDirectory(LogDirectory);
        var settingsDir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(settingsDir)) Directory.CreateDirectory(settingsDir);
    }

    public void Save()
    {
        try
        {
            Normalize();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    /// <summary>What you are expected to work on that date - 0 on a day that is not a work day.</summary>
    public double ExpectedHoursOn(DateTime day) =>
        Array.IndexOf(WorkDays, day.DayOfWeek) >= 0 ? ExpectedHoursPerDay : 0d;

    [JsonIgnore]
    public DayOfWeek FirstDayOfWeek => WeekStartsMonday
        ? DayOfWeek.Monday
        : System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>Folder used for the heartbeat/state file and the error log.</summary>
    [JsonIgnore]
    public string StateDirectory
    {
        get
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            return string.IsNullOrEmpty(dir) ? DefaultDataDirectory() : dir;
        }
    }
}
