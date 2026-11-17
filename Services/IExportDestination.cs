namespace HealthPulse.Services;

/// <summary>
/// A file the daily export is written to, such as a Google Drive file the
/// user picked once through the system file picker.
/// </summary>
public interface IExportDestination
{
    bool HasDestination { get; }
    string? DestinationName { get; }
    Task<bool> ChooseDestinationAsync(string suggestedFileName);
    Task WriteAsync(string content);
}

public class UnsupportedExportDestination : IExportDestination
{
    public bool HasDestination => false;
    public string? DestinationName => null;
    public Task<bool> ChooseDestinationAsync(string suggestedFileName) => Task.FromResult(false);
    public Task WriteAsync(string content) =>
        throw new PlatformNotSupportedException("Export destinations are only available on Android.");
}
