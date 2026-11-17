using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public sealed class MigraineServiceTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"migraine-{Guid.NewGuid():N}.db3");
    LocalMigraineService service = null!;

    public Task InitializeAsync()
    {
        service = new LocalMigraineService(path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await service.Database.CloseAsync();
        File.Delete(path);
    }

    [Fact]
    public async Task SaveThenEdit_KeepsOneEntryWithLatestValues()
    {
        var entry = new MigraineEntry { StartTime = new DateTime(2026, 11, 10, 14, 0, 0), PeakSeverity = 6 };
        entry.TriggerList = ["Poor sleep", "Caffeine"];
        await service.SaveEntryAsync(entry);

        entry.EndTime = new DateTime(2026, 11, 10, 20, 30, 0);
        entry.TriptanTaken = true;
        entry.TriptanRelief = "Partial";
        await service.SaveEntryAsync(entry);

        var saved = Assert.Single(await service.GetEntriesAsync());
        Assert.Equal(6.5, saved.DurationHours);
        Assert.Equal(["Poor sleep", "Caffeine"], saved.TriggerList);
        Assert.Equal("Partial", saved.TriptanRelief);
        Assert.False(saved.IsOngoing);
    }

    [Fact]
    public async Task Save_RejectsEndBeforeStart()
    {
        var entry = new MigraineEntry
        {
            StartTime = new DateTime(2026, 11, 10, 14, 0, 0),
            EndTime = new DateTime(2026, 11, 10, 13, 0, 0)
        };
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveEntryAsync(entry));
    }

    [Fact]
    public async Task Range_IncludesOverlappingAndOngoingAttacks()
    {
        await service.SaveEntryAsync(new MigraineEntry
        {
            StartTime = new DateTime(2026, 11, 1, 22, 0, 0),
            EndTime = new DateTime(2026, 11, 2, 6, 0, 0)
        });
        await service.SaveEntryAsync(new MigraineEntry { StartTime = new DateTime(2026, 11, 5, 9, 0, 0) });
        await service.SaveEntryAsync(new MigraineEntry
        {
            StartTime = new DateTime(2026, 10, 20, 9, 0, 0),
            EndTime = new DateTime(2026, 10, 20, 12, 0, 0)
        });

        var inRange = await service.GetEntriesForRangeAsync(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 30));

        Assert.Equal(2, inRange.Count);
    }

    [Fact]
    public async Task Delete_RemovesEntry()
    {
        var entry = new MigraineEntry();
        await service.SaveEntryAsync(entry);
        await service.DeleteEntryAsync(entry.Id);
        Assert.Empty(await service.GetEntriesAsync());
    }
}
