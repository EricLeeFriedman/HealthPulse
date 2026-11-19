using HealthPulse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SQLite;

namespace HealthPulse.Services;

/// <summary>Check-ins, workouts, measurements, fitness tests and weather, stored on the phone.</summary>
public interface ITrackerStore
{
    Task<DailyCheckIn> GetCheckInAsync(DateOnly date);
    Task<List<DailyCheckIn>> GetCheckInsAsync(DateOnly from, DateOnly to);
    Task SaveCheckInAsync(DailyCheckIn checkIn);

    /// <summary>Workouts in the range, without ones the user deleted.</summary>
    Task<List<WorkoutSession>> GetWorkoutsAsync(DateOnly from, DateOnly to);
    /// <summary>Workouts in the range including deleted (hidden) detected ones, for importing.</summary>
    Task<List<WorkoutSession>> GetAllWorkoutsAsync(DateOnly from, DateOnly to);
    Task<WorkoutSession?> GetWorkoutAsync(string id);
    Task SaveWorkoutAsync(WorkoutSession session);
    Task DeleteWorkoutAsync(string id);

    Task<List<BodyMeasurement>> GetMeasurementsAsync(DateOnly from, DateOnly to);
    Task SaveMeasurementAsync(BodyMeasurement measurement);
    Task DeleteMeasurementAsync(string id);

    Task<List<FitnessTest>> GetFitnessTestsAsync(DateOnly from, DateOnly to);
    Task SaveFitnessTestAsync(FitnessTest test);
    Task DeleteFitnessTestAsync(string id);

    Task<List<WeatherDay>> GetWeatherAsync(DateOnly from, DateOnly to);
    Task SaveWeatherAsync(IEnumerable<WeatherDay> days);

    Task<List<FoodEntry>> GetFoodAsync(DateOnly from, DateOnly to);
    Task SaveFoodAsync(FoodEntry entry);
    Task DeleteFoodAsync(string id);

    /// <summary>Day swaps with dates in the range.</summary>
    Task<List<PlanSwap>> GetPlanSwapsAsync(DateOnly from, DateOnly to);
    Task SavePlanSwapsAsync(IEnumerable<PlanSwap> swaps);
    Task DeletePlanSwapsAsync(IEnumerable<string> dates);
}

public class LocalTrackerStore : ITrackerStore
{
    public SQLiteAsyncConnection Database;
    public ILogger<LocalTrackerStore> Logger;
    readonly SemaphoreSlim initLock = new(1, 1);
    bool initialized;

