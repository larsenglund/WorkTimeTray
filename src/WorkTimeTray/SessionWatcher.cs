using System.Globalization;
using Microsoft.Win32;

namespace WorkTimeTray;

public enum WorkState
{
    Working,
    Locked,
    Idle,

    /// <summary>Paused by hand. Outranks everything else until you resume.</summary>
    Paused
}

/// <summary>
/// Tracks work time. A session is open while the Windows session is unlocked *and* somebody is
/// actually using the machine.
///
/// The lock state is polled once a second and also re-evaluated whenever Windows raises a session
/// or power event, so a missed event self-corrects within a second. If the clock jumps (sleep,
/// hibernation, a hung machine) the missing period is not counted: the open session is closed at
/// the last tick before the gap.
/// </summary>
public sealed class SessionWatcher : IDisposable
{
    private readonly AppSettings _settings;
    private readonly TimeLogStore _store;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly SynchronizationContext _ui;
    private readonly string _statePath;
    private readonly TimeSpan _idleTimeout;

    private DateTime _lastTick;
    private DateTime _lastHeartbeatWrite = DateTime.MinValue;
    private int _lockedTicks;
    private DateTime _firstLockedAt;
    private DateTime _suppressStartUntil = DateTime.MinValue;
    private bool _lockedByEvent;
    private bool _startedOnce;
    private bool _manuallyPaused;
    private bool _disposed;

    /// <summary>A gap larger than this between two ticks means the machine was not running.</summary>
    private static readonly TimeSpan MaxTickGap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>Consecutive locked observations needed before a poll closes the session. A UAC
    /// prompt owns the input desktop too, and should not chop the day into pieces.</summary>
    private const int LockConfirmTicks = 3;

    /// <summary>Input this fresh, on our own desktop, can only mean the session is unlocked.</summary>
    private static readonly TimeSpan RecentInput = TimeSpan.FromSeconds(2);

    public WorkState State { get; private set; } = WorkState.Locked;
    public bool IsWorking => State == WorkState.Working;
    public DateTime CurrentStart { get; private set; }

    /// <summary>Raised on the UI thread after a session was written to the log.</summary>
    public event Action<TimeEntry>? SessionClosed;

    /// <summary>Raised on the UI thread when the state changes.</summary>
    public event Action? StateChanged;

    public SessionWatcher(AppSettings settings, TimeLogStore store)
    {
        _settings = settings;
        _store = store;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _statePath = Path.Combine(settings.StateDirectory, "current-session.txt");
        _idleTimeout = settings.IdleTimeoutMinutes > 0
            ? TimeSpan.FromMinutes(settings.IdleTimeoutMinutes)
            : TimeSpan.Zero;

        RecoverInterruptedSession();

        _lastTick = DateTime.Now;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;

        Evaluate();
    }

    /// <summary>Hours of the currently open session that fall on the given day (0 when paused).</summary>
    public double LiveHoursOn(DateTime day)
    {
        if (!IsWorking) return 0d;
        return new TimeEntry(CurrentStart, DateTime.Now).HoursOn(day);
    }

    public TimeSpan CurrentDuration => IsWorking ? DateTime.Now - CurrentStart : TimeSpan.Zero;

    /// <summary>When the clock is paused for idleness, the moment of the last keypress.</summary>
    public DateTime LastInput => DateTime.Now - Idle.Since();

    /// <summary>When the manual pause was pressed.</summary>
    public DateTime PausedSince { get; private set; }

    /// <summary>
    /// Manual pause, for doing something that is not work while sitting at the machine. It beats
    /// every other signal: unlocking, typing and moving the mouse all leave the clock stopped until
    /// you resume. Deliberately not remembered across restarts - a forgotten pause silently loses a
    /// day, whereas a forgotten resume is visible the moment you look at the tray icon.
    /// </summary>
    public bool ManuallyPaused
    {
        get => _manuallyPaused;
        set
        {
            if (_manuallyPaused == value) return;
            _manuallyPaused = value;

            if (value)
            {
                PausedSince = DateTime.Now;
                if (IsWorking) StopSession(PausedSince);
                SetState(WorkState.Paused);
            }
            else
            {
                _suppressStartUntil = DateTime.MinValue;
                Evaluate();
            }
        }
    }

    // ---------------------------------------------------------------- core

