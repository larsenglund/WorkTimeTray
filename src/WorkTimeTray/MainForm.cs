using System.Globalization;

namespace WorkTimeTray;

/// <summary>
/// The window behind the tray icon: a month calendar with hours per day, week sums, a month
/// sum and the sessions of the selected day, which can be added, edited and deleted.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly TimeLogStore _store;
    private readonly SessionWatcher _watcher;

    private readonly CalendarView _calendar = new();
    private readonly Label _monthLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _dayTitle = new();
    private readonly Label _dayTotal = new();
    private readonly Label _totalsLabel = new();
    private readonly Label _balanceLabel = new();
    private readonly Label _monthTargetLabel = new();
    private readonly Label _dayTarget = new();
    private readonly ListView _list = new();
    private readonly CheckBox _autostart = new();
    private readonly Button _pauseButton = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    private List<TimeEntry> _entries = new();
    private Dictionary<DateTime, double> _stored = new();
    private ListViewItem? _runningItem;
    private DateTime? _storedDayStart;      // earliest logged start that falls on today
    private DateTime? _trackingStart;       // first day ever logged - nothing is expected before it
    private DateTime _dataDate = DateTime.Today;

    public MainForm(AppSettings settings, TimeLogStore store, SessionWatcher watcher)
    {
        _settings = settings;
        _store = store;
        _watcher = watcher;

        Text = "WorkTimeTray";
        Font = new Font("Segoe UI", 9.75f);
        ClientSize = new Size(1020, 640);
        MinimumSize = new Size(880, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Icon = (Icon)TrayIcons.Get(WorkState.Working).Clone();
        KeyPreview = true;

        BuildUi();

        _watcher.SessionClosed += _ => { Reload(); };
        _watcher.StateChanged += UpdateStatus;
        _tick.Tick += (_, _) => Live();
        _tick.Start();

        Reload();
    }

    // ---------------------------------------------------------------- ui

    private void BuildUi()
    {
        _calendar.Dock = DockStyle.Fill;
        _calendar.FirstDayOfWeek = _settings.FirstDayOfWeek;
        _calendar.HoursProvider = HoursForDay;
        _calendar.DayBalanceProvider = DayBalance;
        _calendar.WeekBalanceProvider = WeekBalance;
        _calendar.SelectedDateChanged += (_, _) => RefreshDayPanel();
        _calendar.MonthChanged += (_, _) => { UpdateMonthLabel(); UpdateTotals(); };
        _calendar.DateActivated += (_, _) => AddEntry();

        Controls.Add(_calendar);
        Controls.Add(BuildDayPanel());
        Controls.Add(BuildTopBar());
        Controls.Add(BuildBottomBar());
    }

    private Control BuildTopBar()
    {
        var panel = new Panel { Dock = DockStyle.Top, BackColor = Theme.Background };
        panel.Size = new Size(ClientSize.Width, 62);

        var prev = MakeButton("<", 8, 16, 34, 30, () => _calendar.Month = _calendar.Month.AddMonths(-1));
        var next = MakeButton(">", 214, 16, 34, 30, () => _calendar.Month = _calendar.Month.AddMonths(1));
        var today = MakeButton("Today", 258, 16, 74, 30, () =>
        {
            _calendar.SelectedDate = DateTime.Today;
            _calendar.Month = DateTime.Today;
            RefreshDayPanel();
        });

        _pauseButton.SetBounds(348, 16, 104, 30);
        Theme.StyleButton(_pauseButton);
        _pauseButton.Click += (_, _) => TogglePause();

        _monthLabel.SetBounds(48, 18, 162, 28);
        _monthLabel.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
        _monthLabel.ForeColor = Theme.Text;
        _monthLabel.TextAlign = ContentAlignment.MiddleCenter;

        _statusLabel.SetBounds(468, 18, panel.Width - 480, 28);
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _statusLabel.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;

        panel.Controls.AddRange(new Control[]
            { prev, _monthLabel, next, today, _pauseButton, _statusLabel });
        return panel;
    }

    private Control BuildBottomBar()
    {
        var panel = new Panel { Dock = DockStyle.Bottom, BackColor = Theme.Background };
        panel.Size = new Size(ClientSize.Width, 66);

        _totalsLabel.SetBounds(8, 8, panel.Width - 360, 22);
        _totalsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _totalsLabel.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Regular);
        _totalsLabel.ForeColor = Theme.Text;

        _balanceLabel.SetBounds(8, 34, 150, 22);
        _balanceLabel.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);

        _monthTargetLabel.SetBounds(160, 35, panel.Width - 512, 20);
        _monthTargetLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _monthTargetLabel.Font = new Font(Font.FontFamily, 9.75f, FontStyle.Regular);
        _monthTargetLabel.ForeColor = Theme.TextDim;

        _autostart.Text = "Start with Windows";
        _autostart.SetBounds(panel.Width - 340, 24, 150, 22);
        _autostart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _autostart.ForeColor = Theme.Text;
        _autostart.Checked = Autostart.IsEnabled;
        _autostart.CheckedChanged += (_, _) => Autostart.Set(_autostart.Checked);

        var folder = MakeButton("Open log folder", panel.Width - 180, 20, 170, 30, OpenLogFolder);
        folder.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        panel.Controls.AddRange(new Control[]
            { _totalsLabel, _balanceLabel, _monthTargetLabel, _autostart, folder });
        return panel;
    }

    private Control BuildDayPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 340,
            Padding = new Padding(12, 0, 8, 0),
            BackColor = Theme.Background
        };

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = Theme.Surface;
        _list.ForeColor = Theme.Text;
        _list.Columns.Add("Start", 108);
        _list.Columns.Add("Stop", 108);
        _list.Columns.Add("Hours", 78, HorizontalAlignment.Right);
        _list.DoubleClick += (_, _) => EditEntry();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) DeleteEntry();
            if (e.KeyCode == Keys.Enter) EditEntry();
        };

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Theme.Background };
        buttons.Controls.Add(MakeButton("Add", 0, 10, 96, 30, AddEntry));
        buttons.Controls.Add(MakeButton("Edit", 104, 10, 96, 30, EditEntry));
        buttons.Controls.Add(MakeButton("Delete", 208, 10, 96, 30, DeleteEntry));

        _dayTarget.Dock = DockStyle.Top;
        _dayTarget.Height = 22;
        _dayTarget.Font = new Font(Font.FontFamily, 9.75f, FontStyle.Regular);

        _dayTotal.Dock = DockStyle.Top;
        _dayTotal.Height = 34;
        _dayTotal.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
        _dayTotal.ForeColor = Theme.Text;

        _dayTitle.Dock = DockStyle.Top;
        _dayTitle.Height = 30;
        _dayTitle.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        _dayTitle.ForeColor = Theme.TextDim;
        _dayTitle.TextAlign = ContentAlignment.BottomLeft;

        panel.Controls.Add(_list);
        panel.Controls.Add(buttons);
        panel.Controls.Add(_dayTarget);   // docking runs last-added-first, so this lands under...
        panel.Controls.Add(_dayTotal);    // ...the total, which lands under the title
        panel.Controls.Add(_dayTitle);
        return panel;
    }

    private static Button MakeButton(string text, int left, int top, int width, int height, Action onClick)
    {
        var b = new Button { Text = text };
        b.SetBounds(left, top, width, height);
        Theme.StyleButton(b);
        b.Click += (_, _) => onClick();
        return b;
    }

    // -------------------------------------------------------------- data

    public void Reload()
    {
        _entries = _store.LoadAll();
        _stored = new Dictionary<DateTime, double>();
        foreach (var entry in _entries)
        {
            for (var day = entry.Start.Date; day <= entry.Stop.Date; day = day.AddDays(1))
            {
                var hours = entry.HoursOn(day);
                if (hours <= 0) continue;
                _stored[day] = _stored.TryGetValue(day, out var existing) ? existing + hours : hours;
            }
        }

        _dataDate = DateTime.Today;
        _trackingStart = _entries.Count > 0 ? _entries[0].Start.Date : null;
        _storedDayStart = null;
        foreach (var entry in _entries)
        {
            if (entry.HoursOn(_dataDate) <= 0) continue;
            var start = entry.Start < _dataDate ? _dataDate : entry.Start;   // clip a session from yesterday
            if (_storedDayStart is null || start < _storedDayStart) _storedDayStart = start;
        }

        _calendar.Invalidate();
        UpdateMonthLabel();
        UpdateTotals();
        UpdateStatus();
        RefreshDayPanel();
    }

    /// <summary>Stored hours for a day plus the part of the running session that falls on it.</summary>
    private double HoursForDay(DateTime day)
    {
        _stored.TryGetValue(day.Date, out var hours);
        return hours + _watcher.LiveHoursOn(day);
    }

    /// <summary>
    /// What was expected of a given day. Nothing is expected of a day in the future - it is not due
    /// yet - nor of a day before the log begins, so the first partial month does not open with a
    /// deficit for days that were never tracked.
    /// </summary>
    private double ExpectedOn(DateTime day)
    {
        if (day > DateTime.Today) return 0d;
        if (_trackingStart is null || day < _trackingStart) return 0d;
        return _settings.ExpectedHoursOn(day);
    }

    /// <summary>Hours over or under expectation, or null when there is nothing to compare.</summary>
    private double? DayBalance(DateTime day)
    {
        var expected = ExpectedOn(day);
        var actual = HoursForDay(day);
        if (expected <= 0 && actual <= 0.0049) return null;
        return actual - expected;
    }

    private double? WeekBalance(DateTime weekStart)
    {
        double expected = 0, actual = 0;
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            expected += ExpectedOn(day);
            actual += HoursForDay(day);
        }
        if (expected <= 0 && actual <= 0.0049) return null;
        return actual - expected;
    }

    private static string Fmt(double hours) => hours.ToString("0.00", CultureInfo.InvariantCulture);

    private void UpdateMonthLabel() =>
        _monthLabel.Text = _calendar.Month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    private void UpdateTotals()
    {
        var today = DateTime.Today;
        var weekStart = StartOfWeek(_calendar.SelectedDate);
        var weekNo = ISOWeek.GetWeekOfYear(weekStart.AddDays(3));

        double weekActual = 0, weekExpected = 0;
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            weekActual += HoursForDay(day);
            weekExpected += ExpectedOn(day);
        }

        var month = _calendar.Month;
        double monthActual = 0, monthExpected = 0, monthTarget = 0;
        var workingDaysLeft = 0;
        for (var i = 0; i < DateTime.DaysInMonth(month.Year, month.Month); i++)
        {
            var day = month.AddDays(i);
            monthActual += HoursForDay(day);
            monthExpected += ExpectedOn(day);

            if (_trackingStart is null || day < _trackingStart) continue;
            var full = _settings.ExpectedHoursOn(day);      // the whole month, due or not
            monthTarget += full;
            if (day > today && full > 0) workingDaysLeft++;
        }

        _totalsLabel.Text =
            $"Today  {Fmt(HoursForDay(today))} / {Fmt(ExpectedOn(today))} h" +
            $"        Week {weekNo}  {Fmt(weekActual)} / {Fmt(weekExpected)} h" +
            $"        {month:MMMM}  {Fmt(monthActual)} / {Fmt(monthExpected)} h";

        // Flexible time does not reset on the first of the month, so neither does the balance: what
        // every earlier month came out over or under is carried in, and the headline figure is the
        // running total rather than this month in isolation.
        var carriedIn = CarriedInto(month);
        var monthBalance = monthActual - monthExpected;
        var balance = carriedIn + monthBalance;

        _balanceLabel.Text = $"Balance {CalendarView.Signed(balance)} h";
        _balanceLabel.ForeColor = Math.Abs(balance) < 0.005 ? Theme.TextDim
                                : balance > 0 ? Theme.Ahead : Theme.Behind;

        var isThisMonth = month.Year == today.Year && month.Month == today.Month;
        var breakdown = $"{CalendarView.Signed(carriedIn)} h brought forward, " +
                        $"{CalendarView.Signed(monthBalance)} h in {month:MMMM}";
        _monthTargetLabel.Text = isThisMonth
            ? breakdown + $"  ·  target {Fmt(monthTarget)} h  ·  {Fmt(Math.Max(0, monthTarget - monthActual))} h " +
              $"left over the {workingDaysLeft} working days after today"
            : breakdown + $"  ·  target {Fmt(monthTarget)} h";
    }

    /// <summary>
    /// Everything over or under expectation from the first logged day up to the day before this
    /// month starts. Months do not settle up on their own, so a surplus or a deficit follows you
    /// into the next one.
    /// </summary>
    private double CarriedInto(DateTime month)
    {
        if (_trackingStart is null) return 0d;

        double balance = 0;
        for (var day = _trackingStart.Value; day < month.Date; day = day.AddDays(1))
            balance += HoursForDay(day) - ExpectedOn(day);
        return balance;
    }

    private DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek - (int)_settings.FirstDayOfWeek + 7) % 7;
        return date.Date.AddDays(-offset);
    }

    private void UpdateStatus()
    {
        switch (_watcher.State)
        {
            case WorkState.Working:
                var since = WorkStartToday();
                var worked = TimeSpan.FromHours(HoursForDay(DateTime.Today));
                _statusLabel.ForeColor = Theme.Working;
                _statusLabel.Text = $"Working since {since:HH:mm:ss}   worked " +
                                    $"{(int)worked.TotalHours:00}:{worked.Minutes:00}:{worked.Seconds:00}";
                break;
            case WorkState.Paused:
                var held = DateTime.Now - _watcher.PausedSince;
                _statusLabel.ForeColor = Theme.Attention;
                _statusLabel.Text = $"PAUSED by you at {_watcher.PausedSince:HH:mm:ss}   " +
                                    $"{(int)held.TotalHours:00}:{held.Minutes:00}:{held.Seconds:00} not counted";
                break;
            case WorkState.Idle:
                _statusLabel.ForeColor = Theme.Paused;
                _statusLabel.Text = $"Paused - idle since {_watcher.LastInput:HH:mm:ss}";
                break;
            default:
                _statusLabel.ForeColor = Theme.Paused;
                _statusLabel.Text = "Paused - screen locked";
                break;
        }

        UpdatePauseButton();
    }

    private void TogglePause() => _watcher.ManuallyPaused = !_watcher.ManuallyPaused;

    /// <summary>The button shouts while a manual pause is on, so it cannot be forgotten.</summary>
    private void UpdatePauseButton()
    {
        if (_watcher.ManuallyPaused)
        {
            _pauseButton.Text = "Resume";
            _pauseButton.BackColor = Theme.Attention;
            _pauseButton.ForeColor = Color.White;
            _pauseButton.FlatAppearance.BorderColor = Theme.Attention;
        }
        else
        {
            _pauseButton.Text = "Pause";
            _pauseButton.BackColor = Theme.Surface;
            _pauseButton.ForeColor = Theme.Text;
            _pauseButton.FlatAppearance.BorderColor = Theme.Border;
        }
    }

    /// <summary>
    /// When the working day began: the first logged start of today, or the start of the running
    /// session if that is earlier. A break does not restart it - "working since" is about the day,
    /// not about the session that happens to be open.
    /// </summary>
    private DateTime WorkStartToday()
    {
        var today = DateTime.Today;
        var since = _storedDayStart;
        if (_watcher.IsWorking)
        {
            var start = _watcher.CurrentStart < today ? today : _watcher.CurrentStart;
            if (since is null || start < since) since = start;
        }
        return since ?? DateTime.Now;
    }

    private void Live()
    {
        if (!Visible) return;
        if (_dataDate != DateTime.Today) { Reload(); return; }   // past midnight, redraw for the new day

        UpdateStatus();

        // While the clock runs, today's cell and every sum change continuously. Repainting once a
        // second keeps them honest; comparing against the last painted value instead made the
        // number lag by up to 18 seconds, so it only looked right after a click forced a redraw.
        if (_watcher.IsWorking)
        {
            _calendar.Invalidate();
            UpdateTotals();
            if (_calendar.SelectedDate == DateTime.Today) UpdateDayTotalLabel();
        }

        if (_runningItem is not null && _runningItem.ListView is not null)
        {
            var day = _calendar.SelectedDate;
            var live = new TimeEntry(_watcher.CurrentStart, DateTime.Now);
            _runningItem.SubItems[2].Text = Fmt(live.HoursOn(day));
        }
    }

    // -------------------------------------------------------- day panel

    private void RefreshDayPanel()
    {
        var day = _calendar.SelectedDate;
        _dayTitle.Text = day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
        UpdateDayTotalLabel();
        UpdateTotals();

        _list.BeginUpdate();
        _list.Items.Clear();
        _runningItem = null;

        foreach (var entry in _entries.Where(e => e.HoursOn(day) > 0).OrderBy(e => e.Start))
        {
            var item = new ListViewItem(new[]
            {
                Stamp(entry.Start, day),
                Stamp(entry.Stop, day),
                Fmt(entry.HoursOn(day))
            })
            { Tag = entry };
            _list.Items.Add(item);
        }

        if (_watcher.IsWorking)
        {
            var live = new TimeEntry(_watcher.CurrentStart, DateTime.Now);
            if (live.HoursOn(day) > 0)
            {
                _runningItem = new ListViewItem(new[]
                {
                    Stamp(_watcher.CurrentStart, day),
                    "running...",
                    Fmt(live.HoursOn(day))
                })
                { ForeColor = Theme.Working, Tag = null };
                _list.Items.Add(_runningItem);
            }
        }

        _list.EndUpdate();
    }

    /// <summary>Time of day, with a marker when the session started or ended on another day.</summary>
    private static string Stamp(DateTime value, DateTime day)
    {
        var diff = (value.Date - day.Date).Days;
        var time = value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return diff == 0 ? time : $"{time} ({diff:+0;-0} d)";
    }

    private void UpdateDayTotalLabel()
    {
        var day = _calendar.SelectedDate;
        _dayTotal.Text = Fmt(HoursForDay(day)) + " h";
        _dayTotal.ForeColor = day == DateTime.Today && _watcher.IsWorking ? Theme.Working : Theme.Text;

        var target = _settings.ExpectedHoursOn(day);
        var balance = DayBalance(day);

        if (target <= 0 && balance is null)
        {
            _dayTarget.Text = "no target this day";
            _dayTarget.ForeColor = Theme.TextDim;
            return;
        }

        _dayTarget.Text = $"target {Fmt(target)} h" +
                          (balance is null ? "" : $"      {CalendarView.Signed(balance.Value)} h");
        _dayTarget.ForeColor = balance is null || Math.Abs(balance.Value) < 0.005 ? Theme.TextDim
                             : balance.Value > 0 ? Theme.Ahead : Theme.Behind;
    }

    // ------------------------------------------------------------- edits

    private TimeEntry? SelectedEntry =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as TimeEntry : null;

    private void AddEntry()
    {
        var day = _calendar.SelectedDate;
        var seed = new TimeEntry(day.AddHours(9), day.AddHours(17));
        var result = EntryEditForm.Run(this, seed, isNew: true, _entries);
        if (result is null) return;
        _entries.Add(result);
        Persist();
    }

    private void EditEntry()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            if (_list.SelectedItems.Count > 0)
                MessageBox.Show(this, "The running session cannot be edited. Lock the screen to close it first.",
                    "WorkTimeTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = EntryEditForm.Run(this, entry, isNew: false, _entries);
        if (result is null) return;
        entry.Start = result.Start;
        entry.Stop = result.Stop;
        Persist();
    }

    private void DeleteEntry()
    {
        var entry = SelectedEntry;
        if (entry is null) return;
        var answer = MessageBox.Show(this, $"Delete this session?\n\n{entry.ToCsv()}",
            "WorkTimeTray", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        _entries.Remove(entry);
        Persist();
    }

    private void Persist()
    {
        try
        {
            _store.SaveAll(_entries);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not write the log:\n\n" + ex.Message, "WorkTimeTray",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Reload();
    }

    private void OpenLogFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _store.DirectoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("Failed to open log folder", ex);
        }
    }

    // ------------------------------------------------------------ window

    public void ShowAndActivate()
    {
        Reload();
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The X button hides to the tray; only the tray menu really quits.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; return; }
        if (e.KeyCode == Keys.F5) { Reload(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.P) { TogglePause(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) Reload();
    }
}
