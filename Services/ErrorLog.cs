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
