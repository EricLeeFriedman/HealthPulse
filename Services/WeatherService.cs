using System.Globalization;
using System.Text.Json;
using HealthPulse.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthPulse.Services;

/// <summary>Gives a coarse (city-level) location, or null when unavailable.</summary>
public interface ILocationProvider
{
    /// <param name="interactive">True when the app is open and may show the permission dialog.</param>
    Task<(double Latitude, double Longitude)?> GetCoarseLocationAsync(bool interactive);
}

public class UnsupportedLocationProvider : ILocationProvider
{
    public Task<(double Latitude, double Longitude)?> GetCoarseLocationAsync(bool interactive) =>
        Task.FromResult<(double, double)?>(null);
}

/// <summary>One hour of weather from Open-Meteo, in local time.</summary>
public record HourlyWeather(DateTime Time, double? PressureHpa, double? TemperatureF, double? HumidityPct);

/// <summary>
/// Pulls hourly sea-level pressure (plus temperature and humidity) from Open-Meteo,
/// a free weather API that needs no key, and stores daily summaries.
/// </summary>
public class WeatherService
{
    public const string LatitudeKey = "weather_lat";
    public const string LongitudeKey = "weather_lon";
    public const string LastFetchKey = "weather_last_utc";
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(3);
    public const int MaxPastDays = 92;

    public HttpClient Http;
    public ITrackerStore Store;
    public ISettingsStore Settings;
    public ILocationProvider Location;
    public ILogger<WeatherService> Logger;
    public string BaseUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";

    /// <summary>Open-Meteo asks API clients to identify themselves.</summary>
    public const string UserAgent = "HealthPulse/1.0 (personal health app; Android)";

    /// <summary>If a long look-back is refused, try again with this many days so recent pressure still arrives.</summary>
    public const int ShortRetryDays = 14;

    public WeatherService(HttpClient http, ITrackerStore store, ISettingsStore settings, ILocationProvider location,
        ILogger<WeatherService>? logger = null)
    {
        Http = http;
        Store = store;
        Settings = settings;
        Location = location;
        Logger = logger ?? NullLogger<WeatherService>.Instance;
    }

    /// <summary>Rounds to 2 decimals (about 1 km) so the stored and sent location stays coarse.</summary>
    public static double Coarse(double degrees) => Math.Round(degrees, 2);

    public static string BuildUrl(string baseUrl, double latitude, double longitude, int pastDays)
    {
        var lat = Coarse(latitude).ToString("0.##", CultureInfo.InvariantCulture);
        var lon = Coarse(longitude).ToString("0.##", CultureInfo.InvariantCulture);
        var past = Math.Clamp(pastDays, 1, MaxPastDays).ToString(CultureInfo.InvariantCulture);
        return $"{baseUrl}?latitude={lat}&longitude={lon}" +
            "&hourly=pressure_msl,temperature_2m,relative_humidity_2m" +
            $"&temperature_unit=fahrenheit&timezone=auto&past_days={past}&forecast_days=1";
    }

