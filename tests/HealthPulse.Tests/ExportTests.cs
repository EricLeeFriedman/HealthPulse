using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class ExportTests
{
    [Fact]
    public void BuildJson_HasOneRowPerDayWithHealthAndMigraineFlags()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 3);
        var health = new List<DailyHealthSummary>
        {
            new() { Date = new DateOnly(2026, 11, 2), SleepHours = 5.25, Steps = 8123, RestingHeartRate = 58.44,
                    Exercise = [new ExerciseSummary { Type = "running", Start = new DateTime(2026, 11, 2, 7, 0, 0), Minutes = 31.6 }] }
        };
        var migraine = new MigraineEntry
        {
            StartTime = new DateTime(2026, 11, 2, 15, 0, 0),
            EndTime = new DateTime(2026, 11, 2, 21, 0, 0),
            PeakSeverity = 7,
            TriptanTaken = true,
            TriptanTime = new DateTime(2026, 11, 2, 15, 30, 0)
        };
        migraine.TriggerList = ["Poor sleep"];

        var json = HealthExportService.BuildJson(DateTime.UtcNow, "America/Chicago", from, to, health, [migraine]);
        using var doc = JsonDocument.Parse(json);
        var days = doc.RootElement.GetProperty("days");

        Assert.Equal(3, days.GetArrayLength());
        var day2 = days[1];
        Assert.Equal("2026-11-02", day2.GetProperty("date").GetString());
        Assert.Equal(5.25, day2.GetProperty("sleepHours").GetDouble());
        Assert.Equal(8123, day2.GetProperty("steps").GetInt64());
        Assert.Equal(58.4, day2.GetProperty("restingHeartRate").GetDouble());
        Assert.Equal(32, day2.GetProperty("exerciseMinutes").GetDouble());
        Assert.True(day2.GetProperty("migraine").GetBoolean());
        Assert.Equal(7, day2.GetProperty("migraineMaxPain").GetInt32());
        Assert.True(day2.GetProperty("triptanTaken").GetBoolean());
        Assert.False(days[0].GetProperty("migraine").GetBoolean());
        Assert.False(days[0].TryGetProperty("sleepHours", out _));

        var exported = Assert.Single(doc.RootElement.GetProperty("migraines").EnumerateArray());
        Assert.Equal("Poor sleep", exported.GetProperty("triggers")[0].GetString());
        Assert.Equal(6, exported.GetProperty("durationHours").GetDouble());
    }

    [Fact]
    public async Task ExportIfDue_SkipsWhenRecentAndWritesWhenForced()
    {
        var settings = new InMemorySettingsStore();
        var destination = new FakeDestination();
        var migraines = new LocalMigraineService(Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.db3"));
        var exporter = new HealthExportService(new UnsupportedHealthDataService(), migraines, destination, settings);

        await exporter.ExportIfDueAsync();
        Assert.Equal(1, destination.Writes);
        Assert.NotNull(exporter.LastExportUtc);

        await exporter.ExportIfDueAsync();
        Assert.Equal(1, destination.Writes);

        await exporter.ExportIfDueAsync(force: true);
        Assert.Equal(2, destination.Writes);
        Assert.Contains("not available", destination.Last);
        await migraines.Database.CloseAsync();
    }

    [Fact]
    public async Task Export_IncludesHealthConnectDataWhenPermitted()
    {
        var destination = new FakeDestination();
        var migraines = new LocalMigraineService(Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.db3"));
        var exporter = new HealthExportService(new FakeHealth(), migraines, destination, new InMemorySettingsStore());

        Assert.Null(await exporter.ExportNowAsync());

        using var doc = JsonDocument.Parse(destination.Last);
        Assert.False(doc.RootElement.TryGetProperty("healthConnectError", out _));
        var days = doc.RootElement.GetProperty("days");
        Assert.Equal(HealthExportService.DefaultDays, days.GetArrayLength());
        Assert.Equal(6.5, days[days.GetArrayLength() - 1].GetProperty("sleepHours").GetDouble());
        await migraines.Database.CloseAsync();
    }

    [Fact]
    public async Task Export_WithoutDestination_ReturnsMessageAndDoesNothing()
    {
        var migraines = new LocalMigraineService(Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.db3"));
        var exporter = new HealthExportService(new UnsupportedHealthDataService(), migraines,
            new UnsupportedExportDestination(), new InMemorySettingsStore());

        Assert.Equal("Choose an export file first.", await exporter.ExportNowAsync());
        await exporter.ExportIfDueAsync(force: true);
        Assert.Null(exporter.LastExportUtc);
        await migraines.Database.CloseAsync();
    }

    [Fact]
    public async Task Export_WriteFailure_IsRecordedThenClearedOnSuccess()
    {
        var destination = new FakeDestination { FailNext = true };
        var settings = new InMemorySettingsStore();
        var migraines = new LocalMigraineService(Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.db3"));
        var exporter = new HealthExportService(new UnsupportedHealthDataService(), migraines, destination, settings);

        Assert.Equal("Drive offline", await exporter.ExportNowAsync());
        Assert.Equal("Drive offline", exporter.LastError);
        Assert.Null(exporter.LastExportUtc);

        Assert.Null(await exporter.ExportNowAsync());
        Assert.Null(exporter.LastError);
        Assert.NotNull(exporter.LastExportUtc);
        await migraines.Database.CloseAsync();
    }

    sealed class FakeHealth : IHealthDataService
    {
        public HealthDataAvailability GetAvailability() => HealthDataAvailability.Available;
        public Task<bool> HasPermissionsAsync() => Task.FromResult(true);
        public Task<bool> RequestPermissionsAsync() => Task.FromResult(true);
        public Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to) =>
            Task.FromResult(new List<DailyHealthSummary> { new() { Date = to, SleepHours = 6.5 } });
    }

    sealed class FakeDestination : IExportDestination
    {
        public bool FailNext;
        public int Writes;
        public string Last = "";
        public bool HasDestination => true;
        public string? DestinationName => "test.json";
        public Task<bool> ChooseDestinationAsync(string suggestedFileName) => Task.FromResult(true);
        public void Disconnect() { }
        public Task WriteAsync(string content)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("Drive offline");
            }
            Writes++;
            Last = content;
            return Task.CompletedTask;
        }
    }
}
