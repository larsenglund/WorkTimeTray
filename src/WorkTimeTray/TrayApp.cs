using System.Globalization;

namespace WorkTimeTray;

/// <summary>Owns the tray icon, the watcher and the (lazily created) main window.</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly TimeLogStore _store;
    private readonly SessionWatcher _watcher;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _todayItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly System.Windows.Forms.Timer _tooltipTimer = new() { Interval = 15000 };
    private readonly SingleInstance _singleInstance;

    private MainForm? _form;
    private bool _exiting;
    private double _todayStored;
    private DateTime _todayDate = DateTime.Today;

    public TrayApp(AppSettings settings, string[] args)
    {
        _settings = settings;
        _store = new TimeLogStore(settings.LogDirectory);
        _watcher = new SessionWatcher(settings, _store);

        _todayItem = new ToolStripMenuItem("Today: -") { Enabled = false };
        _autostartItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled
        };
        _autostartItem.CheckedChanged += (_, _) => Autostart.Set(_autostartItem.Checked);

        _pauseItem = new ToolStripMenuItem("Pause tracking") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => _watcher.ManuallyPaused = _pauseItem.Checked;

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => ShowWindow()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_todayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Quit()));
        menu.Opening += (_, _) => RefreshMenu();

        _icon = new NotifyIcon
        {
            Icon = TrayIcons.Get(_watcher.State),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        _icon.DoubleClick += (_, _) => ShowWindow();

        WarnIfSettingsWereNotRead();
        WarnIfLogDirectoryChanged();

        _watcher.StateChanged += OnStateChanged;
        _watcher.SessionClosed += _ => { RecomputeToday(); UpdateTooltip(); };

        _singleInstance = new SingleInstance(ShowWindow, Quit);
        Autostart.EnsureWatchdog();   // a previous deliberate quit will have taken it down

        RecomputeToday();
        UpdateTooltip();
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        _tooltipTimer.Start();

        if (settings.ShowWindowOnStartup || args.Contains("--show", StringComparer.OrdinalIgnoreCase))
            ShowWindow();
    }

    /// <summary>
    /// The settings could not be read at all, so the app invented defaults and is writing its log
    /// somewhere other than usual. Silently carrying on is what cost two mornings of history.
    /// </summary>
    private void WarnIfSettingsWereNotRead()
    {
        if (!_settings.CreatedFromDefaults) return;

        var nl = Environment.NewLine;
        _icon.ShowBalloonTip(30000, "WorkTimeTray could not read its settings",
            "It fell back to defaults and is writing to:" + nl + _settings.LogDirectory + nl + nl +
            "If that is not where your log lives, quit and start it again.",
            ToolTipIcon.Error);
    }

    /// <summary>
    /// Remembers the log directory between runs and complains when it changes. A change is either a
    /// deliberate reinstall with a new -LogDirectory, or the settings did not load and the app fell
    /// back to its default folder - which silently splits the history in two and is otherwise only
    /// noticed days later, when the window looks empty.
    /// </summary>
    private void WarnIfLogDirectoryChanged()
    {
        try
        {
            // Anchored to the default folder, never to the resolved one: if a future fault moves
            // the whole configuration, a marker that moved with it would never notice.
            var anchor = AppSettings.DefaultDataDirectory();
            Directory.CreateDirectory(anchor);
            var marker = Path.Combine(anchor, "last-logdir.txt");
            var current = _settings.LogDirectory;
            var previous = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;

            if (!string.IsNullOrEmpty(previous) &&
                !string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error($"Log directory changed from '{previous}' to '{current}' " +
                          $"(settings file: {_settings.SettingsPath})");
                var nl = Environment.NewLine;
                _icon.ShowBalloonTip(20000, "WorkTimeTray is logging somewhere else",
                    "Now writing to:" + nl + current + nl + nl +
                    "Previously:" + nl + previous + nl + nl +
                    "If that was not deliberate, your history is split across two folders.",
                    ToolTipIcon.Warning);
            }

            File.WriteAllText(marker, current);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to check the log directory marker", ex);
        }
    }

    private void OnStateChanged()
    {
        if (_exiting) return;   // the tray icon is being torn down, touching it throws
        try
        {
            _icon.Icon = TrayIcons.Get(_watcher.State);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update the tray icon", ex);
        }
        RecomputeToday();
        UpdateTooltip();
    }

    private void RecomputeToday()
    {
        try
        {
            _todayDate = DateTime.Today;
            _todayStored = _store.LoadAll().Sum(e => e.HoursOn(_todayDate));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to total today", ex);
        }
    }

    private double TodayHours()
    {
        if (_todayDate != DateTime.Today) RecomputeToday();
        return _todayStored + _watcher.LiveHoursOn(DateTime.Today);
    }

    private void UpdateTooltip()
    {
        if (_exiting) return;
        // NotifyIcon.Text allows 63 characters, so keep this tight.
        _icon.Text = $"WorkTimeTray - today {TodayText()} h ({StateWord()})";
    }

    /// <summary>"1.29 / 5.60" - worked against what is expected today, or just the hours on a day
    /// with no target at all.</summary>
    private string TodayText()
    {
        var worked = TodayHours().ToString("0.00", CultureInfo.InvariantCulture);
        var target = _settings.ExpectedHoursOn(DateTime.Today);
        return target > 0
            ? worked + " / " + target.ToString("0.00", CultureInfo.InvariantCulture)
            : worked;
    }

    private string StateWord() => _watcher.State switch
    {
        WorkState.Working => "working",
        WorkState.Idle => "idle",
        WorkState.Paused => "PAUSED",
        _ => "locked"
    };

    private void RefreshMenu()
    {
        _todayItem.Text = $"Today: {TodayText()} h  ({StateWord()})";
        _autostartItem.Checked = Autostart.IsEnabled;
        _pauseItem.Checked = _watcher.ManuallyPaused;
    }

    private void ShowWindow()
    {
        if (_form is null || _form.IsDisposed)
        {
            _form = new MainForm(_settings, _store, _watcher);
        }
        _form.ShowAndActivate();
    }

    private void OpenLogFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _settings.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("Failed to open log folder", ex);
        }
    }

    private void Quit() => ExitThread();

    protected override void ExitThreadCore()
    {
        _exiting = true;
        Autostart.RemoveWatchdog();   // you asked it to stop; it stays stopped
        _tooltipTimer.Stop();
        _watcher.Dispose();   // stop polling first, so the flush below is the last word
        _watcher.Flush();     // write the session that is open right now
        _icon.Visible = false;
        _icon.Dispose();
        _singleInstance.Dispose();
        base.ExitThreadCore();
    }
}
