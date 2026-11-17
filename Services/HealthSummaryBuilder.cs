using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>
/// Turns raw Health Connect records into one summary per local day. Kept free
/// of Android types so the rules can be unit tested.
/// </summary>
public static class HealthSummaryBuilder
{
    public static Dictionary<DateOnly, DailyHealthSummary> CreateDays(DateOnly from, DateOnly to)
    {
        var days = new Dictionary<DateOnly, DailyHealthSummary>();
        for (var date = from; date <= to; date = date.AddDays(1))
            days[date] = new DailyHealthSummary { Date = date };
        return days;
    }

    /// <summary>
    /// Sleep belongs to the local date it ends on, so Tuesday's sleep is the
    /// night from Monday into Tuesday. Naps add to the total; the longest
    /// session's start/end are kept as the main night.
    /// </summary>
    public static void AddSleepSession(Dictionary<DateOnly, DailyHealthSummary> days, DateTime start, DateTime end)
    {
        if (end <= start) return;
        if (!days.TryGetValue(DateOnly.FromDateTime(end), out var day)) return;

        var hours = (end - start).TotalHours;
        var mainHours = day.SleepStart.HasValue && day.SleepEnd.HasValue
            ? (day.SleepEnd.Value - day.SleepStart.Value).TotalHours
            : 0;
        day.SleepHours = Math.Round((day.SleepHours ?? 0) + hours, 2);
        if (hours > mainHours)
        {
            day.SleepStart = start;
            day.SleepEnd = end;
        }
    }

    /// <summary>Daily HRV is the mean of that day's RMSSD readings.</summary>
    public static void SetHrv(Dictionary<DateOnly, DailyHealthSummary> days, IEnumerable<(DateTime Time, double Millis)> samples)
    {
        foreach (var group in samples.GroupBy(s => DateOnly.FromDateTime(s.Time)))
        {
            if (days.TryGetValue(group.Key, out var day))
                day.HrvRmssdMs = group.Average(s => s.Millis);
        }
    }

    /// <summary>Exercise belongs to the date it starts on.</summary>
    public static void AddExercise(
        Dictionary<DateOnly, DailyHealthSummary> days, int exerciseType, string? title, DateTime start, DateTime end)
    {
        if (!days.TryGetValue(DateOnly.FromDateTime(start), out var day)) return;
        day.Exercise.Add(new ExerciseSummary
        {
            Type = ExerciseTypeName(exerciseType),
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Start = start,
            Minutes = Math.Max(0, (end - start).TotalMinutes)
        });
    }

    /// <summary>The last weigh-in of each day wins.</summary>
    public static void SetWeights(Dictionary<DateOnly, DailyHealthSummary> days, IEnumerable<(DateTime Time, double Pounds)> weights)
    {
        foreach (var weight in weights.OrderBy(w => w.Time))
        {
            if (days.TryGetValue(DateOnly.FromDateTime(weight.Time), out var day))
                day.WeightLbs = weight.Pounds;
        }
    }

    /// <summary>Names for Health Connect's ExerciseSessionRecord.EXERCISE_TYPE_* values.</summary>
    public static string ExerciseTypeName(int type) => type switch
    {
        0 => "other",
        8 => "biking",
        9 => "stationary_biking",
        13 => "calisthenics",
        25 => "elliptical",
        36 => "hiit",
        37 => "hiking",
        48 => "pilates",
        54 => "rowing_machine",
        56 => "running",
        70 => "strength_training",
        74 => "swimming",
        79 => "walking",
        81 => "weightlifting",
        83 => "yoga",
        _ => $"type_{type}"
    };
}
