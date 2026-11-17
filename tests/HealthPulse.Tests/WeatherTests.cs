using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

sealed class FakeOpenMeteo : HttpMessageHandler
{
    public readonly List<string> Requests = [];
    public HttpStatusCode Status = HttpStatusCode.OK;
    /// <summary>Pressure for an hour; default falls 1 hPa every 6 hours from 1020.</summary>
    public Func<DateTime, double> Pressure = time => 1020 - (time - new DateTime(2026, 1, 1)).TotalHours / 6 % 20;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        var query = request.RequestUri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var pastDays = int.Parse(query["past_days"], CultureInfo.InvariantCulture);
        var today = DateTime.Today;
        var start = today.AddDays(-pastDays);
        var times = new List<string>();
        var pressure = new List<double>();
        for (var t = start; t < today.AddDays(1); t = t.AddHours(1))
        {
            times.Add(t.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture));
            pressure.Add(Pressure(t));
        }
        var body = JsonSerializer.Serialize(new
        {
            hourly = new
            {
                time = times,
                pressure_msl = pressure,
                temperature_2m = times.Select(_ => 50.0),
                relative_humidity_2m = times.Select(_ => 60.0),
            }
        });
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

sealed class FakeLocation : ILocationProvider
{
    public (double, double)? Result = (41.878113, -87.629799);
    public int Calls;
    public bool LastInteractive;

    public Task<(double Latitude, double Longitude)?> GetCoarseLocationAsync(bool interactive)
    {
        Calls++;
        LastInteractive = interactive;
        return Task.FromResult<(double Latitude, double Longitude)?>(Result);
    }
}

public sealed class WeatherTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"weather-{Guid.NewGuid():N}.db3");
    LocalTrackerStore store = null!;
    readonly FakeOpenMeteo api = new();
    readonly FakeLocation location = new();
    readonly InMemorySettingsStore settings = new();
    WeatherService service = null!;

    public Task InitializeAsync()
    {
        store = new LocalTrackerStore(path);
        service = new WeatherService(new HttpClient(api), store, settings, location);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await store.Database.CloseAsync();
        File.Delete(path);
    }

    [Fact]
    public void BuildUrl_UsesCoarseCoordinatesAndPressureFields()
    {
        var url = WeatherService.BuildUrl("https://api.open-meteo.com/v1/forecast", 41.878113, -87.629799, 120);
        Assert.Contains("latitude=41.88", url);
        Assert.Contains("longitude=-87.63", url);
        Assert.Contains("pressure_msl", url);
        Assert.Contains("past_days=92", url); // clamped to the API maximum
        Assert.Contains("timezone=auto", url);
    }

    [Fact]
    public void ParseHourly_HandlesNullValues()
    {
        const string json = """
            {"hourly":{"time":["2026-11-17T00:00","2026-11-17T01:00"],
             "pressure_msl":[1012.3,null],"temperature_2m":[40.1,39.5],"relative_humidity_2m":[80,81]}}
            """;
        var hours = WeatherService.ParseHourly(json);
        Assert.Equal(2, hours.Count);
        Assert.Equal(new DateTime(2026, 11, 17, 1, 0, 0), hours[1].Time);
        Assert.Equal(1012.3, hours[0].PressureHpa);
        Assert.Null(hours[1].PressureHpa);
        Assert.Throws<InvalidDataException>(() => WeatherService.ParseHourly("{}"));
    }

    [Fact]
    public void Summarize_ComputesDailyPressureChangeAndFastestDrop()
    {
        var hours = new List<HourlyWeather>();
        var day1 = new DateTime(2026, 11, 16);
        for (var h = 0; h < 24; h++) hours.Add(new HourlyWeather(day1.AddHours(h), 1015, 40 + h, 70));
        // Day 2: steady 1015 until noon, then drops 1 hPa an hour to 1003.
        for (var h = 0; h < 24; h++)
            hours.Add(new HourlyWeather(day1.AddDays(1).AddHours(h), h < 12 ? 1015 : 1015 - (h - 11), 50, 90));

        var days = WeatherService.Summarize(hours, DateTime.UtcNow);

        Assert.Equal(2, days.Count);
        Assert.Equal("2026-11-16", days[0].Date);
        Assert.Equal(1015, days[0].PressureMeanHpa);
        Assert.Null(days[0].PressureChangeHpa);
        Assert.Equal(63, days[0].TempMaxF);
        Assert.Equal(24, days[0].Hours);

        Assert.Equal(1003, days[1].PressureMinHpa);
        Assert.Equal(1015, days[1].PressureMaxHpa);
        Assert.True(days[1].PressureChangeHpa < -3);
        Assert.Equal(6, days[1].PressureMaxDrop6hHpa);
        Assert.Equal(90, days[1].HumidityMeanPct);
    }

    [Fact]
    public async Task Refresh_FetchesStoresAndCachesCoarseLocation()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        Assert.Null(await service.RefreshAsync(today, 30, interactive: true));

        Assert.Single(api.Requests);
        Assert.Contains("past_days=30", api.Requests[0]);
        Assert.Equal("41.88", settings.GetString(WeatherService.LatitudeKey));
        Assert.True(location.LastInteractive);
        var stored = await store.GetWeatherAsync(today.AddDays(-29), today);
        Assert.Equal(30, stored.Count);
        Assert.All(stored.Take(29), d => Assert.Equal(24, d.Hours));
        Assert.All(stored.Skip(1), d => Assert.NotNull(d.PressureChangeHpa));

        // Fresh and complete: no second request.
        Assert.Null(await service.RefreshAsync(today, 30, interactive: false));
        Assert.Single(api.Requests);

        // Forced, with history complete: only the last couple of days are re-fetched, using the cached location.
        location.Calls = 0;
        Assert.Null(await service.RefreshAsync(today, 30, interactive: false, force: true));
        Assert.Equal(2, api.Requests.Count);
        Assert.Contains("past_days=2", api.Requests[1]);
        Assert.Equal(0, location.Calls);
    }

    [Fact]
    public async Task Refresh_LongerWindowFetchesEvenWhenRecent()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Null(await service.RefreshAsync(today, 14, interactive: true));
        Assert.Null(await service.RefreshAsync(today, 90, interactive: false));
        Assert.Equal(2, api.Requests.Count);
        Assert.Contains("past_days=90", api.Requests[1]);
        Assert.Equal(90, (await store.GetWeatherAsync(today.AddDays(-89), today)).Count);
    }

    [Fact]
    public async Task Refresh_ReportsMissingLocationAndHttpErrors()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        location.Result = null;
        var error = await service.RefreshAsync(today, 7, interactive: false);
        Assert.Contains("Location not available", error);
        Assert.Empty(api.Requests);

        location.Result = (40.0, -88.0);
        api.Status = HttpStatusCode.InternalServerError;
        error = await service.RefreshAsync(today, 7, interactive: true);
        Assert.Contains("500", error);
        Assert.Null(service.LastFetchUtc);
    }
}
