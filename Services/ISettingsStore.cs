namespace HealthPulse.Services;

/// <summary>Small key/value settings store (MAUI Preferences in the app, in-memory in tests).</summary>
public interface ISettingsStore
{
    string? GetString(string key);
    void SetString(string key, string? value);
}

public class InMemorySettingsStore : ISettingsStore
{
    readonly Dictionary<string, string?> values = [];
    public string? GetString(string key) => values.TryGetValue(key, out var value) ? value : null;
    public void SetString(string key, string? value) => values[key] = value;
}
