using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>A set row as typed on the workout screen.</summary>
/// <param name="RepsInReserve">Reps left in the tank, 0 to 3 (3 means 3 or more); null when not picked.</param>
public record SetInput(string Exercise, bool Timed, string? Amount, string? Weight, int? RepsInReserve = null);

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
                RepsInReserve = row.RepsInReserve is >= 0 ? Math.Min(row.RepsInReserve.Value, 3) : null,
                Order = sets.Count,
            });
        }
        return sets;
    }
}