    public LocalTrackerStore(string dbPath, ILogger<LocalTrackerStore>? logger = null)
    {
        Logger = logger ?? NullLogger<LocalTrackerStore>.Instance;
        SQLitePCL.Batteries_V2.Init();
        Database = new SQLiteAsyncConnection(dbPath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    async Task InitAsync()
    {
        if (initialized) return;
        await initLock.WaitAsync();
        try
        {
            if (initialized) return;
            await Database.CreateTableAsync<DailyCheckIn>();
            await Database.CreateTableAsync<WorkoutSession>();
            await Database.CreateTableAsync<WorkoutSet>();
            await Database.CreateTableAsync<BodyMeasurement>();
            await Database.CreateTableAsync<FitnessTest>();
            await Database.CreateTableAsync<WeatherDay>();
            await Database.CreateTableAsync<FoodEntry>();
            await Database.CreateTableAsync<PlanSwap>();
            initialized = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    static DateTime Start(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
    static DateTime End(DateOnly date) => date.AddDays(1).ToDateTime(TimeOnly.MinValue);

    public async Task<DailyCheckIn> GetCheckInAsync(DateOnly date)
    {
        await InitAsync();
        var key = DailyCheckIn.Key(date);
        return await Database.Table<DailyCheckIn>().Where(c => c.Date == key).FirstOrDefaultAsync()
            ?? new DailyCheckIn { Date = key, UpdatedAt = DateTime.MinValue };
    }

    public async Task<List<DailyCheckIn>> GetCheckInsAsync(DateOnly from, DateOnly to)
    {
        await InitAsync();
        var start = DailyCheckIn.Key(from);
        var end = DailyCheckIn.Key(to);
        var all = await Database.Table<DailyCheckIn>().ToListAsync();
        // yyyy-MM-dd strings sort in date order.
        return all.Where(c => string.CompareOrdinal(c.Date, start) >= 0 && string.CompareOrdinal(c.Date, end) <= 0)
            .OrderBy(c => c.Date, StringComparer.Ordinal)
            .ToList();
    }

    public async Task SaveCheckInAsync(DailyCheckIn checkIn)
    {
        await InitAsync();
        TrackerRules.Validate(checkIn);
        checkIn.UpdatedAt = DateTime.UtcNow;
        await Database.InsertOrReplaceAsync(checkIn);
    }

    public async Task<List<WorkoutSession>> GetWorkoutsAsync(DateOnly from, DateOnly to) =>
        (await GetAllWorkoutsAsync(from, to)).Where(s => !s.Hidden).ToList();

    public async Task<List<WorkoutSession>> GetAllWorkoutsAsync(DateOnly from, DateOnly to)
    {
        await InitAsync();
        var start = Start(from);
        var end = End(to);
        var sessions = await Database.Table<WorkoutSession>()
            .Where(s => s.Date >= start && s.Date < end)
            .ToListAsync();
        foreach (var session in sessions)
            session.Sets = await LoadSetsAsync(session.Id);
        return sessions.OrderBy(s => s.Date).ToList();
    }

    async Task<List<WorkoutSet>> LoadSetsAsync(string sessionId) =>
        (await Database.Table<WorkoutSet>().Where(x => x.SessionId == sessionId).ToListAsync())
            .OrderBy(x => x.Order).ToList();

    public async Task<WorkoutSession?> GetWorkoutAsync(string id)
    {
        await InitAsync();
        var session = await Database.Table<WorkoutSession>().Where(s => s.Id == id).FirstOrDefaultAsync();
        if (session != null) session.Sets = await LoadSetsAsync(session.Id);
        return session;
    }

    public async Task SaveWorkoutAsync(WorkoutSession session)
    {
        await InitAsync();
        TrackerRules.Validate(session);
        session.UpdatedAt = DateTime.UtcNow;
        var sets = session.Sets;
        for (var i = 0; i < sets.Count; i++)
        {
            sets[i].SessionId = session.Id;
            sets[i].Order = i;
        }
        await Database.RunInTransactionAsync(connection =>
        {
            connection.InsertOrReplace(session);
            connection.Execute("DELETE FROM WorkoutSet WHERE SessionId = ?", session.Id);
            foreach (var set in sets) connection.Insert(set);
        });
        Logger.LogInformation("Workout saved");
    }

    public async Task DeleteWorkoutAsync(string id)
    {
        await InitAsync();
        var session = await Database.Table<WorkoutSession>().Where(s => s.Id == id).FirstOrDefaultAsync();
        await Database.RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM WorkoutSet WHERE SessionId = ?", id);
            if (session?.ExternalId != null)
            {
                // Keep detected workouts as hidden so the next Health Connect sync doesn't bring them back.
                session.Hidden = true;
                session.UpdatedAt = DateTime.UtcNow;
                connection.Update(session);
            }
            else
            {
                connection.Delete<WorkoutSession>(id);
            }
        });
    }

    public async Task<List<BodyMeasurement>> GetMeasurementsAsync(DateOnly from, DateOnly to)
    {
        await InitAsync();
        var start = Start(from);
        var end = End(to);
        var list = await Database.Table<BodyMeasurement>().Where(m => m.Date >= start && m.Date < end).ToListAsync();
        return list.OrderBy(m => m.Date).ToList();
    }

    public async Task SaveMeasurementAsync(BodyMeasurement measurement)
    {
        await InitAsync();
        TrackerRules.Validate(measurement);
        measurement.UpdatedAt = DateTime.UtcNow;
        await Database.InsertOrReplaceAsync(measurement);
    }

    public async Task DeleteMeasurementAsync(string id)
    {
        await InitAsync();
        await Database.DeleteAsync<BodyMeasurement>(id);
    }

    public async Task<List<FitnessTest>> GetFitnessTestsAsync(DateOnly from, DateOnly to)
    {
        await InitAsync();
        var start = Start(from);
        var end = End(to);
        var list = await Database.Table<FitnessTest>().Where(t => t.Date >= start && t.Date < end).ToListAsync();
        return list.OrderBy(t => t.Date).ToList();
    }

    public async Task SaveFitnessTestAsync(FitnessTest test)
    {
        await InitAsync();
        TrackerRules.Validate(test);
        test.UpdatedAt = DateTime.UtcNow;
        await Database.InsertOrReplaceAsync(test);
    }

    public async Task DeleteFitnessTestAsync(string id)
    {
        await InitAsync();
        await Database.DeleteAsync<FitnessTest>(id);
    }

    public async Task<List<WeatherDay>> GetWeatherAsync(DateOnly from, DateOnly to)
    {
        await InitAsync();
        var start = DailyCheckIn.Key(from);
        var end = DailyCheckIn.Key(to);
        var all = await Database.Table<WeatherDay>().ToListAsync();
        return all.Where(w => string.CompareOrdinal(w.Date, start) >= 0 && string.CompareOrdinal(w.Date, end) <= 0)
            .OrderBy(w => w.Date, StringComparer.Ordinal)
            .ToList();
    }

    public async Task SaveWeatherAsync(IEnumerable<WeatherDay> days)
    {
        await InitAsync();
        var list = days.ToList();
        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var day in list) connection.InsertOrReplace(day);
        });
    }

