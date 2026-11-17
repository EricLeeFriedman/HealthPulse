using SQLite;

namespace HealthPulse.Models;

public class MigraineEntry
{
    public static readonly string[] TriggerOptions =
    [
        "Poor sleep",
        "Skipped meal",
        "Caffeine",
        "Alcohol",
        "Stress",
        "Weather change",
        "Dehydration",
        "Screen time"
    ];

    public static readonly string[] ReliefOptions = ["Full", "Partial", "None"];

    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public string UserId { get; set; } = "local";

    /// <summary>Local start time of the attack.</summary>
    [Indexed]
    public DateTime StartTime { get; set; } = DateTime.Now;

    /// <summary>Local end time; null while the attack is ongoing.</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Peak pain on a 0–10 scale.</summary>
    public int PeakSeverity { get; set; } = 5;

    public bool Aura { get; set; }
    public bool Nausea { get; set; }
    public bool LightSensitivity { get; set; }
    public bool SoundSensitivity { get; set; }

    public bool TriptanTaken { get; set; }
    public DateTime? TriptanTime { get; set; }

    /// <summary>Relief two hours after the triptan: Full, Partial, None, or null.</summary>
    public string? TriptanRelief { get; set; }

    public string? OtherMeds { get; set; }

    /// <summary>Selected triggers, separated by "|".</summary>
    public string Triggers { get; set; } = "";

    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Ignore]
    public IReadOnlyList<string> TriggerList
    {
        get => Triggers.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        set => Triggers = string.Join("|", value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
    }

    [Ignore]
    public bool IsOngoing => EndTime == null;

    [Ignore]
    public double? DurationHours =>
        EndTime.HasValue ? Math.Round((EndTime.Value - StartTime).TotalHours, 1) : null;

    [Ignore]
    public string DisplayStart => StartTime.ToString("ddd MMM d, h:mm tt");

    [Ignore]
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"Pain {PeakSeverity}/10" };
            parts.Add(IsOngoing ? "ongoing" : $"{DurationHours:0.#} h");
            if (TriptanTaken)
                parts.Add(TriptanRelief != null ? $"triptan ({TriptanRelief.ToLowerInvariant()} relief)" : "triptan");
            if (Aura) parts.Add("aura");
            return string.Join(" • ", parts);
        }
    }

    [Ignore]
    public string TriggerSummary => string.Join(", ", TriggerList);

    [Ignore]
    public bool HasTriggers => TriggerList.Count > 0;
}
