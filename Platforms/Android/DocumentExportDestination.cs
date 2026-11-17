using Android.App;
using Android.Content;
using Android.Provider;
using HealthPulse.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Writes the export to a file the user creates once with the system file
/// picker. Picking "Drive" in the picker puts the file in Google Drive, and
/// the app keeps permission to overwrite it.
/// </summary>
public class DocumentExportDestination : IExportDestination
{
    public const string UriKey = "health_export_uri";
    public const string NameKey = "health_export_name";

    public ISettingsStore Settings;
    public ILogger<DocumentExportDestination> Logger;

    public DocumentExportDestination(ISettingsStore settings, ILogger<DocumentExportDestination>? logger = null)
    {
        Settings = settings;
        Logger = logger ?? NullLogger<DocumentExportDestination>.Instance;
    }

    static ContentResolver Resolver => global::Android.App.Application.Context.ContentResolver!;

    public bool HasDestination => Settings.GetString(UriKey) != null;

    public string? DestinationName => Settings.GetString(NameKey);

    public async Task<bool> ChooseDestinationAsync(string suggestedFileName)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/json");
        intent.PutExtra(Intent.ExtraTitle, suggestedFileName);

        var (resultCode, data) = await ActivityResultBridge.StartAsync(intent);
        var uri = data?.Data;
        if (resultCode != Result.Ok || uri == null) return false;

        try
        {
            Resolver.TakePersistableUriPermission(uri,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch (Java.Lang.SecurityException exception)
        {
            // Some providers don't offer persistable grants; writes may still work this session.
            Logger.LogWarning(exception, "Persistable permission not granted for export file");
        }

        Settings.SetString(UriKey, uri.ToString());
        Settings.SetString(NameKey, QueryDisplayName(uri) ?? suggestedFileName);
        Logger.LogInformation("Export destination chosen");
        return true;
    }

    public async Task WriteAsync(string content)
    {
        var uriText = Settings.GetString(UriKey);
        if (uriText == null)
            throw new InvalidOperationException("Choose an export file first.");

        var uri = global::Android.Net.Uri.Parse(uriText)!;
        // "wt" truncates so a shorter export doesn't leave stale bytes behind.
        using var stream = Resolver.OpenOutputStream(uri, "wt")
            ?? throw new InvalidOperationException("Could not open the export file.");
        using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        await writer.FlushAsync();
    }

    static string? QueryDisplayName(global::Android.Net.Uri uri)
    {
        try
        {
            using var cursor = Resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor != null && cursor.MoveToFirst())
                return cursor.GetString(0);
        }
        catch (Exception)
        {
            // Name is cosmetic only.
        }
        return null;
    }
}
