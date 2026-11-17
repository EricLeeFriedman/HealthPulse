using System.Net;
using System.Text;
using System.Text.Json;
using HealthPulse.Services;

namespace HealthPulse.Tests;

/// <summary>A fake Google Drive that keeps folders and files in memory.</summary>
sealed class FakeDrive : HttpMessageHandler
{
    public record Item(string Id, string Name, string Parent, string MimeType)
    {
        public string Content { get; set; } = "";
    }

    public readonly List<Item> Items = [];
    public readonly List<string> Requests = [];
    public string ValidToken = "token-1";
    int nextId;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        Requests.Add($"{request.Method} {url.AbsolutePath}");
        if (request.Headers.Authorization?.Parameter != ValidToken)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var path = url.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        if (path == "/drive/v3/about")
            return Json(new { user = new { emailAddress = "eric@example.com" } });

        if (request.Method == HttpMethod.Get && path == "/drive/v3/files")
        {
            var q = query["q"]!;
            var name = Between(q, "name = '", "'");
            var parent = q.Contains("'root' in parents") ? "root" : Between(q, "and '", "' in parents");
            var mime = q.Contains("mimeType") ? Between(q, "mimeType = '", "'") : null;
            var match = Items.Where(i => i.Name == name && i.Parent == parent && (mime == null || i.MimeType == mime))
                .Select(i => new { id = i.Id });
            return Json(new { files = match });
        }

        if (request.Method == HttpMethod.Post && path == "/drive/v3/files")
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var item = new Item($"id{++nextId}", doc.RootElement.GetProperty("name").GetString()!,
                doc.RootElement.GetProperty("parents")[0].GetString()!, doc.RootElement.GetProperty("mimeType").GetString()!);
            Items.Add(item);
            return Json(new { id = item.Id });
        }

        if (request.Method == HttpMethod.Post && path == "/upload/drive/v3/files")
        {
            var parts = ((MultipartContent)request.Content!).ToList();
            using var doc = JsonDocument.Parse(await parts[0].ReadAsStringAsync(cancellationToken));
            var item = new Item($"id{++nextId}", doc.RootElement.GetProperty("name").GetString()!,
                doc.RootElement.GetProperty("parents")[0].GetString()!, doc.RootElement.GetProperty("mimeType").GetString()!)
            {
                Content = await parts[1].ReadAsStringAsync(cancellationToken)
            };
            Items.Add(item);
            return Json(new { id = item.Id });
        }

        if (request.Method == HttpMethod.Patch && path.StartsWith("/upload/drive/v3/files/"))
        {
            var id = path["/upload/drive/v3/files/".Length..];
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item == null) return new HttpResponseMessage(HttpStatusCode.NotFound);
            item.Content = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(new { id });
        }

        return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("unexpected " + path) };
    }

    static string Between(string text, string start, string end)
    {
        var i = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        return text[i..text.IndexOf(end, i, StringComparison.Ordinal)];
    }

    static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };
}

sealed class FakeAuth(FakeDrive drive) : IGoogleAuth
{
    public bool RequireApproval;
    public int InteractiveCalls;
    public readonly List<string> Invalidated = [];
    public string Current = "token-1";

    public Task<string> GetAccessTokenAsync(bool interactive)
    {
        if (interactive) InteractiveCalls++;
        if (RequireApproval && !interactive) throw new GoogleAuthRequiredException("approve again");
        return Task.FromResult(Current);
    }

    public Task InvalidateAsync(string accessToken)
    {
        Invalidated.Add(accessToken);
        Current = drive.ValidToken;
        return Task.CompletedTask;
    }
}

public class DriveExportTests
{
    readonly FakeDrive drive = new();
    readonly InMemorySettingsStore settings = new();
    readonly FakeAuth auth;
    readonly DriveExportDestination destination;

    public DriveExportTests()
    {
        auth = new FakeAuth(drive);
        destination = new DriveExportDestination(auth, new GoogleDriveClient(new HttpClient(drive)), settings);
    }

