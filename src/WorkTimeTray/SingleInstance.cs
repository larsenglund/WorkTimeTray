namespace WorkTimeTray;

/// <summary>
/// Lets a second launch talk to the instance that is already running: starting the exe again opens
/// the window, starting it with --quit shuts the running one down cleanly.
///
/// This uses named events rather than a broadcast window message. A PostMessage to HWND_BROADCAST
/// is filtered by Windows - it returns TRUE, sets ERROR_ACCESS_DENIED and the message never arrives -
/// so that approach made both features silently do nothing.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string ShowName = @"Local\WorkTimeTray.Show";
    private const string QuitName = @"Local\WorkTimeTray.Quit";

    private readonly EventWaitHandle _show;
    private readonly EventWaitHandle _quit;
    private readonly RegisteredWaitHandle _showWait;
    private readonly RegisteredWaitHandle _quitWait;
    private bool _disposed;

    /// <summary>Starts listening. The callbacks are invoked on the thread that created this.</summary>
    public SingleInstance(Action onShow, Action onQuit)
    {
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);

        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _show, (_, _) => ui.Post(_ => onShow(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(
            _quit, (_, _) => ui.Post(_ => onQuit(), null), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Pokes the running instance. False when nobody is listening.</summary>
    public static bool Signal(bool quit)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(quit ? QuitName : ShowName, out var handle)) return false;
            using (handle) return handle.Set();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to signal the running instance", ex);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _showWait.Unregister(null);
        _quitWait.Unregister(null);
        _show.Dispose();
        _quit.Dispose();
    }
}
