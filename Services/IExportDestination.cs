namespace HealthPulse.Services;

/// <summary>Where the export is written (a fixed file in the user's Google Drive on Android).</summary>
public interface IExportDestination
{
    bool HasDestination { get; }
    string? DestinationName { get; }
    Task<bool> ChooseDestinationAsync(string suggestedFileName);
    Task WriteAsync(string content);
    void Disconnect();

    /// <summary>Whether meal photos can be copied next to the export.</summary>
    bool CanUploadPhotos => false;

    /// <summary>Copies a meal photo into the export's photo folder (once per name). Returns its file id.</summary>
    Task<string> UploadPhotoAsync(string name, byte[] content, string mimeType) =>
        throw new NotSupportedException("This export destination can't store photos.");
}

public class UnsupportedExportDestination : IExportDestination
{
    public bool HasDestination => false;
    public string? DestinationName => null;
    public Task<bool> ChooseDestinationAsync(string suggestedFileName) => Task.FromResult(false);
    public void Disconnect() { }
    public Task WriteAsync(string content) =>
        throw new PlatformNotSupportedException("Export destinations are only available on Android.");
}
