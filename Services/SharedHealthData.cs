using HealthPulse.Models;

namespace HealthPulse.Services;

/// <summary>
/// Shares one Health Connect read between the Health tab, the Fitness tab and the
/// export. Every read covers the same 90-day window, only one runs at a time, and
/// a result is reused for a few minutes, so opening the app no longer starts three
/// heavy reads at once.
/// </summary>
public class SharedHealthData : IHealthDataService
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(3);

    readonly IHealthDataService inner;
    readonly Func<DateTime> now;
    readonly object gate = new();
    Task<List<DailyHealthSummary>>? running;
    (DateOnly From, DateOnly To)? runningRange;
    List<DailyHealthSummary>? cached;
    (DateOnly From, DateOnly To)? cachedRange;
    DateTime cachedAt;

    public SharedHealthData(IHealthDataService inner, Func<DateTime>? now = null)
    {
        this.inner = inner;
        this.now = now ?? (() => DateTime.UtcNow);
    }

    public int Reads { get; private set; }

    public HealthDataAvailability GetAvailability() => inner.GetAvailability();
    public Task<bool> HasPermissionsAsync() => inner.HasPermissionsAsync();
    public IReadOnlyList<string> LastReadSteps => inner.LastReadSteps;

    /// <summary>Always a fresh, exercise-only read, so Sync watch sees a session recorded a minute ago.</summary>
    public Task<ExerciseRead> GetExerciseSessionsAsync(DateOnly from, DateOnly to) =>
        inner.GetExerciseSessionsAsync(from, to);

    public async Task<bool> RequestPermissionsAsync()
    {
        Invalidate();
        return await inner.RequestPermissionsAsync();
    }

    public void Invalidate()
    {
        lock (gate)
        {
            cached = null;
            cachedRange = null;
        }
    }

    public async Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to)
    {
        while (true)
        {
            Task<List<DailyHealthSummary>>? mine = null;
            Task<List<DailyHealthSummary>>? other = null;
            lock (gate)
            {
                if (cached != null && Covers(cachedRange, from, to) && now() - cachedAt < CacheFor)
                    return Slice(cached, from, to);

                if (running != null && !running.IsCompleted)
                {
                    if (Covers(runningRange, from, to)) mine = running;
                    else other = running;
                }
                else
                {
                    runningRange = Widen(from, to);
                    Reads++;
                    mine = running = ReadAndCacheAsync(runningRange.Value);
                }
            }

            if (mine != null)
                return Slice(await mine, from, to);

            // A read that doesn't cover our range is running: let it finish, then read ours.
            try { await other!; } catch { /* our own read reports its own error */ }
        }
    }

    async Task<List<DailyHealthSummary>> ReadAndCacheAsync((DateOnly From, DateOnly To) range)
    {
        var result = await inner.GetDailySummariesAsync(range.From, range.To);
        lock (gate)
        {
            cached = result;
            cachedRange = range;
            cachedAt = now();
        }
        return result;
    }

    /// <summary>Reads at least the export's 90 days, so every caller can share it.</summary>
    public static (DateOnly From, DateOnly To) Widen(DateOnly from, DateOnly to)
    {
        var earliest = to.AddDays(-(HealthExportService.DefaultDays - 1));
        return (from < earliest ? from : earliest, to);
    }

    static bool Covers((DateOnly From, DateOnly To)? range, DateOnly from, DateOnly to) =>
        range.HasValue && range.Value.From <= from && range.Value.To >= to;

    static List<DailyHealthSummary> Slice(List<DailyHealthSummary> days, DateOnly from, DateOnly to) =>
        days.Where(d => d.Date >= from && d.Date <= to).ToList();
}