    public async Task<List<FoodEntry>> GetFoodAsync(DateOnly from, DateOnly to)
    {
        await InitAsync();
        var start = Start(from);
        var end = End(to);
        var list = await Database.Table<FoodEntry>().Where(f => f.Time >= start && f.Time < end).ToListAsync();
        return list.OrderBy(f => f.Time).ToList();
    }

    public async Task SaveFoodAsync(FoodEntry entry)
    {
        await InitAsync();
        FoodLog.Validate(entry);
        entry.Text = entry.Text.Trim();
        entry.UpdatedAt = DateTime.UtcNow;
        await Database.InsertOrReplaceAsync(entry);
    }

    public async Task DeleteFoodAsync(string id)
    {
        await InitAsync();
        await Database.DeleteAsync<FoodEntry>(id);
    }

    public async Task<List<PlanSwap>> GetPlanSwapsAsync(DateOnly from, DateOnly to)
    {
        await InitAsync();
        var start = DailyCheckIn.Key(from);
        var end = DailyCheckIn.Key(to);
        var all = await Database.Table<PlanSwap>().ToListAsync();
        return all.Where(s => string.CompareOrdinal(s.Date, start) >= 0 && string.CompareOrdinal(s.Date, end) <= 0)
            .OrderBy(s => s.Date, StringComparer.Ordinal)
            .ToList();
    }

    public async Task SavePlanSwapsAsync(IEnumerable<PlanSwap> swaps)
    {
        await InitAsync();
        var list = swaps.ToList();
        foreach (var swap in list) swap.UpdatedAt = DateTime.UtcNow;
        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var swap in list) connection.InsertOrReplace(swap);
        });
    }

    public async Task DeletePlanSwapsAsync(IEnumerable<string> dates)
    {
        await InitAsync();
        var list = dates.ToList();
        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var date in list) connection.Delete<PlanSwap>(date);
        });
    }
}
