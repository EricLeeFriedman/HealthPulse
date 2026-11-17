namespace HealthPulse.Models;

/// <summary>
/// One day of data read from Android Health Connect. Every value is optional
/// because a source may not have written that type for the day.
/// </summary>
public class DailyHealthSummary
{
    public DateOnly Date { get; set; }

    public long? Steps { get; set; }

    /// <summary>Total sleep for sessions that ended on this date (the night before).</summary>
    public double? SleepHours { get; set; }
    public DateTime? SleepStart { get; set; }
    public DateTime? SleepEnd { get; set; }

    public double? AvgHeartRate { get; set; }
    public long? MinHeartRate { get; set; }
    public long? MaxHeartRate { get; set; }
    public double? RestingHeartRate { get; set; }
    public double? HrvRmssdMs { get; set; }
    public double? WeightLbs { get; set; }

    public List<ExerciseSummary> Exercise { get; set; } = [];

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (SleepHours.HasValue) parts.Add($"😴 {SleepHours:0.0} h");
            if (Steps.HasValue) parts.Add($"👣 {Steps:N0}");
            if (RestingHeartRate.HasValue) parts.Add($"❤️ {RestingHeartRate:0} rest");
            if (HrvRmssdMs.HasValue) parts.Add($"HRV {HrvRmssdMs:0} ms");
            if (Exercise.Count > 0) parts.Add($"🏋️ {Exercise.Sum(e => e.Minutes):0} min");
            if (WeightLbs.HasValue) parts.Add($"⚖️ {WeightLbs:0.#} lbs");
            return parts.Count > 0 ? string.Join("  ", parts) : "No data";
        }
    }

    public string DisplayDate => Date.ToString("ddd MMM d");
}

public class ExerciseSummary
{
    public string Type { get; set; } = "";
    public string? Title { get; set; }
    public DateTime Start { get; set; }
    public double Minutes { get; set; }
}