    public static List<HourlyWeather> ParseHourly(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("hourly", out var hourly))
            throw new InvalidDataException("Weather response had no hourly data.");
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => t.GetString()).ToList();
        var pressure = Values(hourly, "pressure_msl");
        var temperature = Values(hourly, "temperature_2m");
        var humidity = Values(hourly, "relative_humidity_2m");

        var result = new List<HourlyWeather>();
        for (var i = 0; i < times.Count; i++)
        {
            if (!DateTime.TryParse(times[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;
            result.Add(new HourlyWeather(time, At(pressure, i), At(temperature, i), At(humidity, i)));
        }
        return result;
    }

    static List<double?> Values(JsonElement hourly, string name) =>
        hourly.TryGetProperty(name, out var array)
            ? array.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null).ToList()
            : [];

    static double? At(List<double?> values, int index) => index < values.Count ? values[index] : null;

    /// <summary>Turns hourly readings into one summary per local date.</summary>
    public static List<WeatherDay> Summarize(IReadOnlyList<HourlyWeather> hours, DateTime fetchedAtUtc)
    {
        var ordered = hours.OrderBy(h => h.Time).ToList();
        var byTime = ordered.Where(h => h.PressureHpa.HasValue).ToDictionary(h => h.Time, h => h.PressureHpa!.Value);
        var days = new List<WeatherDay>();
        double? previousMean = null;
        foreach (var group in ordered.GroupBy(h => DateOnly.FromDateTime(h.Time)).OrderBy(g => g.Key))
        {
            var pressures = group.Where(h => h.PressureHpa.HasValue).Select(h => h.PressureHpa!.Value).ToList();
            var temps = group.Where(h => h.TemperatureF.HasValue).Select(h => h.TemperatureF!.Value).ToList();
            var humidity = group.Where(h => h.HumidityPct.HasValue).Select(h => h.HumidityPct!.Value).ToList();
            double? mean = pressures.Count > 0 ? Math.Round(pressures.Average(), 1) : null;

            double? maxDrop = null;
            foreach (var hour in group.Where(h => h.PressureHpa.HasValue))
            {
                if (byTime.TryGetValue(hour.Time.AddHours(-6), out var earlier))
                {
                    var drop = earlier - hour.PressureHpa!.Value;
                    if (maxDrop == null || drop > maxDrop) maxDrop = drop;
                }
            }

            days.Add(new WeatherDay
            {
                Date = DailyCheckIn.Key(group.Key),
                PressureMeanHpa = mean,
                PressureMinHpa = pressures.Count > 0 ? Math.Round(pressures.Min(), 1) : null,
                PressureMaxHpa = pressures.Count > 0 ? Math.Round(pressures.Max(), 1) : null,
                PressureChangeHpa = mean.HasValue && previousMean.HasValue ? Math.Round(mean.Value - previousMean.Value, 1) : null,
                PressureMaxDrop6hHpa = maxDrop.HasValue ? Math.Round(Math.Max(0, maxDrop.Value), 1) : null,
                TempMaxF = temps.Count > 0 ? Math.Round(temps.Max(), 1) : null,
                TempMinF = temps.Count > 0 ? Math.Round(temps.Min(), 1) : null,
                HumidityMeanPct = humidity.Count > 0 ? Math.Round(humidity.Average()) : null,
                Hours = group.Count(),
                FetchedAtUtc = fetchedAtUtc,
            });
            previousMean = mean ?? previousMean;
        }
        return days;
    }

    public DateTime? LastFetchUtc =>
        DateTime.TryParse(Settings.GetString(LastFetchKey), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    /// <summary>
    /// Fetches weather up to <paramref name="today"/> when it's stale (or forced), covering
    /// <paramref name="days"/> days. Returns null on success, or an error message.
    /// </summary>
    public async Task<string?> RefreshAsync(DateOnly today, int days, bool interactive, bool force = false)
    {
        try
        {
            // Only re-fetch what's missing, plus a couple of days so partial days fill in.
            var stored = await Store.GetWeatherAsync(today.AddDays(-(days - 1)), today);
            var complete = stored.Where(d => d.Hours >= 24).Select(d => d.Date).ToHashSet();
            var anyMissing = Enumerable.Range(1, Math.Max(0, days - 1))
                .Any(back => !complete.Contains(DailyCheckIn.Key(today.AddDays(-back))));
            var pastDays = anyMissing ? days : 2;

            // Recently fetched and nothing missing: skip.
            if (!force && !anyMissing && LastFetchUtc is { } last && DateTime.UtcNow - last < RefreshInterval) return null;

            var location = await ResolveLocationAsync(interactive);
            if (location == null) return "Location not available yet. Open the Today tab and allow location for weather.";

            pastDays = Math.Min(pastDays, MaxPastDays);
            var (ok, status, body) = await FetchAsync(location.Value, pastDays);
            if (!ok && status is >= 400 and < 500 && pastDays > ShortRetryDays)
            {
                Logger.LogWarning("Weather refused {Status} for {Days} days; retrying with {Short}", status, pastDays, ShortRetryDays);
                (ok, status, body) = await FetchAsync(location.Value, ShortRetryDays);
            }
            if (!ok)
                throw new HttpRequestException($"Weather service returned {status}: {Reason(body)}");

            var now = DateTime.UtcNow;
            var summaries = Summarize(ParseHourly(body), now)
                .Where(d => string.CompareOrdinal(d.Date, DailyCheckIn.Key(today)) <= 0)
                .ToList();
            // The first fetched day has no previous day in this response; keep the stored change if there is one.
            if (summaries.Count > 0 && summaries[0].PressureChangeHpa == null)
            {
                var first = DateOnly.ParseExact(summaries[0].Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var before = (await Store.GetWeatherAsync(first.AddDays(-1), first.AddDays(-1))).FirstOrDefault();
                if (before?.PressureMeanHpa is { } beforeMean && summaries[0].PressureMeanHpa is { } firstMean)
                    summaries[0].PressureChangeHpa = Math.Round(firstMean - beforeMean, 1);
            }
            await Store.SaveWeatherAsync(summaries);
            Settings.SetString(LastFetchKey, now.ToString("O", CultureInfo.InvariantCulture));
            Logger.LogInformation("Weather updated for {Count} days", summaries.Count);
            return null;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Weather update failed");
            return exception.Message;
        }
    }

    async Task<(bool Ok, int Status, string Body)> FetchAsync((double Latitude, double Longitude) location, int pastDays)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            BuildUrl(BaseUrl, location.Latitude, location.Longitude, pastDays));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }

    /// <summary>Open-Meteo explains errors as {"error":true,"reason":"..."}; otherwise show the start of the body.</summary>
    public static string Reason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("reason", out var reason))
                return reason.GetString() ?? "no reason given";
        }
        catch (JsonException)
        {
        }
        var text = body.Trim();
        if (text.Length == 0) return "no details";
        return text.Length <= 200 ? text : text[..200] + "…";
    }

    async Task<(double Latitude, double Longitude)?> ResolveLocationAsync(bool interactive)
    {
        var cached = CachedLocation();
        if (interactive || cached == null)
        {
            try
            {
                var fresh = await Location.GetCoarseLocationAsync(interactive);
                if (fresh != null)
                {
                    var lat = Coarse(fresh.Value.Latitude);
                    var lon = Coarse(fresh.Value.Longitude);
                    Settings.SetString(LatitudeKey, lat.ToString(CultureInfo.InvariantCulture));
                    Settings.SetString(LongitudeKey, lon.ToString(CultureInfo.InvariantCulture));
                    return (lat, lon);
                }
            }
            catch (Exception exception)
            {
                Logger.LogWarning(exception, "Location lookup failed");
            }
        }
        return cached;
    }

    public (double Latitude, double Longitude)? CachedLocation()
    {
        if (double.TryParse(Settings.GetString(LatitudeKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            && double.TryParse(Settings.GetString(LongitudeKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return (lat, lon);
        return null;
    }
}
