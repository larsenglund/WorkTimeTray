using System.Text;

namespace WorkTimeTray;

/// <summary>
/// Reads and writes the plain text log. One file per year: worktime-YYYY.csv,
/// lines are "start,stop,hours" sorted by start time.
/// </summary>
public sealed class TimeLogStore
{
    private readonly string _directory;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public TimeLogStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public string FileForYear(int year) => Path.Combine(_directory, $"worktime-{year}.csv");

    public IEnumerable<string> ExistingFiles() =>
        System.IO.Directory.EnumerateFiles(_directory, "worktime-*.csv").OrderBy(f => f);

    public List<TimeEntry> LoadAll()
    {
        var entries = new List<TimeEntry>();
        foreach (var file in ExistingFiles())
        {
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    var entry = TimeEntry.TryParse(line);
                    if (entry is not null) entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read " + file, ex);
            }
        }
        entries.Sort((a, b) => a.Start.CompareTo(b.Start));
        return entries;
    }

    public void Append(TimeEntry entry)
    {
        var file = FileForYear(entry.Start.Year);
        Retry(() =>
        {
            var needHeader = !File.Exists(file) || new FileInfo(file).Length == 0;
            using var writer = new StreamWriter(file, append: true, Utf8NoBom);
            if (needHeader) writer.WriteLine(TimeEntry.Header);
            writer.WriteLine(entry.ToCsv());
        }, "append entry to " + file);
    }

    /// <summary>Rewrites every year file so that it matches the given set of entries.</summary>
    public void SaveAll(IEnumerable<TimeEntry> entries)
    {
        var byYear = entries.OrderBy(e => e.Start)
                            .GroupBy(e => e.Start.Year)
                            .ToDictionary(g => g.Key, g => g.ToList());

        var years = new HashSet<int>(byYear.Keys);
        foreach (var file in ExistingFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(name.AsSpan("worktime-".Length), out var y)) years.Add(y);
        }

        foreach (var year in years)
        {
            byYear.TryGetValue(year, out var list);
            var file = FileForYear(year);
            var sb = new StringBuilder();
            sb.AppendLine(TimeEntry.Header);
            foreach (var e in list ?? new List<TimeEntry>()) sb.AppendLine(e.ToCsv());
            Retry(() => File.WriteAllText(file, sb.ToString(), Utf8NoBom), "rewrite " + file);
        }
    }

    private static void Retry(Action action, string what)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException ex) when (attempt < 4)
            {
                Log.Error($"Retrying to {what} ({ex.Message})");
                Thread.Sleep(150 * (attempt + 1));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to " + what, ex);
                throw;
            }
        }
    }
}
