using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class TrackerExportTests
{
    static TrackerExport SampleTrackers()
    {
        var measurement = new BodyMeasurement { Date = new DateTime(2026, 11, 22, 8, 0, 0), WaistInches = 34.5, WeightLbs = 165.2 };
        measurement.PhotoList = ["/data/user/0/photos/front.jpg", "/data/user/0/photos/side.jpg"];
        return new TrackerExport(
            [new DailyCheckIn
            {
                Date = "2026-11-23", CaffeineCups = 3, LastCaffeineTime = new TimeSpan(12, 5, 0), WaterGlasses = 7,
                FirstMealTime = new TimeSpan(9, 30, 0), AlcoholDrinks = 0, Stress = 2,
                Riboflavin = true, BComplex = true, CoQ10 = true, Magnesium = false, Jublia = true, ProteinGrams = 105
            }],
            [new WorkoutSession
            {
                Date = new DateTime(2026, 11, 23, 7, 0, 0), SessionType = WorkoutPlan.StrengthA, DurationMinutes = 31,
                Sets = [new WorkoutSet { Exercise = "Goblet squat", Reps = 10, WeightLbs = 25 }]
            }],
            [measurement],
            [new FitnessTest { Date = new DateTime(2026, 11, 22, 8, 10, 0), ToeTouchInches = -3, PushUps = 15, PlankSeconds = 40 }],
            [new WeatherDay { Date = "2026-11-23", PressureMeanHpa = 1009.5, PressureChangeHpa = -5.5, PressureMaxDrop6hHpa = 3.2, Hours = 24 }]);
    }

    [Fact]
    public void BuildJson_IncludesCheckInWorkoutsWeatherAndBody()
    {
        var json = HealthExportService.BuildJson(DateTime.UtcNow, "America/Chicago",
            new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 23), [], [], trackers: SampleTrackers());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var days = root.GetProperty("days");

        var sunday = days[0];
        Assert.Equal("Mobility", sunday.GetProperty("plannedSession").GetString());
        Assert.Equal(34.5, sunday.GetProperty("waistInches").GetDouble());
        Assert.False(sunday.TryGetProperty("checkIn", out _));

        var monday = days[1];
        Assert.Equal("Strength A", monday.GetProperty("plannedSession").GetString());
        Assert.Equal("Strength A", monday.GetProperty("workouts")[0].GetString());
        var checkIn = monday.GetProperty("checkIn");
        Assert.Equal(3, checkIn.GetProperty("caffeineCups").GetInt32());
        Assert.Equal("12:05", checkIn.GetProperty("lastCaffeine").GetString());
        Assert.Equal("09:30", checkIn.GetProperty("firstMeal").GetString());
        Assert.Equal(2, checkIn.GetProperty("stress").GetInt32());
        // A day saved as glasses only still reports ounces (8 oz per glass).
        Assert.Equal(56, checkIn.GetProperty("waterOunces").GetInt32());
        Assert.Equal(7, checkIn.GetProperty("waterGlasses").GetInt32());
        Assert.False(checkIn.GetProperty("allSupplements").GetBoolean());
        Assert.False(checkIn.GetProperty("supplements").GetProperty("magnesium").GetBoolean());
        Assert.True(checkIn.GetProperty("jubliaApplied").GetBoolean());
        Assert.False(checkIn.GetProperty("supplements").TryGetProperty("jublia", out _)); // not a migraine supplement
        Assert.Equal("Below", checkIn.GetProperty("proteinStatus").GetString());
        var weather = monday.GetProperty("weather");
        Assert.Equal(-5.5, weather.GetProperty("pressureChangeHpa").GetDouble());

        var workout = Assert.Single(root.GetProperty("workouts").EnumerateArray());
        Assert.Equal(1, workout.GetProperty("weekInBlock").GetInt32());
        Assert.Equal(25, workout.GetProperty("sets")[0].GetProperty("weightLbs").GetDouble());

        var measurement = Assert.Single(root.GetProperty("measurements").EnumerateArray());
        Assert.Equal(2, measurement.GetProperty("photoCount").GetInt32());
        Assert.DoesNotContain("front.jpg", json); // photos never leave the phone

        Assert.Equal(-3, Assert.Single(root.GetProperty("fitnessTests").EnumerateArray()).GetProperty("toeTouchInches").GetDouble());
        Assert.Equal(120, root.GetProperty("plan").GetProperty("proteinTargetGrams")[0].GetInt32());

        var weeks = root.GetProperty("weeks");
        Assert.Equal(2, weeks.GetArrayLength()); // Nov 16 week and Nov 23 week
        Assert.Equal(1, weeks[1].GetProperty("sessionsDone").GetInt32());
    }

    [Fact]
    public void BuildJson_WithoutTrackers_StillWorks()
    {
        var json = HealthExportService.BuildJson(DateTime.UtcNow, "UTC", new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 22), [], []);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("workouts").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("days")[0].TryGetProperty("weather", out _));
    }

    [Fact]
    public async Task BuildJsonAsync_ReadsStoreAndRefreshesWeather()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"export-trackers-{Guid.NewGuid():N}.db3");
        var store = new LocalTrackerStore(dbPath);
        var migraines = new LocalMigraineService(dbPath);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await store.SaveCheckInAsync(new DailyCheckIn { Date = DailyCheckIn.Key(today), CaffeineCups = 2, ProteinGrams = 140 });
        var refreshed = 0;
        var exporter = new HealthExportService(new UnsupportedHealthDataService(), migraines,
            new UnsupportedExportDestination(), new InMemorySettingsStore())
        {
            Trackers = store,
            RefreshWeather = (_, days) => { refreshed = days; return Task.FromResult<string?>("Location not available yet."); }
        };

        var json = await exporter.BuildJsonAsync(today, 7);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(7, refreshed);
        Assert.Equal("Location not available yet.", doc.RootElement.GetProperty("weatherError").GetString());
        var last = doc.RootElement.GetProperty("days")[6];
        Assert.Equal("OnTarget", last.GetProperty("checkIn").GetProperty("proteinStatus").GetString());
        await store.Database.CloseAsync();
        await migraines.Database.CloseAsync();
    }
}
