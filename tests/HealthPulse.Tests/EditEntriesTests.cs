using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public sealed class EditEntriesTests : IAsyncLifetime
{
    readonly LocalTrackerStore store = new(Path.Combine(Path.GetTempPath(), $"edit-{Guid.NewGuid():N}.db3"));
    static readonly DateOnly Day = new(2026, 11, 17);

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await store.Database.CloseAsync();

    [Fact]
    public async Task EditingFood_UpdatesTheSameEntry()
    {
        var entry = new FoodEntry { Time = Day.ToDateTime(new TimeOnly(8, 0)), Text = "2 eggs" };
        await store.SaveFoodAsync(entry);

        entry.Text = "3 eggs, toast";
        entry.Time = Day.ToDateTime(new TimeOnly(8, 30));
        await store.SaveFoodAsync(entry);

        var saved = Assert.Single(await store.GetFoodAsync(Day, Day));
        Assert.Equal("3 eggs, toast", saved.Text);
        Assert.Equal(new TimeSpan(8, 30, 0), saved.Time.TimeOfDay);
    }

    [Fact]
    public async Task EditingMeasurementAndTest_UpdatesInPlace()
    {
        var measurement = new BodyMeasurement { Date = Day.ToDateTime(new TimeOnly(7, 0)), WaistInches = 35 };
        var test = new FitnessTest { Date = Day.ToDateTime(new TimeOnly(7, 0)), PushUps = 20 };
        await store.SaveMeasurementAsync(measurement);
        await store.SaveFitnessTestAsync(test);

        measurement.WaistInches = 34.5;
        test.PushUps = 22;
        await store.SaveMeasurementAsync(measurement);
        await store.SaveFitnessTestAsync(test);

        Assert.Equal(34.5, Assert.Single(await store.GetMeasurementsAsync(Day, Day)).WaistInches);
        Assert.Equal(22, Assert.Single(await store.GetFitnessTestsAsync(Day, Day)).PushUps);
    }

    [Fact]
    public void History_ListsBothKindsNewestFirst()
    {
        var older = new BodyMeasurement { Date = Day.ToDateTime(new TimeOnly(7, 0)).AddDays(-7), WaistInches = 35, WeightLbs = 180.4 };
        var newer = new BodyMeasurement { Date = Day.ToDateTime(new TimeOnly(7, 0)) };
        newer.PhotoList = ["a.jpg", "b.jpg"];
        var test = new FitnessTest { Date = Day.ToDateTime(new TimeOnly(9, 0)).AddDays(-3), ToeTouchInches = -3, PlankSeconds = 60 };

        var items = MeasureHistory.Build([older, newer], [test]);

        Assert.Equal(new[] { newer.Id, test.Id, older.Id }, items.Select(i => i.Id));
        Assert.EndsWith("2 photos", items[0].Display);
        Assert.Equal(MeasureHistoryItem.Test, items[1].Kind);
        Assert.EndsWith("test: toe touch -3 in, plank 60 s", items[1].Display);
        Assert.EndsWith("waist 35 in, 180.4 lb", items[2].Display);
    }
}
