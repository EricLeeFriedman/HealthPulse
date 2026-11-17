namespace HealthPulse.Services;

public class PreferencesSettingsStore : ISettingsStore
{
    public string? GetString(string key)
    {
        var value = Preferences.Default.Get(key, "");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public void SetString(string key, string? value)
    {
        if (value == null) Preferences.Default.Remove(key);
        else Preferences.Default.Set(key, value);
    }
}
