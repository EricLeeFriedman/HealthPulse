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
}

/// <summary>Used on platforms without Health Connect (the Windows dev target).</summary>
public class UnsupportedHealthDataService : IHealthDataService
{
    public HealthDataAvailability GetAvailability() => HealthDataAvailability.NotSupported;
    public Task<bool> HasPermissionsAsync() => Task.FromResult(false);
    public Task<bool> RequestPermissionsAsync() => Task.FromResult(false);
    public Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to) =>
        Task.FromResult(new List<DailyHealthSummary>());
}
