using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>
/// The migraine editor's fields, with the rules for turning them into a saved
/// entry. Kept free of MAUI so it can be unit tested.
/// </summary>
public class MigraineForm
{
    public const string ReliefUnknown = "Not yet known";
    public static readonly IReadOnlyList<string> ReliefChoices = [ReliefUnknown, .. MigraineEntry.ReliefOptions];

    public DateTime StartDate { get; set; }
    public TimeSpan StartClock { get; set; }
    public bool IsOngoing { get; set; }
    public DateTime EndDate { get; set; }
    public TimeSpan EndClock { get; set; }
    public double Severity { get; set; }
    public bool Aura { get; set; }
    public bool Nausea { get; set; }
    public bool LightSensitivity { get; set; }
    public bool SoundSensitivity { get; set; }
    public bool TriptanTaken { get; set; }
    public DateTime TriptanDate { get; set; }
    public TimeSpan TriptanClock { get; set; }
    public int ReliefIndex { get; set; }
    public string OtherMeds { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<(string Name, bool Selected)> Triggers { get; set; } = [];

    public static MigraineForm FromEntry(MigraineEntry entry, DateTime now)
    {
        var end = entry.EndTime ?? now;
        var triptan = entry.TriptanTime ?? now;
        var selected = entry.TriggerList.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new MigraineForm
        {
            StartDate = entry.StartTime.Date,
            StartClock = TrimSeconds(entry.StartTime.TimeOfDay),
            IsOngoing = entry.EndTime == null,
            EndDate = end.Date,
            EndClock = TrimSeconds(end.TimeOfDay),
            Severity = entry.PeakSeverity,
            Aura = entry.Aura,
            Nausea = entry.Nausea,
            LightSensitivity = entry.LightSensitivity,
            SoundSensitivity = entry.SoundSensitivity,
            TriptanTaken = entry.TriptanTaken,
            TriptanDate = triptan.Date,
            TriptanClock = TrimSeconds(triptan.TimeOfDay),
            ReliefIndex = entry.TriptanRelief == null
                ? 0
                : Math.Max(0, Array.IndexOf(MigraineEntry.ReliefOptions, entry.TriptanRelief) + 1),
            OtherMeds = entry.OtherMeds ?? "",
            Notes = entry.Notes ?? "",
            // Standard triggers first, then any custom ones saved earlier.
            Triggers = MigraineEntry.TriggerOptions
                .Concat(entry.TriggerList.Where(t => !MigraineEntry.TriggerOptions.Contains(t, StringComparer.OrdinalIgnoreCase)))
                .Select(name => (name, selected.Contains(name)))
                .ToList()
        };
    }

    public void ApplyTo(MigraineEntry entry)
    {
        entry.StartTime = StartDate.Date + StartClock;
        entry.EndTime = IsOngoing ? null : EndDate.Date + EndClock;
        entry.PeakSeverity = Math.Clamp((int)Math.Round(Severity), 0, 10);
        entry.Aura = Aura;
        entry.Nausea = Nausea;
        entry.LightSensitivity = LightSensitivity;
        entry.SoundSensitivity = SoundSensitivity;
        entry.TriptanTaken = TriptanTaken;
        entry.TriptanTime = TriptanTaken ? TriptanDate.Date + TriptanClock : null;
        entry.TriptanRelief = TriptanTaken && ReliefIndex > 0 && ReliefIndex <= MigraineEntry.ReliefOptions.Length
            ? MigraineEntry.ReliefOptions[ReliefIndex - 1]
            : null;
        entry.OtherMeds = Clean(OtherMeds);
        entry.Notes = Clean(Notes);
        entry.TriggerList = Triggers.Where(t => t.Selected).Select(t => t.Name).ToList();
    }

    static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    static TimeSpan TrimSeconds(TimeSpan time) => new(time.Hours, time.Minutes, 0);
}
