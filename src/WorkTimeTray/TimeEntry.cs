using System.Globalization;

namespace WorkTimeTray;

/// <summary>One work session: the time between an unlock and the following lock.</summary>
public sealed class TimeEntry
{
    public DateTime Start { get; set; }
    public DateTime Stop { get; set; }

    public TimeEntry() { }

    public TimeEntry(DateTime start, DateTime stop)
    {
        Start = start;
        Stop = stop;
    }

    public TimeSpan Duration => Stop - Start;

    /// <summary>Duration in hours, rounded to 2 decimals - the value written to the csv.</summary>
    public double Hours => Math.Round(Duration.TotalHours, 2, MidpointRounding.AwayFromZero);

    /// <summary>Hours of this session that fall inside the given calendar day.</summary>
    public double HoursOn(DateTime day)
    {
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        var from = Start > dayStart ? Start : dayStart;
        var to = Stop < dayEnd ? Stop : dayEnd;
        return to > from ? (to - from).TotalHours : 0d;
    }

    public bool Overlaps(TimeEntry other) => Start < other.Stop && other.Start < Stop;

    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    public const string Header = "start,stop,hours";

    public string ToCsv() => string.Join(',',
        Start.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
        Stop.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
        Hours.ToString("0.00", CultureInfo.InvariantCulture));

    /// <summary>
    /// Parses a log line. The hours column is recomputed from the timestamps, so hand-edited
    /// files stay consistent even if only the start/stop columns were changed.
    /// </summary>
    public static TimeEntry? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        line = line.Trim();
        if (line.StartsWith('#')) return null;

        var parts = line.Split(',');
        if (parts.Length < 2) return null;
        if (!TryParseDateTime(parts[0], out var start)) return null;
        if (!TryParseDateTime(parts[1], out var stop)) return null;
        if (stop < start) return null;
        return new TimeEntry(start, stop);
    }

    private static readonly string[] AcceptedFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm"
    };

    private static bool TryParseDateTime(string text, out DateTime value) =>
        DateTime.TryParseExact(text.Trim(), AcceptedFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value);
}