    [Fact]
    public async Task SignIn_CreatesHealthPulseFolderInMyDriveAndRemembersAccount()
    {
        Assert.False(destination.HasDestination);

        Assert.True(await destination.ChooseDestinationAsync(HealthExportService.FileName));

        Assert.Equal(1, auth.InteractiveCalls);
        var folder = Assert.Single(drive.Items);
        Assert.Equal(("HealthPulse", "root", GoogleDriveClient.FolderMimeType), (folder.Name, folder.Parent, folder.MimeType));
        Assert.True(destination.HasDestination);
        Assert.Equal("My Drive/HealthPulse/healthpulse-export.json (eric@example.com)", destination.DestinationName);
    }

    [Fact]
    public async Task SignIn_ReusesExistingFolder()
    {
        drive.Items.Add(new FakeDrive.Item("existing", "HealthPulse", "root", GoogleDriveClient.FolderMimeType));
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        Assert.Single(drive.Items);
        Assert.Equal("existing", settings.GetString(DriveExportDestination.FolderIdKey));
    }

    [Fact]
    public async Task Write_CreatesFileOnceThenOverwritesIt()
    {
        await destination.ChooseDestinationAsync(HealthExportService.FileName);

        await destination.WriteAsync("{\"exported\":1}");
        await destination.WriteAsync("{\"exported\":2}");

        var file = Assert.Single(drive.Items, i => i.Name == HealthExportService.FileName);
        Assert.Equal(settings.GetString(DriveExportDestination.FolderIdKey), file.Parent);
        Assert.Equal("{\"exported\":2}", file.Content);
        Assert.Equal(1, drive.Requests.Count(r => r == "POST /upload/drive/v3/files"));
        Assert.Equal(1, auth.InteractiveCalls);
    }

    [Fact]
    public async Task Write_RecreatesFileIfUserDeletedIt()
    {
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        await destination.WriteAsync("{\"exported\":1}");
        drive.Items.RemoveAll(i => i.Name == HealthExportService.FileName);

        await destination.WriteAsync("{\"exported\":3}");

        Assert.Equal("{\"exported\":3}", Assert.Single(drive.Items, i => i.Name == HealthExportService.FileName).Content);
    }

    [Fact]
    public async Task Write_RefreshesAnExpiredTokenOnce()
    {
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        drive.ValidToken = "token-2";

        await destination.WriteAsync("{\"exported\":4}");

        Assert.Equal(["token-1"], auth.Invalidated);
        Assert.Equal("{\"exported\":4}", Assert.Single(drive.Items, i => i.Name == HealthExportService.FileName).Content);
    }

    [Fact]
    public async Task Write_InBackgroundWhenApprovalNeeded_ThrowsClearError()
    {
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        auth.RequireApproval = true;
        var error = await Assert.ThrowsAsync<GoogleAuthRequiredException>(() => destination.WriteAsync("{}"));
        Assert.Equal("approve again", error.Message);
    }

    [Fact]
    public async Task Write_BeforeSignIn_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => destination.WriteAsync("{}"));
    }

    [Fact]
    public async Task Disconnect_ForgetsAccount()
    {
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        destination.Disconnect();
        Assert.False(destination.HasDestination);
        Assert.Null(settings.GetString(DriveExportDestination.FolderIdKey));
    }

    [Fact]
    public async Task DriveErrors_IncludeStatusAndBody()
    {
        var client = new GoogleDriveClient(new HttpClient(new FailingHandler()));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.EnsureFolderAsync("t", "HealthPulse"));
        Assert.Contains("403", error.Message);
        Assert.Contains("Drive API has not been used", error.Message);
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"Drive API has not been used in project\"}")
            });
    }

    [Fact]
    public async Task ExportService_WritesFullExportToDrive()
    {
        await destination.ChooseDestinationAsync(HealthExportService.FileName);
        var migraines = new LocalMigraineService(Path.Combine(Path.GetTempPath(), $"drive-{Guid.NewGuid():N}.db3"));
        var exporter = new HealthExportService(new UnsupportedHealthDataService(), migraines, destination, settings)
        {
            RecentErrors = () => ["2026-11-17 Crash: boom"]
        };

        Assert.Null(await exporter.ExportNowAsync());

        var file = Assert.Single(drive.Items, i => i.Name == HealthExportService.FileName);
        using var doc = JsonDocument.Parse(file.Content);
        Assert.Equal(HealthExportService.DefaultDays, doc.RootElement.GetProperty("days").GetArrayLength());
        Assert.Equal("2026-11-17 Crash: boom", doc.RootElement.GetProperty("appErrors")[0].GetString());
        await migraines.Database.CloseAsync();
    }
}
