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
    /// <summary>How long to keep trying before giving up on a settings file that may exist.</summary>
    private const int LoadAttempts = 10;
    private const int LoadRetryMs = 300;

    /// <summary>
    /// True when there was demonstrably no settings file and defaults were created - a first run.
    /// Never set because a file could not be read: that case refuses to start instead.
    /// </summary>
    [JsonIgnore]
    public bool CreatedFromDefaults { get; private set; }

    /// <summary>
    /// Finds and reads the settings file.
    ///
    /// The hard part is telling "there is no settings file" apart from "I cannot see it right now",
    /// and getting that wrong has cost this log three times. At startup the folder can be briefly
    /// unavailable; the app then adopted its default folder, wrote the day's sessions there, and
    /// could not even record that it had done so, because the log it would have written that to
    /// lives in the folder that was unavailable. The window then showed only today.
    ///
    /// So absence must be proven - the containing folder listed, the file genuinely not in it - and
    /// anything short of proof is retried. If it still cannot be proven, the app refuses to start
    /// rather than logging somewhere else; the watchdog retries within five minutes, by which time
    /// the folder is invariably back.
    /// </summary>
    public static AppSettings Load()
    {
        string? lastProblem = null;

        for (var attempt = 1; attempt <= LoadAttempts; attempt++)
        {
            var allProvenAbsent = true;

            foreach (var dir in CandidateDirectories())
            {
                var path = Path.Combine(dir, "settings.json");
                string? text = null;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    if (!ProvenAbsent(path))
                    {
                        allProvenAbsent = false;
                        lastProblem = $"{path}: {ex.GetType().Name} {ex.Message}";
                    }
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
                    allProvenAbsent = false;   // it is there, it just will not parse: worth another look
                    lastProblem = $"{path}: {ex.GetType().Name} {ex.Message}";
                    Log.Error($"Attempt {attempt}: cannot parse {path}", ex);
                }
            }

            if (allProvenAbsent) break;                       // genuinely a first run - no waiting
            if (attempt < LoadAttempts) Thread.Sleep(LoadRetryMs * attempt);
        }

        if (lastProblem is not null)
            throw new SettingsUnavailableException(
                $"the settings could not be read after {LoadAttempts} attempts ({lastProblem})");

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
        Log.Error($"No settings file exists yet; created one at {fresh.SettingsPath}");
        return fresh;
    }

    /// <summary>
    /// Proof that a file is absent rather than merely invisible: its folder has to be listable, and
    /// the file has to be genuinely missing from it. A folder that is not there yet counts as proof
    /// only when its own parent can be listed, which is what makes a real first run instant.
    /// </summary>
    private static bool ProvenAbsent(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return false;

            if (CanList(dir)) return !File.Exists(path);

            var parent = Path.GetDirectoryName(dir);
            return !string.IsNullOrEmpty(parent) && CanList(parent) && !Directory.Exists(dir);
        }
        catch
        {
            return false;
        }
    }

    private static bool CanList(string dir)
    {
        try
        {
            using var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
            e.MoveNext();
            return true;
        }
        catch
        {
            return false;
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
