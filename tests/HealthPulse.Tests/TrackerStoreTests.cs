using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public sealed class TrackerStoreTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"trackers-{Guid.NewGuid():N}.db3");
    LocalTrackerStore store = null!;

    public Task InitializeAsync()
    {
        store = new LocalTrackerStore(path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await store.Database.CloseAsync();
        File.Delete(path);
    }

    [Fact]
    public async Task CheckIn_MissingDayIsBlankThenRoundTrips()
    {
        var day = new DateOnly(2026, 11, 17);
        var blank = await store.GetCheckInAsync(day);
        Assert.Equal("2026-11-17", blank.Date);
        Assert.Equal(DateTime.MinValue, blank.UpdatedAt);

        await store.SaveCheckInAsync(new DailyCheckIn
        {
            Date = "2026-11-17", CaffeineCups = 3, LastCaffeineTime = new TimeSpan(12, 15, 0),
            WaterGlasses = 6, FirstMealTime = new TimeSpan(12, 0, 0), AlcoholDrinks = 1, Stress = 3,
            Riboflavin = true, BComplex = true, CoQ10 = true, Magnesium = true, ProteinGrams = 110, MobilityDone = true, Jublia = true
        });

        var saved = await store.GetCheckInAsync(day);
        Assert.Equal(3, saved.CaffeineCups);
        Assert.Equal(new TimeSpan(12, 15, 0), saved.LastCaffeineTime);
        Assert.Equal(new TimeSpan(12, 0, 0), saved.FirstMealTime);
        Assert.Equal(3, saved.Stress);
        Assert.True(saved.AllSupplementsTaken);
        Assert.Equal(110, saved.ProteinGrams);
        Assert.True(saved.Jublia);
        Assert.NotEqual(DateTime.MinValue, saved.UpdatedAt);

        // Saving the same day again replaces it.
        saved.CaffeineCups = 2;
        await store.SaveCheckInAsync(saved);
        var range = await store.GetCheckInsAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        Assert.Equal(2, Assert.Single(range).CaffeineCups);
        Assert.Empty(await store.GetCheckInsAsync(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public async Task CheckIn_InvalidIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveCheckInAsync(new DailyCheckIn { Date = "2026-11-17", Stress = 9 }));
    }

    [Fact]
    public async Task Workout_SavesSetsAndEditingReplacesThem()
    {
        var session = new WorkoutSession
        {
            Date = new DateTime(2026, 11, 23, 7, 30, 0),
            SessionType = WorkoutPlan.StrengthA,
            DurationMinutes = 30,
            Sets =
            [
                new WorkoutSet { Exercise = "Goblet squat", Reps = 10, WeightLbs = 25 },
                new WorkoutSet { Exercise = "Goblet squat", Reps = 10, WeightLbs = 25 },
                new WorkoutSet { Exercise = "Push-ups", Reps = 12 },
            ]
        };
        await store.SaveWorkoutAsync(session);

        var loaded = Assert.Single(await store.GetWorkoutsAsync(new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 23)));
        Assert.Equal(3, loaded.Sets.Count);
        Assert.Equal("Push-ups", loaded.Sets[2].Exercise);
        Assert.Contains("2 exercises, 3 sets", loaded.Summary);

        loaded.Sets = [new WorkoutSet { Exercise = "Goblet squat", Reps = 12, WeightLbs = 30 }];
        await store.SaveWorkoutAsync(loaded);
        var edited = await store.GetWorkoutAsync(session.Id);
        Assert.NotNull(edited);
        var set = Assert.Single(edited.Sets);
        Assert.Equal(30, set.WeightLbs);

        await store.DeleteWorkoutAsync(session.Id);
        Assert.Null(await store.GetWorkoutAsync(session.Id));
        Assert.Empty(await store.Database.Table<WorkoutSet>().ToListAsync());
    }

    [Fact]
    public async Task Measurements_TestsAndRanges()
    {
        var measurement = new BodyMeasurement { Date = new DateTime(2026, 11, 22, 8, 0, 0), WaistInches = 34.5, WeightLbs = 165 };
        measurement.PhotoList = ["/data/photos/front.jpg", "/data/photos/side.jpg"];
        await store.SaveMeasurementAsync(measurement);
        await store.SaveMeasurementAsync(new BodyMeasurement { Date = new DateTime(2026, 12, 6, 8, 0, 0), WaistInches = 34 });
        await store.SaveFitnessTestAsync(new FitnessTest { Date = new DateTime(2026, 12, 6, 8, 0, 0), ToeTouchInches = -3, PushUps = 18 });

        var nov = await store.GetMeasurementsAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        Assert.Equal(2, Assert.Single(nov).PhotoList.Count);
        Assert.Equal(2, (await store.GetMeasurementsAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31))).Count);
        var test = Assert.Single(await store.GetFitnessTestsAsync(new DateOnly(2026, 12, 6), new DateOnly(2026, 12, 6)));
        Assert.Equal(-3, test.ToeTouchInches);

        await store.DeleteMeasurementAsync(measurement.Id);
        await store.DeleteFitnessTestAsync(test.Id);
        Assert.Single(await store.GetMeasurementsAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31)));
        Assert.Empty(await store.GetFitnessTestsAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public async Task Weather_UpsertsByDate()
    {
        await store.SaveWeatherAsync([
            new WeatherDay { Date = "2026-11-16", PressureMeanHpa = 1015, Hours = 24 },
            new WeatherDay { Date = "2026-11-17", PressureMeanHpa = 1010, Hours = 12 },
        ]);
        await store.SaveWeatherAsync([new WeatherDay { Date = "2026-11-17", PressureMeanHpa = 1008, Hours = 24 }]);

        var days = await store.GetWeatherAsync(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17));
        Assert.Equal(2, days.Count);
        Assert.Equal(1008, days[1].PressureMeanHpa);
        Assert.Single(await store.GetWeatherAsync(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 20)));
    }
}
