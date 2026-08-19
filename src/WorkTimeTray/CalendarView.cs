using System.Globalization;

namespace WorkTimeTray;

/// <summary>
/// Owner drawn month grid. Each cell shows the day number and the hours worked that day with
/// two decimals; a trailing column shows the ISO week number and the week sum.
/// </summary>
public sealed class CalendarView : Control
{
    private const int Rows = 6;
    private const float WeekColRatio = 0.85f;

    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selected = DateTime.Today;
    private Font? _dayFont, _hoursFont, _smallFont, _headerFont;

    public CalendarView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Font = new Font("Segoe UI", 9.75f);
    }

    /// <summary>Any day inside the month that is displayed.</summary>
    public DateTime Month
    {
        get => _month;
        set
        {
            var first = new DateTime(value.Year, value.Month, 1);
            if (first == _month) return;
            _month = first;
            Invalidate();
            MonthChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DateTime SelectedDate
    {
        get => _selected;
        set
        {
            var d = value.Date;
            if (d == _selected) return;
            _selected = d;
            if (d.Year != _month.Year || d.Month != _month.Month) Month = d;
            Invalidate();
            SelectedDateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Func<DateTime, double> HoursProvider { get; set; } = _ => 0d;

    /// <summary>Hours over (+) or under (-) what was expected that day; null hides the figure.</summary>
    public Func<DateTime, double?> DayBalanceProvider { get; set; } = _ => null;

    /// <summary>Same for a whole week, given its first day.</summary>
    public Func<DateTime, double?> WeekBalanceProvider { get; set; } = _ => null;
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    public event EventHandler? SelectedDateChanged;
    public event EventHandler? MonthChanged;
    public event EventHandler? DateActivated;

    public DateTime GridStart
    {
        get
        {
            var offset = ((int)_month.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
            return _month.AddDays(-offset);
        }
    }

    public double WeekSum(DateTime weekStart)
    {
        double sum = 0;
        for (var i = 0; i < 7; i++) sum += HoursProvider(weekStart.AddDays(i));
        return sum;
    }

    public double MonthSum()
    {
        double sum = 0;
        var days = DateTime.DaysInMonth(_month.Year, _month.Month);
        for (var i = 0; i < days; i++) sum += HoursProvider(_month.AddDays(i));
        return sum;
    }

    // ------------------------------------------------------------ layout

    private float DayWidth => (ClientSize.Width - 1) / (7f + WeekColRatio);
    private float WeekWidth => DayWidth * WeekColRatio;
    private float HeaderHeight => Font.Height + 14f;
    private float RowHeight => (ClientSize.Height - HeaderHeight - 1) / Rows;

    private RectangleF CellRect(int row, int col) =>
        new(col * DayWidth, HeaderHeight + row * RowHeight, DayWidth, RowHeight);

    private RectangleF WeekRect(int row) =>
        new(7 * DayWidth, HeaderHeight + row * RowHeight, WeekWidth, RowHeight);

    public DateTime? DateAt(Point p)
    {
        if (p.Y < HeaderHeight) return null;
        var row = (int)((p.Y - HeaderHeight) / RowHeight);
        if (row < 0 || row >= Rows) return null;
        var col = p.X >= 7 * DayWidth ? 0 : (int)(p.X / DayWidth);
        if (col < 0 || col > 6) return null;
        return GridStart.AddDays(row * 7 + col);
    }

    // ------------------------------------------------------------- paint

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        DisposeFonts();
        Invalidate();
    }

    private void EnsureFonts()
    {
        _dayFont ??= new Font(Font.FontFamily, Font.SizeInPoints, FontStyle.Regular);
        _hoursFont ??= new Font(Font.FontFamily, Font.SizeInPoints + 3.5f, FontStyle.Bold);
        _smallFont ??= new Font(Font.FontFamily, Font.SizeInPoints - 1.25f, FontStyle.Regular);
        _headerFont ??= new Font(Font.FontFamily, Font.SizeInPoints - 0.5f, FontStyle.Bold);
    }

    private void DisposeFonts()
    {
        _dayFont?.Dispose(); _hoursFont?.Dispose(); _smallFont?.Dispose(); _headerFont?.Dispose();
        _dayFont = _hoursFont = _smallFont = _headerFont = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureFonts();
        var g = e.Graphics;
        g.Clear(Theme.Background);

        var culture = CultureInfo.CurrentCulture;
        using var borderPen = new Pen(Theme.Border);
        using var accentPen = new Pen(Theme.Accent, 2f);

        // header row
        for (var col = 0; col < 7; col++)
        {
            var day = (DayOfWeek)(((int)FirstDayOfWeek + col) % 7);
            var name = culture.DateTimeFormat.GetAbbreviatedDayName(day).ToUpper(culture);
            var rect = new RectangleF(col * DayWidth, 0, DayWidth, HeaderHeight);
            TextRenderer.DrawText(g, name, _headerFont, Rectangle.Round(rect), Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
        }
        TextRenderer.DrawText(g, "WEEK", _headerFont,
            Rectangle.Round(new RectangleF(7 * DayWidth, 0, WeekWidth, HeaderHeight)), Theme.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);

        var gridStart = GridStart;
        var today = DateTime.Today;

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < 7; col++)
            {
                var date = gridStart.AddDays(row * 7 + col);
                var rect = CellRect(row, col);
                var inMonth = date.Month == _month.Month && date.Year == _month.Year;
                var isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
                var isSelected = date == _selected;

                var back = !inMonth ? Theme.SurfaceDim : isWeekend ? Theme.Weekend : Theme.Surface;
                if (isSelected) back = Theme.AccentSoft;
                using (var brush = new SolidBrush(back)) g.FillRectangle(brush, rect);
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
                if (date == today)
                    g.DrawRectangle(accentPen, rect.X + 1.5f, rect.Y + 1.5f, rect.Width - 3f, rect.Height - 3f);

                var numColor = inMonth ? (date == today ? Theme.Accent : Theme.Text) : Theme.TextDim;
                var numRect = new RectangleF(rect.X + 6, rect.Y + 4, rect.Width - 12, _dayFont!.Height + 2);
                TextRenderer.DrawText(g, date.Day.ToString(culture), _dayFont, Rectangle.Round(numRect),
                    numColor, TextFormatFlags.Left | TextFormatFlags.Top);

                var hours = HoursProvider(date);
                if (hours > 0.0049)
                {
                    var text = hours.ToString("0.00", CultureInfo.InvariantCulture);
                    var hRect = new RectangleF(rect.X + 4, rect.Y + rect.Height * 0.26f,
                                               rect.Width - 8, rect.Height * 0.52f);
                    TextRenderer.DrawText(g, text, _hoursFont, Rectangle.Round(hRect),
                        inMonth ? Theme.Text : Theme.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                var balance = DayBalanceProvider(date);
                if (balance is not null)
                {
                    var bRect = new RectangleF(rect.X + 4, rect.Y + rect.Height * 0.70f,
                                               rect.Width - 8, rect.Height * 0.28f);
                    TextRenderer.DrawText(g, Signed(balance.Value), _smallFont, Rectangle.Round(bRect),
                        BalanceColor(balance.Value, inMonth),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            // week column
            var weekStart = gridStart.AddDays(row * 7);
            var wRect = WeekRect(row);
            using (var brush = new SolidBrush(Theme.SurfaceDim)) g.FillRectangle(brush, wRect);
            g.DrawRectangle(borderPen, wRect.X, wRect.Y, wRect.Width, wRect.Height);

            var weekNo = ISOWeek.GetWeekOfYear(weekStart.AddDays(3)); // the Thursday decides the ISO week
            TextRenderer.DrawText(g, "w" + weekNo.ToString(culture), _smallFont,
                Rectangle.Round(new RectangleF(wRect.X, wRect.Y + 4, wRect.Width, _smallFont!.Height + 2)),
                Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

            var sum = WeekSum(weekStart);
            if (sum > 0.0049)
                TextRenderer.DrawText(g, sum.ToString("0.00", CultureInfo.InvariantCulture), _dayFont,
                    Rectangle.Round(new RectangleF(wRect.X, wRect.Y + wRect.Height * 0.26f,
                                                   wRect.Width, wRect.Height * 0.52f)),
                    Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var weekBalance = WeekBalanceProvider(weekStart);
            if (weekBalance is not null)
                TextRenderer.DrawText(g, Signed(weekBalance.Value), _smallFont,
                    Rectangle.Round(new RectangleF(wRect.X, wRect.Y + wRect.Height * 0.70f,
                                                   wRect.Width, wRect.Height * 0.28f)),
                    BalanceColor(weekBalance.Value, true),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (Focused)
        {
            using var focusPen = new Pen(Theme.Accent) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawRectangle(focusPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    /// <summary>"+1.20" / "-0.35" / "0.00".</summary>
    public static string Signed(double hours) =>
        (hours > 0.0049 ? "+" : "") + hours.ToString("0.00", CultureInfo.InvariantCulture);

    private static Color BalanceColor(double hours, bool strong)
    {
        if (Math.Abs(hours) < 0.005) return Theme.TextDim;
        var c = hours > 0 ? Theme.Ahead : Theme.Behind;
        return strong ? c : Color.FromArgb(140, c);
    }

    // ------------------------------------------------------------- input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var date = DateAt(e.Location);
        if (date is not null) SelectedDate = date.Value;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (DateAt(e.Location) is not null) DateActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Month = _month.AddMonths(e.Delta > 0 ? -1 : 1);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Left:
            case Keys.Right:
            case Keys.Up:
            case Keys.Down:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Home:
                return true;
            default:
                return base.IsInputKey(keyData);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left: SelectedDate = _selected.AddDays(-1); break;
            case Keys.Right: SelectedDate = _selected.AddDays(1); break;
            case Keys.Up: SelectedDate = _selected.AddDays(-7); break;
            case Keys.Down: SelectedDate = _selected.AddDays(7); break;
            case Keys.PageUp: SelectedDate = _selected.AddMonths(-1); break;
            case Keys.PageDown: SelectedDate = _selected.AddMonths(1); break;
            case Keys.Home: SelectedDate = DateTime.Today; break;
            case Keys.Enter: DateActivated?.Invoke(this, EventArgs.Empty); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeFonts();
        base.Dispose(disposing);
    }
}
