using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthPulse.Services;

/// <summary>
/// Writes the export to My Drive/HealthPulse/healthpulse-export.json in the
/// signed-in Google account, so the project reading the Drive sees the same file.
/// </summary>
public class DriveExportDestination : IExportDestination
{
    public const string FolderName = "HealthPulse";
    public const string AccountKey = "drive_account";
    public const string FolderIdKey = "drive_folder_id";
    public const string FileIdKey = "drive_file_id";
    public const string PhotoFolderIdKey = "drive_photo_folder_id";
    public static string DrivePath => $"My Drive/{FolderName}/{HealthExportService.FileName}";

    readonly IGoogleAuth auth;
    readonly GoogleDriveClient drive;
    readonly ISettingsStore settings;
    readonly ILogger<DriveExportDestination> logger;

    public DriveExportDestination(IGoogleAuth auth, GoogleDriveClient drive, ISettingsStore settings,
        ILogger<DriveExportDestination>? logger = null)
    {
        this.auth = auth;
        this.drive = drive;
        this.settings = settings;
        this.logger = logger ?? NullLogger<DriveExportDestination>.Instance;
    }

    public bool HasDestination => settings.GetString(AccountKey) != null;

    public string? DestinationName => HasDestination
        ? $"{DrivePath} ({settings.GetString(AccountKey)})"
        : null;

    /// <summary>Signs in with Google (shows the consent screen) and prepares the folder.</summary>
    public async Task<bool> ChooseDestinationAsync(string suggestedFileName)
    {
        var token = await auth.GetAccessTokenAsync(interactive: true);
        var email = await drive.GetUserEmailAsync(token) ?? "Google account";
        var folderId = await drive.EnsureFolderAsync(token, FolderName);
        settings.SetString(FolderIdKey, folderId);
        settings.SetString(AccountKey, email);
        logger.LogInformation("Google Drive export connected");
        return true;
    }

    public void Disconnect()
    {
        settings.SetString(AccountKey, null);
        settings.SetString(FolderIdKey, null);
        settings.SetString(FileIdKey, null);
        settings.SetString(PhotoFolderIdKey, null);
    }

    public bool CanUploadPhotos => true;

    /// <summary>
    /// Copies a meal photo to My Drive/HealthPulse/meal-photos. A photo already there under the
    /// same name (an upload whose answer was lost) is reused instead of copied again.
    /// </summary>
    public async Task<string> UploadPhotoAsync(string name, byte[] content, string mimeType)
    {
        if (!HasDestination)
            throw new InvalidOperationException("Sign in with Google first.");
        var token = await auth.GetAccessTokenAsync(interactive: false);
        try
        {
            return await UploadPhotoWithTokenAsync(token, name, content, mimeType);
        }
        catch (GoogleDriveClient.DriveUnauthorizedException)
        {
            await auth.InvalidateAsync(token);
            return await UploadPhotoWithTokenAsync(await auth.GetAccessTokenAsync(interactive: false), name, content, mimeType);
        }
    }

    async Task<string> UploadPhotoWithTokenAsync(string token, string name, byte[] content, string mimeType)
    {
        var photoFolderId = settings.GetString(PhotoFolderIdKey);
        if (photoFolderId != null)
        {
            try
            {
                return await drive.FindAsync(token, name, photoFolderId, null)
                       ?? await drive.CreateBinaryFileAsync(token, photoFolderId, name, content, mimeType);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The folder was deleted: find or recreate it below.
            }
        }
        var folderId = await drive.EnsureFolderAsync(token, FolderName);
        settings.SetString(FolderIdKey, folderId);
        photoFolderId = await drive.EnsureFolderAsync(token, FoodLog.PhotoFolderName, folderId);
        settings.SetString(PhotoFolderIdKey, photoFolderId);
        return await drive.FindAsync(token, name, photoFolderId, null)
               ?? await drive.CreateBinaryFileAsync(token, photoFolderId, name, content, mimeType);
    }

    public async Task WriteAsync(string content)
    {
        if (!HasDestination)
            throw new InvalidOperationException("Sign in with Google first.");
        var token = await auth.GetAccessTokenAsync(interactive: false);
        try
        {
            await WriteWithTokenAsync(token, content);
        }
        catch (GoogleDriveClient.DriveUnauthorizedException)
        {
            // Cached token expired or was revoked: drop it and try once more.
            await auth.InvalidateAsync(token);
            await WriteWithTokenAsync(await auth.GetAccessTokenAsync(interactive: false), content);
        }
    }

    async Task WriteWithTokenAsync(string token, string content)
    {
        const string mime = "application/json";
        var fileId = settings.GetString(FileIdKey);
        if (fileId != null && await drive.UpdateFileAsync(token, fileId, content, mime))
            return;

        // First write, or the file was deleted: find or recreate the folder and file.
        var folderId = await drive.EnsureFolderAsync(token, FolderName);
        settings.SetString(FolderIdKey, folderId);
        fileId = await drive.FindAsync(token, HealthExportService.FileName, folderId, null);
        if (fileId == null || !await drive.UpdateFileAsync(token, fileId, content, mime))
            fileId = await drive.CreateFileAsync(token, folderId, HealthExportService.FileName, content, mime);
        settings.SetString(FileIdKey, fileId);
    }
}
