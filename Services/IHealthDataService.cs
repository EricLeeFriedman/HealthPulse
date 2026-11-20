using HealthPulse.Models;

namespace HealthPulse.Services;

public enum HealthDataAvailability
{
    NotSupported,
    NeedsInstallOrUpdate,
    Available
}

public interface IHealthDataService
{
    HealthDataAvailability GetAvailability();
    Task<bool> HasPermissionsAsync();
    Task<bool> RequestPermissionsAsync();
    /// <summary>Returns one summary per date from <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
    Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to);

    /// <summary>How each step of the last read went (time taken, errors), for the export.</summary>
    IReadOnlyList<string> LastReadSteps => [];

    /// <summary>
    /// Only the exercise sessions in the range, read fresh (not from the shared cache), for
    /// Sync watch. A failed read throws instead of looking like a day with no workouts.
    /// </summary>
    async Task<ExerciseRead> GetExerciseSessionsAsync(DateOnly from, DateOnly to)
    {
        var days = await GetDailySummariesAsync(from, to);
        return new ExerciseRead(true, days.SelectMany(d => d.Exercise).ToList());
    }
}

/// <summary>
/// Exercise sessions from Health Connect. <see cref="CanRead"/> is false when the app
/// hasn't been allowed to read exercise, so no sessions can be seen.
/// </summary>
public sealed record ExerciseRead(bool CanRead, List<ExerciseSummary> Sessions);

/// <summary>Used on platforms without Health Connect (the Windows dev target).</summary>
public class UnsupportedHealthDataService : IHealthDataService
{
    public HealthDataAvailability GetAvailability() => HealthDataAvailability.NotSupported;
    public Task<bool> HasPermissionsAsync() => Task.FromResult(false);
    public Task<bool> RequestPermissionsAsync() => Task.FromResult(false);
    public Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(new List<DailyHealthSummary>());
}
