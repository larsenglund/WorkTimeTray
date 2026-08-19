namespace WorkTimeTray;

/// <summary>Minimal error log; never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string FilePath { get; set; } =
        Path.Combine(AppSettings.DefaultDataDirectory(), "worktimetray.log");

    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{(ex is null ? "" : Environment.NewLine + ex)}";
                File.AppendAllText(FilePath, text + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }

    public static void Info(string message) => Error(message);
}
