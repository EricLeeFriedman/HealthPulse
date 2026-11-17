using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

/// <summary>A stuck step must end in a visible error, never an endless spinner.</summary>
public class ExportTimeoutTests : IAsyncLifetime
{
    static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);
    readonly LocalMigraineService migraines =
        new(Path.Combine(Path.GetTempPath(), $"timeout-{Guid.NewGuid():N}.db3"));
    readonly InMemorySettingsStore settings = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await migraines.Database.CloseAsync();

    HealthExportService Exporter(IHealthDataService health, IExportDestination destination) =>
        new(health, migraines, destination, settings)
        {
            HealthTimeout = Short,
            WeatherTimeout = Short,
            TrackerTimeout = TimeSpan.FromSeconds(10),
            UploadTimeout = Short,
            LockTimeout = Short,
        };

    [Fact]
    public async Task HungHealthConnect_StillWritesExportWithErrorAndWarning()
    {
        var destination = new RecordingDestination();
        var exporter = Exporter(new HangingHealth(), destination);
        var errors = new List<Exception>();
        exporter.ExportFailed = errors.Add;

        Assert.Null(await exporter.ExportNowAsync());

        using var doc = JsonDocument.Parse(destination.Last!);
        Assert.Contains("Reading Health Connect didn't finish", doc.RootElement.GetProperty("healthConnectError").GetString());
        Assert.Equal(90, doc.RootElement.GetProperty("days").GetArrayLength());
        Assert.NotNull(exporter.LastExportUtc);
        Assert.Null(exporter.LastError);
        Assert.Contains("Health Connect", exporter.LastWarning);
        Assert.Single(errors);
    }

    [Fact]
    public async Task HungWeather_StillWritesExport()
    {
        var destination = new RecordingDestination();
        var exporter = Exporter(new UnsupportedHealthDataService(), destination);
        exporter.RefreshWeather = (_, _) => new TaskCompletionSource<string?>().Task;

        Assert.Null(await exporter.ExportNowAsync());

        using var doc = JsonDocument.Parse(destination.Last!);
        Assert.Contains("Updating weather didn't finish", doc.RootElement.GetProperty("weatherError").GetString());
        Assert.Contains("weather", exporter.LastWarning);
    }

    [Fact]
    public async Task HungUpload_ReturnsErrorAndRecordsIt()
    {
        var exporter = Exporter(new UnsupportedHealthDataService(), new RecordingDestination { Hang = true });
        Exception? recorded = null;
        exporter.ExportFailed = e => recorded = e;

        var error = await exporter.ExportNowAsync();

        Assert.Contains("Uploading to Google Drive didn't finish", error);
        Assert.Equal(error, exporter.LastError);
        Assert.IsType<TimeoutException>(recorded);
        Assert.Null(exporter.LastExportUtc);
    }

    [Fact]
    public async Task ExportAlreadyRunning_GivesUpWithClearMessage()
    {
        var exporter = Exporter(new UnsupportedHealthDataService(), new RecordingDestination());
        await exporter.ExportLock.WaitAsync();

        var error = await exporter.ExportNowAsync();

        Assert.Contains("Another export has been running", error);
        Assert.Equal(error, exporter.LastError);
    }

    [Fact]
    public async Task ExportAlreadyRunning_WaitsForItToFinish()
    {
        var destination = new RecordingDestination();
        var exporter = Exporter(new UnsupportedHealthDataService(), destination);
        exporter.LockTimeout = TimeSpan.FromSeconds(10);
        await exporter.ExportLock.WaitAsync();

        var export = exporter.ExportNowAsync();
        await Task.Delay(50);
        exporter.ExportLock.Release();

        Assert.Null(await export);
        Assert.Equal(1, destination.Writes);
    }

    [Fact]
    public async Task SuccessAfterWarning_ClearsWarning()
    {
        var destination = new RecordingDestination();
        var exporter = Exporter(new HangingHealth(), destination);
        await exporter.ExportNowAsync();
        Assert.NotNull(exporter.LastWarning);

        exporter.HealthData = new UnsupportedHealthDataService();
        Assert.Null(await exporter.ExportNowAsync());
        Assert.Null(exporter.LastWarning);
    }

    [Fact]
    public async Task Status_ReportsEachStepThenClears()
    {
        var exporter = Exporter(new UnsupportedHealthDataService(), new RecordingDestination());
        exporter.RefreshWeather = (_, _) => Task.FromResult<string?>(null);
        var steps = new List<string>();
        exporter.StatusChanged += steps.Add;

        await exporter.ExportNowAsync();

        Assert.Equal("Reading Health Connect…", steps[0]);
        Assert.Contains("Updating weather…", steps);
        Assert.Contains("Uploading to Google Drive…", steps);
        Assert.Equal("", steps[^1]);
    }

    [Fact]
    public async Task Limit_PassesResultsThrough_AndNamesTheStuckStep()
    {
        Assert.Equal(5, await Timeouts.Limit(Task.FromResult(5), Short, "x"));
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            Timeouts.Limit(new TaskCompletionSource().Task, Short, "Uploading"));
        Assert.Equal("Uploading didn't finish within 200 ms.", error.Message);
        Assert.Equal("90 seconds", Timeouts.Describe(TimeSpan.FromSeconds(90)));
        Assert.Equal("5 minutes", Timeouts.Describe(TimeSpan.FromMinutes(5)));
    }

    sealed class HangingHealth : IHealthDataService
    {
        public HealthDataAvailability GetAvailability() => HealthDataAvailability.Available;
        public Task<bool> HasPermissionsAsync() => Task.FromResult(true);
        public Task<bool> RequestPermissionsAsync() => Task.FromResult(true);
        public Task<List<DailyHealthSummary>> GetDailySummariesAsync(DateOnly from, DateOnly to) =>
            new TaskCompletionSource<List<DailyHealthSummary>>().Task;
    }

    sealed class RecordingDestination : IExportDestination
    {
        public bool Hang;
        public int Writes;
        public string? Last;
        public bool HasDestination => true;
        public string? DestinationName => "test.json";
        public Task<bool> ChooseDestinationAsync(string suggestedFileName) => Task.FromResult(true);
        public void Disconnect() { }
        public Task WriteAsync(string content)
        {
            if (Hang) return new TaskCompletionSource().Task;
            Writes++;
            Last = content;
            return Task.CompletedTask;
        }
    }
}
