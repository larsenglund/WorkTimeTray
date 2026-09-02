namespace WorkTimeTray;

/// <summary>
/// The settings file may exist but could not be read. Starting anyway would mean logging to some
/// other folder and splitting the history, so the app declines to run and lets the watchdog try
/// again in a few minutes.
/// </summary>
public sealed class SettingsUnavailableException : Exception
{
    public SettingsUnavailableException(string message) : base(message) { }
}
