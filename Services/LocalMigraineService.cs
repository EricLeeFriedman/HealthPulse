using HealthPulse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SQLite;

namespace HealthPulse.Services;

public class LocalMigraineService : IMigraineService
{
    public SQLiteAsyncConnection Database;
    public SemaphoreSlim InitLock = new(1, 1);
    public bool IsInitialized;
    public ILogger<LocalMigraineService> Logger;

    public LocalMigraineService(
        string dbPath,
        ILogger<LocalMigraineService>? logger = null)
    {
        Logger = logger ?? NullLogger<LocalMigraineService>.Instance;
        SQLitePCL.Batteries_V2.Init();
        Database = new SQLiteAsyncConnection(dbPath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized) return;

        await InitLock.WaitAsync();
        try
        {
            if (IsInitialized) return;
            await Database.CreateTableAsync<MigraineEntry>();
            IsInitialized = true;
            Logger.LogInformation("Migraine database initialized");
        }
        finally
        {
            InitLock.Release();
        }
    }

    public async Task<List<MigraineEntry>> GetEntriesAsync()
    {
        await InitializeAsync();
        var entries = await Database.Table<MigraineEntry>()
            .Where(m => m.UserId == "local")
            .ToListAsync();
        return entries.OrderByDescending(m => m.StartTime).ToList();
    }

    public async Task<List<MigraineEntry>> GetEntriesForRangeAsync(DateOnly from, DateOnly to)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var entries = await GetEntriesAsync();
        // An attack belongs to the range if any part of it overlaps the range.
        return entries
            .Where(m => m.StartTime < end && (m.EndTime ?? DateTime.MaxValue) >= start)
            .OrderBy(m => m.StartTime)
            .ToList();
    }

    public async Task<MigraineEntry?> GetEntryAsync(string id)
    {
        await InitializeAsync();
        return await Database.Table<MigraineEntry>()
            .Where(m => m.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task SaveEntryAsync(MigraineEntry entry)
    {
        await InitializeAsync();
        if (entry.EndTime.HasValue && entry.EndTime.Value < entry.StartTime)
            throw new ArgumentException("End time cannot be before start time.");
        if (entry.PeakSeverity is < 0 or > 10)
            throw new ArgumentException("Pain must be between 0 and 10.");

        entry.UserId = "local";
        entry.UpdatedAt = DateTime.UtcNow;
        await Database.InsertOrReplaceAsync(entry);
        Logger.LogInformation("Migraine entry saved");
    }

    public async Task DeleteEntryAsync(string id)
    {
        await InitializeAsync();
        await Database.DeleteAsync<MigraineEntry>(id);
        Logger.LogInformation("Migraine entry deleted");
    }
}
