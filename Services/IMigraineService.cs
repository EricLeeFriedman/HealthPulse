using HealthPulse.Models;

namespace HealthPulse.Services;

public interface IMigraineService
{
    Task InitializeAsync();
    Task<List<MigraineEntry>> GetEntriesAsync();
    Task<List<MigraineEntry>> GetEntriesForRangeAsync(DateOnly from, DateOnly to);
    Task<MigraineEntry?> GetEntryAsync(string id);
    Task SaveEntryAsync(MigraineEntry entry);
    Task DeleteEntryAsync(string id);
}
