using HealthPulse.Models;

namespace HealthPulse.Services;

public static class MigraineStats
{
    /// <summary>
    /// Attacks, migraine days, and triptan days over the last 30 days. Triptan
    /// days matter because using a triptan on 10+ days a month risks
    /// medication-overuse headache.
    /// </summary>
    public static (int Attacks, int MigraineDays, int TriptanDays) Last30Days(IEnumerable<MigraineEntry> entries, DateTime now)
    {
        var cutoff = now.AddDays(-30);
        var recent = entries.Where(e => e.StartTime >= cutoff && e.StartTime <= now).ToList();
        var migraineDays = recent.Select(e => e.StartTime.Date).Distinct().Count();
        var triptanDays = recent.Where(e => e.TriptanTaken)
            .Select(e => (e.TriptanTime ?? e.StartTime).Date).Distinct().Count();
        return (recent.Count, migraineDays, triptanDays);
    }

    public static string Describe(IEnumerable<MigraineEntry> entries, DateTime now)
    {
        var (attacks, migraineDays, triptanDays) = Last30Days(entries, now);
        var text = $"Last 30 days: {attacks} attack{(attacks == 1 ? "" : "s")} on {migraineDays} day{(migraineDays == 1 ? "" : "s")} • triptan on {triptanDays} day{(triptanDays == 1 ? "" : "s")}";
        if (triptanDays >= 10)
            text += " ⚠️ 10+ triptan days can cause rebound headaches";
        return text;
    }

    public static string OngoingDescription(MigraineEntry entry, DateTime now) =>
        $"Started {entry.StartTime:h:mm tt} ({(now - entry.StartTime).TotalHours:0.#} h ago), pain {entry.PeakSeverity}/10"
        + (entry.TriptanTaken && entry.TriptanTime.HasValue ? $", triptan at {entry.TriptanTime.Value:h:mm tt}" : "");
}
