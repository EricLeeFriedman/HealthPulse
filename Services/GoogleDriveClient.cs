using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HealthPulse.Services;

/// <summary>
/// Minimal Google Drive v3 REST client: find or create a folder in My Drive,
/// and create or overwrite one file in it.
/// </summary>
public class GoogleDriveClient
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";
    const string Api = "https://www.googleapis.com/drive/v3";
    const string UploadApi = "https://www.googleapis.com/upload/drive/v3";

    readonly HttpClient http;

    /// <summary>Waits before retrying after a dropped connection (no answer from Google at all).</summary>
    public TimeSpan[] RetryDelays { get; set; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)];

    public GoogleDriveClient(HttpClient http)
    {
        this.http = http;
    }

    /// <summary>
    /// Sends a request, retrying when the connection fails before Google answers
    /// (no signal, DNS not ready after waking). Errors Google returns are not retried here.
    /// </summary>
    async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build)
    {
        for (var attempt = 0; ; attempt++)
        {
            var request = build();
            try
            {
                return await http.SendAsync(request);
            }
            catch (Exception exception) when (attempt < RetryDelays.Length && IsConnectionFailure(exception))
            {
                request.Dispose();
                await Task.Delay(RetryDelays[attempt]);
            }
        }
    }

    public static bool IsConnectionFailure(Exception exception) =>
        exception is HttpRequestException { StatusCode: null } || exception is IOException;

    public class DriveUnauthorizedException() : Exception("Google rejected the access token.");

    public async Task<string?> GetUserEmailAsync(string token)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"{Api}/about?fields=user(emailAddress)", token);
        return doc.RootElement.TryGetProperty("user", out var user)
               && user.TryGetProperty("emailAddress", out var email)
            ? email.GetString()
            : null;
    }

    public async Task<string?> FindAsync(string token, string name, string? parentId, string? mimeType)
    {
        var query = new StringBuilder($"name = '{Escape(name)}' and trashed = false");
        query.Append(parentId == null ? " and 'root' in parents" : $" and '{Escape(parentId)}' in parents");
        if (mimeType != null) query.Append($" and mimeType = '{Escape(mimeType)}'");
        var url = $"{Api}/files?spaces=drive&fields=files(id)&pageSize=1&q={Uri.EscapeDataString(query.ToString())}";
        using var doc = await SendJsonAsync(HttpMethod.Get, url, token);
        var files = doc.RootElement.GetProperty("files");
        return files.GetArrayLength() > 0 ? files[0].GetProperty("id").GetString() : null;
    }

    public async Task<string> CreateFolderAsync(string token, string name, string? parentId = null)
    {
        var body = JsonSerializer.Serialize(new { name, mimeType = FolderMimeType, parents = new[] { parentId ?? "root" } });
        using var doc = await SendJsonAsync(HttpMethod.Post, $"{Api}/files?fields=id", token,
            () => new StringContent(body, Encoding.UTF8, "application/json"));
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Finds or creates a folder in My Drive, or inside <paramref name="parentId"/> when given.</summary>
    public async Task<string> EnsureFolderAsync(string token, string name, string? parentId = null) =>
        await FindAsync(token, name, parentId, FolderMimeType) ?? await CreateFolderAsync(token, name, parentId);

    /// <summary>Creates a binary file (a photo, say) in a folder. Returns its id.</summary>
    public async Task<string> CreateBinaryFileAsync(string token, string folderId, string name, byte[] content, string mimeType)
    {
        var boundary = "healthpulse" + Guid.NewGuid().ToString("N");
        var metadata = JsonSerializer.Serialize(new { name, parents = new[] { folderId }, mimeType });
        using var doc = await SendJsonAsync(HttpMethod.Post, $"{UploadApi}/files?uploadType=multipart&fields=id", token,
            () =>
            {
                var file = new ByteArrayContent(content);
                file.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                return new MultipartContent("related", boundary)
                {
                    new StringContent(metadata, Encoding.UTF8, "application/json"),
                    file
                };
            });
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<string> CreateFileAsync(string token, string folderId, string name, string content, string mimeType)
    {
        var boundary = "healthpulse" + Guid.NewGuid().ToString("N");
        var metadata = JsonSerializer.Serialize(new { name, parents = new[] { folderId }, mimeType });
        using var doc = await SendJsonAsync(HttpMethod.Post, $"{UploadApi}/files?uploadType=multipart&fields=id", token,
            () => new MultipartContent("related", boundary)
            {
                new StringContent(metadata, Encoding.UTF8, "application/json"),
                new StringContent(content, Encoding.UTF8, mimeType)
            });
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Overwrites a file's contents. Returns false if the file no longer exists.</summary>
    public async Task<bool> UpdateFileAsync(string token, string fileId, string content, string mimeType)
    {
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Patch,
                $"{UploadApi}/files/{Uri.EscapeDataString(fileId)}?uploadType=media&fields=id")
            {
                Content = new StringContent(content, Encoding.UTF8, mimeType)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        });
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccessAsync(response);
        return true;
    }

    async Task<JsonDocument> SendJsonAsync(HttpMethod method, string url, string token, Func<HttpContent>? content = null)
    {
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(method, url) { Content = content?.Invoke() };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        });
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new DriveUnauthorizedException();
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Google Drive returned {(int)response.StatusCode}: {Truncate(body, 300)}", null, response.StatusCode);
        }
    }

    static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
    static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
