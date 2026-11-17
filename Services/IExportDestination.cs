namespace HealthPulse.Services;

/// <summary>Where the export is written (a fixed file in the user's Google Drive on Android).</summary>
public interface IExportDestination
{
    bool HasDestination { get; }
    string? DestinationName { get; }
    Task<bool> ChooseDestinationAsync(string suggestedFileName);
    Task WriteAsync(string content);
    void Disconnect();
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
