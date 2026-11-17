using SQLite;

namespace HealthPulse.Models;

/// <summary>One row per calendar day: the 30-second daily check-in.</summary>
public class DailyCheckIn
{
    /// <summary>Local date, yyyy-MM-dd.</summary>
    [PrimaryKey]
    public string Date { get; set; } = "";

    public int CaffeineCups { get; set; }
    /// <summary>Local time of the last caffeinated drink.</summary>
    public TimeSpan? LastCaffeineTime { get; set; }

    /// <summary>Glasses of water (about 8 oz / 250 ml each).</summary>
    public int WaterGlasses { get; set; }

    /// <summary>Local time of the first meal or snack of the day.</summary>
    public TimeSpan? FirstMealTime { get; set; }

    public int AlcoholDrinks { get; set; }

    /// <summary>Stress 1 (calm) to 5 (very stressed); null when not rated.</summary>
    public int? Stress { get; set; }

    public bool Riboflavin { get; set; }
    public bool BComplex { get; set; }
    public bool CoQ10 { get; set; }
    public bool Magnesium { get; set; }

    /// <summary>Rough protein estimate in grams.</summary>
    public int? ProteinGrams { get; set; }

    /// <summary>The mobility routine (planned for Sunday) was done today.</summary>
    public bool MobilityDone { get; set; }

    public string? Notes { get; set; }
    /// <summary>UTC time of the last save; DateTime.MinValue for a day with no check-in yet.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Ignore]
    public DateOnly DateValue => DateOnly.ParseExact(Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Ignore]
    public bool AllSupplementsTaken => Riboflavin && BComplex && CoQ10 && Magnesium;

    public static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A strength, run or mobility session.</summary>
public class WorkoutSession
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Local start time.</summary>
    [Indexed]
    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>One of WorkoutPlan.SessionTypes.</summary>
    public string SessionType { get; set; } = "";

    public int? DurationMinutes { get; set; }
    public double? DistanceMiles { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>"manual" or "health_connect".</summary>
    public string Source { get; set; } = ManualSource;

    /// <summary>Health Connect exercise session id for detected workouts.</summary>
    [Indexed]
    public string? ExternalId { get; set; }

    /// <summary>The watch's exercise type, e.g. running or strength_training.</summary>
    public string? ExerciseType { get; set; }
    public double? AvgHeartRate { get; set; }
    public double? MaxHeartRate { get; set; }

    /// <summary>A detected workout the user deleted; kept so it isn't imported again.</summary>
    public bool Hidden { get; set; }

    public const string ManualSource = "manual";
    public const string HealthConnectSource = "health_connect";

    [Ignore]
    public bool IsDetected => Source == HealthConnectSource;

    [Ignore]
    public List<WorkoutSet> Sets { get; set; } = [];

    [Ignore]
    public string DisplayDate => Date.ToString("ddd MMM d");

    [Ignore]
    public string Summary
    {
        get
        {
            var parts = new List<string> { IsDetected ? $"⌚ {SessionType}" : SessionType };
            if (DurationMinutes.HasValue) parts.Add($"{DurationMinutes} min");
            if (DistanceMiles.HasValue) parts.Add($"{DistanceMiles:0.##} mi");
            if (AvgHeartRate.HasValue) parts.Add($"avg {AvgHeartRate:0} bpm");
            var exercises = Sets.Select(s => s.Exercise).Distinct().Count();
            if (exercises > 0) parts.Add($"{exercises} exercises, {Sets.Count} sets");
            return string.Join(" • ", parts);
        }
    }
}

/// <summary>One set of one exercise in a workout.</summary>
public class WorkoutSet
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public string SessionId { get; set; } = "";

    public int Order { get; set; }
    public string Exercise { get; set; } = "";
    public int? Reps { get; set; }
    public double? WeightLbs { get; set; }
    public int? Seconds { get; set; }
}

/// <summary>Waist, weight and optional progress photos (photos stay on the phone).</summary>
public class BodyMeasurement
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public DateTime Date { get; set; } = DateTime.Now;

    public double? WaistInches { get; set; }
    public double? WeightLbs { get; set; }

    /// <summary>Local file paths of progress photos, separated by "|". Never exported.</summary>
    public string PhotoPaths { get; set; } = "";

    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Ignore]
    public IReadOnlyList<string> PhotoList
    {
        get => PhotoPaths.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        set => PhotoPaths = string.Join("|", value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
    }
}

/// <summary>Monthly fitness test: toe-touch reach, push-ups and plank.</summary>
public class FitnessTest
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public DateTime Date { get; set; } = DateTime.Now;

    /// <summary>Reach relative to the toes with straight legs: negative = short of the toes, 0 = touching, positive = past.</summary>
    public double? ToeTouchInches { get; set; }
    public int? PushUps { get; set; }
    public int? PlankSeconds { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Daily weather summary for the phone's coarse location.</summary>
public class WeatherDay
{
    /// <summary>Local date, yyyy-MM-dd.</summary>
    [PrimaryKey]
    public string Date { get; set; } = "";

    /// <summary>Sea-level pressure (hPa).</summary>
    public double? PressureMeanHpa { get; set; }
    public double? PressureMinHpa { get; set; }
    public double? PressureMaxHpa { get; set; }
    /// <summary>Mean pressure minus the previous day's mean.</summary>
    public double? PressureChangeHpa { get; set; }
    /// <summary>Largest fall over any 6 hours ending on this day (positive number = a drop).</summary>
    public double? PressureMaxDrop6hHpa { get; set; }
    public double? TempMaxF { get; set; }
    public double? TempMinF { get; set; }
    public double? HumidityMeanPct { get; set; }
    /// <summary>Hours of data behind the summary (24 for a full day).</summary>
    public int Hours { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}
