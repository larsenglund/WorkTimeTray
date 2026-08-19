using System.Globalization;

namespace WorkTimeTray;

/// <summary>Add or edit a single work session by hand.</summary>
public sealed class EntryEditForm : Form
{
    private readonly DateTimePicker _startDate = new();
    private readonly DateTimePicker _startTime = new();
    private readonly DateTimePicker _stopDate = new();
    private readonly DateTimePicker _stopTime = new();
    private readonly Label _hours = new();
    private readonly Button _ok = new();
    private readonly Button _cancel = new();

    public DateTime Start => _startDate.Value.Date + _startTime.Value.TimeOfDay;
    public DateTime Stop => _stopDate.Value.Date + _stopTime.Value.TimeOfDay;

    public EntryEditForm(TimeEntry entry, bool isNew)
    {
        Text = isNew ? "Add work session" : "Edit work session";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(400, 190);
        Font = new Font("Segoe UI", 9.75f);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;

        var startLabel = new Label { Text = "Start", Left = 18, Top = 22, Width = 60, ForeColor = Theme.TextDim };
        var stopLabel = new Label { Text = "Stop", Left = 18, Top = 62, Width = 60, ForeColor = Theme.TextDim };
        var hoursCaption = new Label { Text = "Hours", Left = 18, Top = 106, Width = 60, ForeColor = Theme.TextDim };

        // Every field uses up/down spinners instead of the dropdown calendar. That calendar is a
        // native control and takes its month and weekday names from the Windows region setting, not
        // from the app culture, which would put Swedish names in an otherwise English window. Type
        // the date or arrow through the parts; the day you picked in the calendar is pre-filled.
        Configure(_startDate, "yyyy-MM-dd", 84, 18, 130);
        Configure(_startTime, "HH:mm:ss", 222, 18, 100);
        Configure(_stopDate, "yyyy-MM-dd", 84, 58, 130);
        Configure(_stopTime, "HH:mm:ss", 222, 58, 100);

        _startDate.Value = entry.Start.Date;
        _startTime.Value = entry.Start;
        _stopDate.Value = entry.Stop.Date;
        _stopTime.Value = entry.Stop;

        _hours.SetBounds(84, 104, 240, 24);
        _hours.Font = new Font(Font.FontFamily, Font.SizeInPoints + 2f, FontStyle.Bold);
        _hours.ForeColor = Theme.Text;

        _ok.Text = "Save";
        _ok.SetBounds(196, 144, 90, 30);
        _ok.DialogResult = DialogResult.OK;
        Theme.StyleButton(_ok);

        _cancel.Text = "Cancel";
        _cancel.SetBounds(294, 144, 90, 30);
        _cancel.DialogResult = DialogResult.Cancel;
        Theme.StyleButton(_cancel);

        AcceptButton = _ok;
        CancelButton = _cancel;

        Controls.AddRange(new Control[]
        {
            startLabel, stopLabel, hoursCaption,
            _startDate, _startTime, _stopDate, _stopTime, _hours, _ok, _cancel
        });

        foreach (var picker in new[] { _startDate, _startTime, _stopDate, _stopTime })
            picker.ValueChanged += (_, _) => UpdateHours();

        UpdateHours();
    }

    private static void Configure(DateTimePicker picker, string format, int left, int top, int width)
    {
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = format;
        picker.ShowUpDown = true;
        picker.SetBounds(left, top, width, 26);
    }

    private void UpdateHours()
    {
        var span = Stop - Start;
        if (span <= TimeSpan.Zero)
        {
            _hours.Text = "stop must be after start";
            _hours.ForeColor = Color.IndianRed;
            _ok.Enabled = false;
            return;
        }

        _hours.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.00} h   ({1:hh\\:mm\\:ss})",
            Math.Round(span.TotalHours, 2, MidpointRounding.AwayFromZero), span);
        _hours.ForeColor = Theme.Text;
        _ok.Enabled = true;
    }

    /// <summary>Shows the dialog and returns the edited entry, or null when cancelled.</summary>
    public static TimeEntry? Run(IWin32Window owner, TimeEntry seed, bool isNew, IEnumerable<TimeEntry> existing)
    {
        using var dialog = new EntryEditForm(seed, isNew);
        while (true)
        {
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
            var result = new TimeEntry(dialog.Start, dialog.Stop);
            if (result.Duration <= TimeSpan.Zero) continue;

            var clash = existing.FirstOrDefault(e => !ReferenceEquals(e, seed) && e.Overlaps(result));
            if (clash is not null)
            {
                var answer = MessageBox.Show(owner,
                    $"This overlaps an existing session:\n\n{clash.ToCsv()}\n\nSave anyway?",
                    "Overlapping session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) continue;
            }
            return result;
        }
    }
}
