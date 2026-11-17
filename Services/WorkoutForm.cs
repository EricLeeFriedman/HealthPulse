using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>A set row as typed on the workout screen.</summary>
public record SetInput(string Exercise, bool Timed, string? Amount, string? Weight);

/// <summary>Turns typed workout rows into stored sets.</summary>
public static class WorkoutForm
{
    /// <summary>
    /// Rows without reps (or seconds) are skipped as not done, even when the
    /// weight was prefilled from last time.
    /// </summary>
    public static List<WorkoutSet> BuildSets(IEnumerable<SetInput> rows)
    {
        var sets = new List<WorkoutSet>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Amount)) continue;
            var amount = TrackerRules.ParseInt(row.Amount, $"{row.Exercise} {(row.Timed ? "seconds" : "reps")}");
            sets.Add(new WorkoutSet
            {
                Exercise = row.Exercise.Trim(),
                Reps = row.Timed ? null : amount,
                Seconds = row.Timed ? amount : null,
                WeightLbs = TrackerRules.ParseDouble(row.Weight, $"{row.Exercise} weight"),
                Order = sets.Count,
            });
        }
        return sets;
    }
}
