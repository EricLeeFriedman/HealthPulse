using HealthPulse.Services;

namespace HealthPulse.Tests;

/// <summary>Drops the first N requests as if the phone had no connection yet.</summary>
sealed class FlakyConnection(HttpMessageHandler inner, int failures) : DelegatingHandler(inner)
{
    public int Failures = failures;
    public int Attempts;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Attempts++;
        if (Failures-- > 0)
            throw new HttpRequestException("Connection failure", new IOException("Unable to resolve host \"www.googleapis.com\""));
        return base.SendAsync(request, cancellationToken);
    }
}

public class DriveRetryTests
{
    readonly FakeDrive drive = new();
    readonly InMemorySettingsStore settings = new();

    DriveExportDestination Destination(FlakyConnection connection) =>
        new(new FakeAuth(drive), new GoogleDriveClient(new HttpClient(connection)) { RetryDelays = [TimeSpan.Zero, TimeSpan.Zero] },
            settings);

    [Fact]
    public async Task DroppedConnection_IsRetriedAndOverwriteSucceeds()
    {
        var connection = new FlakyConnection(drive, 0);
        var destination = Destination(connection);
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        await destination.WriteAsync("{\"v\":1}");

        connection.Failures = 2;
        await destination.WriteAsync("{\"v\":2}");

        var file = Assert.Single(drive.Items, i => i.Name == HealthExportService.FileName);
        Assert.Equal("{\"v\":2}", file.Content);
    }

    [Fact]
    public async Task ConnectionThatNeverComesBack_StillFails()
    {
        var connection = new FlakyConnection(drive, 0);
        var destination = Destination(connection);
        await destination.ChooseDestinationAsync(HealthExportService.FileName);

        connection.Failures = 10;
        var attemptsBefore = connection.Attempts;
        await Assert.ThrowsAsync<HttpRequestException>(() => destination.WriteAsync("{}"));
        Assert.Equal(3, connection.Attempts - attemptsBefore);
    }

    [Fact]
    public void OnlyConnectionFailuresAreRetried()
    {
        Assert.True(GoogleDriveClient.IsConnectionFailure(new HttpRequestException("Connection failure")));
        Assert.False(GoogleDriveClient.IsConnectionFailure(
            new HttpRequestException("Google Drive returned 403", null, System.Net.HttpStatusCode.Forbidden)));
    }

    [Fact]
    public void ErrorLog_RecentDropsProblemsOlderThanTwoDays()
    {
        var log = new ErrorLog(Path.Combine(Path.GetTempPath(), $"errors-{Guid.NewGuid():N}.log"));
        var now = new DateTime(2026, 11, 20, 12, 0, 0);
        log.Record("Export", new InvalidOperationException("old blip"), now.AddDays(-3));
        log.Record("Export", new InvalidOperationException("fresh"), now.AddHours(-1));

        var recent = Assert.Single(log.Recent(now));
        Assert.Contains("fresh", recent);
        Assert.Equal(2, log.Entries.Count);
    }
}
