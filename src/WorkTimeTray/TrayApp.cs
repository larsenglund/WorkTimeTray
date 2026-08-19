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

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => ShowWindow()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_todayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Quit()));
        menu.Opening += (_, _) => RefreshMenu();

        _icon = new NotifyIcon
        {
            Icon = TrayIcons.Get(_watcher.IsWorking),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        _icon.DoubleClick += (_, _) => ShowWindow();

        _watcher.StateChanged += OnStateChanged;
        _watcher.SessionClosed += _ => { RecomputeToday(); UpdateTooltip(); };

        _singleInstance = new SingleInstance(ShowWindow, Quit);

        RecomputeToday();
        UpdateTooltip();
        _tooltipTimer.Tick += (_, _) => UpdateTooltip();
        _tooltipTimer.Start();

        if (settings.ShowWindowOnStartup || args.Contains("--show", StringComparer.OrdinalIgnoreCase))
            ShowWindow();
    }

    private void OnStateChanged()
    {
        if (_exiting) return;   // the tray icon is being torn down, touching it throws
        try
        {
            _icon.Icon = TrayIcons.Get(_watcher.IsWorking);
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
        _ => "locked"
    };

    private void RefreshMenu()
    {
        _todayItem.Text = $"Today: {TodayText()} h  ({StateWord()})";
        _autostartItem.Checked = Autostart.IsEnabled;
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
        _tooltipTimer.Stop();
        _watcher.Dispose();   // stop polling first, so the flush below is the last word
        _watcher.Flush();     // write the session that is open right now
        _icon.Visible = false;
        _icon.Dispose();
        _singleInstance.Dispose();
        base.ExitThreadCore();
    }
}