    private void Evaluate()
    {
        if (_disposed) return;
        var now = DateTime.Now;

        // Sleep, hibernation or a stopped clock: do not count the invisible period.
        var gap = now - _lastTick;
        if (gap > MaxTickGap || gap < TimeSpan.FromSeconds(-5))
        {
            if (IsWorking) StopSession(_lastTick);
            _lockedTicks = 0;
        }
        _lastTick = now;

        if (_manuallyPaused)
        {
            if (IsWorking) StopSession(now);
            SetState(WorkState.Paused);
            return;
        }

        bool desktopLocked;
        try
        {
            desktopLocked = LockState.IsLocked();
        }
        catch (Exception ex)
        {
            Log.Error("Lock state query failed", ex);
            return;
        }

        var idle = Idle.Since();

        // The desktop check is one way evidence. Winlogon owning the input desktop proves the
        // machine is locked, but the reverse is not true: once the display sleeps the lock screen
        // is dismissed and the input desktop goes back to Default while the session stays locked.
        // So a lock we were told about is remembered until something proves the session is open
        // again - an unlock event, or fresh input on our own desktop.
        if (!desktopLocked && idle < RecentInput) _lockedByEvent = false;

        if (desktopLocked)
        {
            if (_lockedTicks == 0) _firstLockedAt = now;
            _lockedTicks++;
        }
        else
        {
            _lockedTicks = 0;
        }

        // A few consecutive observations are needed before the poll believes a lock, so that a
        // passing UAC prompt does not chop the day into pieces; the stop is then backdated to the
        // first one, so nothing is lost.
        if (_lockedByEvent || _lockedTicks >= LockConfirmTicks)
        {
            if (IsWorking) StopSession(_lockedByEvent ? now : _firstLockedAt);
            SetState(WorkState.Locked);
        }
        else if (_idleTimeout > TimeSpan.Zero && idle >= _idleTimeout)
        {
            // Unlocked but nobody is here. End the session at the last keypress, not now.
            if (IsWorking) StopSession(now - idle);
            SetState(WorkState.Idle);
        }
        else if (!IsWorking && now >= _suppressStartUntil)
        {
            // Just after launch there is no event history to lean on, and the machine may well have
            // been locked before we started - with the lock screen already dismissed, which looks
            // exactly like an unlocked machine. So the first session waits for real input.
            if (!_startedOnce && idle >= RecentInput)
            {
                SetState(WorkState.Idle);
            }
            else
            {
                _startedOnce = true;
                StartSession(now);
            }
        }

        if (IsWorking && now - _lastHeartbeatWrite >= HeartbeatInterval) WriteState(now);
    }

    private void SetState(WorkState state)
    {
        if (State == state) return;
        State = state;
        Raise(() => StateChanged?.Invoke());
    }

    private void StartSession(DateTime at)
    {
        CurrentStart = at;
        SetState(WorkState.Working);
        WriteState(at);
    }

    private void StopSession(DateTime at)
    {
        if (!IsWorking) return;
        if (at < CurrentStart) at = CurrentStart;   // never produce a negative session
        var entry = new TimeEntry(CurrentStart, at);
        State = WorkState.Locked;                   // the caller sets the real reason right after
        ClearState();
        Commit(entry);
    }

    private void Commit(TimeEntry entry)
    {
        if (entry.Duration.TotalSeconds < _settings.MinSessionSeconds) return;
        try
        {
            _store.Append(entry);
            Raise(() => SessionClosed?.Invoke(entry));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to write session " + entry.ToCsv(), ex);
        }
    }

    /// <summary>
    /// Windows told us the session is going away (lock, logoff, disconnect, suspend). That needs no
    /// confirmation, so the session is closed right now; starting again is held off for a moment in
    /// case the event arrives before the desktop actually switches.
    /// </summary>
    private void LockNow()
    {
        var now = DateTime.Now;
        _lastTick = now;
        if (IsWorking) StopSession(now);
        SetState(WorkState.Locked);
        _lockedTicks = LockConfirmTicks;
        _firstLockedAt = now;
        _suppressStartUntil = now.AddSeconds(2);
    }

    /// <summary>Closes the open session, e.g. on shutdown or when the user quits.</summary>
    public void Flush()
    {
        if (IsWorking) StopSession(DateTime.Now);
    }

    private void Raise(Action action)
    {
        if (SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }

    // ---------------------------------------------------- crash recovery

    /// <summary>
    /// The state file holds the start of the open session plus a heartbeat that is refreshed
    /// every 30 s. If it still exists at startup the previous run died (power loss, crash,
    /// forced reboot) and the session is closed at the last heartbeat.
    /// </summary>
    private void RecoverInterruptedSession()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            var parts = File.ReadAllText(_statePath).Split(',', StringSplitOptions.TrimEntries);
            File.Delete(_statePath);
            if (parts.Length < 2) return;
            if (!DateTime.TryParseExact(parts[0], TimeEntry.DateTimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var start)) return;
            if (!DateTime.TryParseExact(parts[1], TimeEntry.DateTimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var heartbeat)) return;
            if (heartbeat <= start) return;

            var entry = new TimeEntry(start, heartbeat);
            if (entry.Duration.TotalSeconds < _settings.MinSessionSeconds) return;
            _store.Append(entry);
            Log.Info($"Recovered interrupted session {entry.ToCsv()}");
        }
        catch (Exception ex)
        {
            Log.Error("Session recovery failed", ex);
        }
    }

    private void WriteState(DateTime heartbeat)
    {
        _lastHeartbeatWrite = heartbeat;
        try
        {
            var text = CurrentStart.ToString(TimeEntry.DateTimeFormat, CultureInfo.InvariantCulture) + "," +
                       heartbeat.ToString(TimeEntry.DateTimeFormat, CultureInfo.InvariantCulture);
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, _statePath, true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to write heartbeat", ex);
        }
    }

    private void ClearState()
    {
        try
        {
            if (File.Exists(_statePath)) File.Delete(_statePath);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to clear heartbeat", ex);
        }
    }

    // -------------------------------------------------------- OS events

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Log.Info("Session switch: " + e.Reason);
        var leaving = e.Reason == SessionSwitchReason.SessionLock
                   || e.Reason == SessionSwitchReason.SessionLogoff
                   || e.Reason == SessionSwitchReason.ConsoleDisconnect
                   || e.Reason == SessionSwitchReason.RemoteDisconnect;
        Raise(() =>
        {
            if (leaving)
            {
                _lockedByEvent = true;
                LockNow();
            }
            else
            {
                _lockedByEvent = false;
                _suppressStartUntil = DateTime.MinValue;
                Evaluate();
            }
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Raise(() => { _lockedByEvent = true; LockNow(); });
        else Raise(Evaluate);
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => Raise(Flush);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _timer.Stop();
        _timer.Dispose();
    }
}
