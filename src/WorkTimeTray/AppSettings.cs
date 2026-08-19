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
    /// when nobody is at it.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 10;

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
    public static AppSettings Load()
    {
        foreach (var dir in CandidateDirectories())
        {
            var path = Path.Combine(dir, "settings.json");
            if (!File.Exists(path)) continue;
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
                s.SettingsPath = path;
                if (string.IsNullOrWhiteSpace(s.LogDirectory)) s.LogDirectory = dir;
                s.Normalize();
                return s;
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read " + path, ex);
            }
        }

        var fallback = Environment.GetEnvironmentVariable("WORKTIMETRAY_DIR");
        if (string.IsNullOrWhiteSpace(fallback)) fallback = DefaultDataDirectory();
        var fresh = new AppSettings
        {
            SettingsPath = Path.Combine(fallback, "settings.json"),
            LogDirectory = fallback
        };
        fresh.Normalize();
        fresh.Save();
        return fresh;
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
