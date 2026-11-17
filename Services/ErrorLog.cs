namespace HealthPulse.Services;

/// <summary>
/// Keeps the most recent unexpected errors (including crashes) in a small file
/// so they can be shown in the app and included in the export.
/// </summary>
public class ErrorLog
{
    public const int MaxEntries = 10;
    const string Separator = "\n-----\n";
    readonly string path;
    readonly object gate = new();

    public ErrorLog(string path)
    {
        this.path = path;
    }

    public void Record(string context, Exception exception, DateTime? now = null)
    {
        var text = $"{(now ?? DateTime.Now):yyyy-MM-dd HH:mm:ss} {context}: {exception}";
        lock (gate)
        {
            try
            {
                var entries = ReadAllUnlocked();
                entries.Insert(0, text.Length > 4000 ? text[..4000] : text);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Join(Separator, entries.Take(MaxEntries)));
            }
            catch (IOException)
            {
                // Logging must never throw.
            }
        }
    }

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (gate) return ReadAllUnlocked();
        }
    }

    /// <summary>How long a problem stays on the Health tab and in the export.</summary>
    public static readonly TimeSpan ShowFor = TimeSpan.FromDays(2);

    /// <summary>Entries newer than <see cref="ShowFor"/>, so an old network blip ages out on its own.</summary>
    public IReadOnlyList<string> Recent(DateTime now) =>
        Entries.Where(entry => !DateTime.TryParseExact(entry.Length >= 19 ? entry[..19] : "", "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at)
            || now - at < ShowFor).ToList();

    public void Clear()
    {
        lock (gate)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    List<string> ReadAllUnlocked() =>
        File.Exists(path)
            ? File.ReadAllText(path).Split(Separator, StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];
}
