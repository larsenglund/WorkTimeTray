namespace WorkTimeTray;

/// <summary>Minimal error log; never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string FilePath { get; set; } =
        Path.Combine(AppSettings.DefaultDataDirectory(), "worktimetray.log");

    /// <summary>
    /// Writes a line, retrying briefly. The retries matter: the one time the app silently moved its
    /// log to another folder, the explanation was lost because this write failed in the same moment
    /// the settings read did, leaving nothing at all to go on.
    /// </summary>
    public static void Error(string message, Exception? ex = null)
    {
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{(ex is null ? "" : Environment.NewLine + ex)}";

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.AppendAllText(FilePath, text + Environment.NewLine);
                }
                return;
            }
            catch
            {
                if (attempt < 3) Thread.Sleep(120 * attempt);
                // logging must never take the app down
            }
        }
    }

    public static void Info(string message) => Error(message);
}
