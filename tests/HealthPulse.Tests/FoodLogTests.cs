using System.Text.Json;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public sealed class FoodLogTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"food-{Guid.NewGuid():N}.db3");
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
    public void Validate_RequiresTextAndSaneProtein()
    {
        FoodLog.Validate(new FoodEntry { Text = "3 eggs, toast", ProteinGrams = 25 });
        Assert.Throws<ArgumentException>(() => FoodLog.Validate(new FoodEntry { Text = "   " }));
        Assert.Throws<ArgumentException>(() => FoodLog.Validate(new FoodEntry { Text = new string('x', 501) }));
        Assert.Throws<ArgumentException>(() => FoodLog.Validate(new FoodEntry { Text = "steak", ProteinGrams = 900 }));
    }

    [Fact]
    public void ProteinTotal_IgnoresEntriesWithoutEstimates()
    {
        Assert.Null(FoodLog.ProteinTotal([new FoodEntry { Text = "apple" }]));
        Assert.Equal(65, FoodLog.ProteinTotal(
        [
            new FoodEntry { Text = "3 eggs", ProteinGrams = 20 },
            new FoodEntry { Text = "apple" },
            new FoodEntry { Text = "chicken bowl", ProteinGrams = 45 },
        ]));
    }

    [Fact]
    public void ApplyTo_FillsProteinAndFirstMealTime()
    {
        var checkIn = new DailyCheckIn { Date = "2026-11-17", ProteinGrams = 50 };
        var meals = new List<FoodEntry>
        {
            new() { Time = new DateTime(2026, 11, 17, 12, 10, 30), Text = "chicken bowl", ProteinGrams = 45 },
            new() { Time = new DateTime(2026, 11, 17, 9, 5, 0), Text = "greek yogurt", ProteinGrams = 22 },
        };

        Assert.True(FoodLog.ApplyTo(checkIn, meals));
        Assert.Equal(67, checkIn.ProteinGrams);
        Assert.Equal(new TimeSpan(9, 5, 0), checkIn.FirstMealTime);
        Assert.False(FoodLog.ApplyTo(checkIn, meals)); // nothing new

        // An earlier first meal set by hand is kept.
        var manual = new DailyCheckIn { Date = "2026-11-17", FirstMealTime = new TimeSpan(7, 0, 0) };
        FoodLog.ApplyTo(manual, meals);
        Assert.Equal(new TimeSpan(7, 0, 0), manual.FirstMealTime);

        // Meals without estimates leave a typed protein total alone.
        var typed = new DailyCheckIn { Date = "2026-11-17", ProteinGrams = 130 };
        FoodLog.ApplyTo(typed, [new FoodEntry { Time = new DateTime(2026, 11, 17, 13, 0, 0), Text = "salad" }]);
        Assert.Equal(130, typed.ProteinGrams);
    }

    [Fact]
    public async Task Store_SavesTrimsListsByDayAndDeletes()
    {
        var breakfast = new FoodEntry { Time = new DateTime(2026, 11, 17, 9, 0, 0), Text = "  3 eggs, toast  ", ProteinGrams = 20 };
        await store.SaveFoodAsync(breakfast);
        await store.SaveFoodAsync(new FoodEntry { Time = new DateTime(2026, 11, 17, 12, 30, 0), Text = "chicken bowl" });
        await store.SaveFoodAsync(new FoodEntry { Time = new DateTime(2026, 11, 18, 8, 0, 0), Text = "oatmeal" });

        var day = await store.GetFoodAsync(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 17));
        Assert.Equal(2, day.Count);
        Assert.Equal("3 eggs, toast", day[0].Text);
        Assert.Equal("3 eggs, toast  •  20 g protein", day[0].Display);

        await store.DeleteFoodAsync(breakfast.Id);
        Assert.Single(await store.GetFoodAsync(new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 17)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveFoodAsync(new FoodEntry { Text = "" }));
    }

    [Fact]
    public void Export_IncludesMealsPerDay()
    {
        var trackers = TrackerExport.Empty with
        {
            Food =
            [
                new FoodEntry { Time = new DateTime(2026, 11, 17, 9, 5, 0), Text = "3 eggs, toast", ProteinGrams = 20 },
                new FoodEntry { Time = new DateTime(2026, 11, 17, 12, 30, 0), Text = "big chicken salad" },
            ]
        };

        var json = HealthExportService.BuildJson(DateTime.UtcNow, "UTC",
            new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17), [], [], trackers: trackers);

        using var doc = JsonDocument.Parse(json);
        var days = doc.RootElement.GetProperty("days");
        Assert.False(days[0].TryGetProperty("meals", out _));
        var meals = days[1].GetProperty("meals");
        Assert.Equal(2, meals.GetArrayLength());
        Assert.Equal("09:05", meals[0].GetProperty("time").GetString());
        Assert.Equal("big chicken salad", meals[1].GetProperty("text").GetString());
        Assert.False(meals[1].TryGetProperty("proteinGrams", out _));
        Assert.Equal(20, days[1].GetProperty("mealProteinGrams").GetInt32());
    }

    [Fact]
    public void Count_DefaultsToOneAndScalesProteinAndDisplay()
    {
        var old = new FoodEntry { Text = "clementine", ProteinGrams = 1 };
        Assert.Null(old.Quantity);
        Assert.Equal(1, old.Count);
        Assert.Equal("clementine  •  1 g protein", old.Display);

        var two = new FoodEntry { Text = "Chobani 6.7 oz", ProteinGrams = 16, Count = 2 };
        Assert.Equal(32, two.TotalProteinGrams);
        Assert.Equal("2 × Chobani 6.7 oz  •  32 g protein", two.Display);
        Assert.Equal(1 + 32, FoodLog.ProteinTotal([old, two]));

        // A stored 0 (or anything below 1) reads as 1.
        Assert.Equal(1, new FoodEntry { Text = "apple", Quantity = 0 }.Count);
    }

    [Fact]
    public void Validate_AndStepCount_KeepCountWithinRange()
    {
        FoodLog.Validate(new FoodEntry { Text = "clementine", Count = 2 });
        FoodLog.Validate(new FoodEntry { Text = "clementine" });
        Assert.Throws<ArgumentException>(() => FoodLog.Validate(new FoodEntry { Text = "clementine", Count = 0 }));
        Assert.Throws<ArgumentException>(() => FoodLog.Validate(new FoodEntry { Text = "clementine", Count = FoodLog.MaxCount + 1 }));

        Assert.Equal(2, FoodLog.StepCount(1, 1));
        Assert.Equal(1, FoodLog.StepCount(1, -1));
        Assert.Equal(FoodLog.MaxCount, FoodLog.StepCount(FoodLog.MaxCount, 1));
    }

    [Fact]
    public void ApplyTo_ScalesProteinByCount()
    {
        var checkIn = new DailyCheckIn { Date = "2026-11-25" };
        FoodLog.ApplyTo(checkIn,
        [
            new FoodEntry { Time = new DateTime(2026, 11, 25, 9, 0, 0), Text = "egg", ProteinGrams = 6, Count = 3 },
            new FoodEntry { Time = new DateTime(2026, 11, 25, 12, 0, 0), Text = "chicken bowl", ProteinGrams = 40 },
        ]);
        Assert.Equal(58, checkIn.ProteinGrams);
    }

    [Fact]
    public async Task Store_KeepsCountAndReadsOlderEntriesAsOne()
    {
        // A database from before counts existed: the FoodEntry table has no Quantity column.
        await store.Database.ExecuteAsync(
            "create table FoodEntry (Id varchar primary key not null, Time bigint, Text varchar, ProteinGrams integer, " +
            "Portion varchar, Ingredients varchar, PhotoPath varchar, PhotoName varchar, PhotoDriveId varchar, UpdatedAt bigint)");
        await store.Database.ExecuteAsync(
            "insert into FoodEntry (Id, Time, Text, Ingredients, UpdatedAt) values (?, ?, ?, '', ?)",
            "old", new DateTime(2026, 11, 24, 8, 0, 0).Ticks, "clementine", DateTime.UtcNow.Ticks);

        await store.SaveFoodAsync(new FoodEntry { Time = new DateTime(2026, 11, 24, 15, 0, 0), Text = "clementine", Count = 2 });

        var day = await store.GetFoodAsync(new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24));
        Assert.Equal(2, day.Count);
        Assert.Equal(1, day[0].Count);
        Assert.Equal(2, day[1].Count);
        Assert.Equal("2 × clementine", day[1].Display);

        day[1].Count = 3;
        await store.SaveFoodAsync(day[1]);
        Assert.Equal(3, (await store.GetFoodAsync(new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24)))[1].Count);
    }

    [Fact]
    public void Export_IncludesCountAndScaledProtein()
    {
        var trackers = TrackerExport.Empty with
        {
            Food =
            [
                new FoodEntry { Time = new DateTime(2026, 11, 25, 9, 0, 0), Text = "egg", ProteinGrams = 6, Count = 3 },
                new FoodEntry { Time = new DateTime(2026, 11, 25, 15, 0, 0), Text = "clementine" },
            ]
        };

        var json = HealthExportService.BuildJson(DateTime.UtcNow, "UTC",
            new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 25), [], [], trackers: trackers);

        using var doc = JsonDocument.Parse(json);
        var day = doc.RootElement.GetProperty("days")[0];
        var meals = day.GetProperty("meals");
        Assert.Equal(3, meals[0].GetProperty("count").GetInt32());
        Assert.Equal(18, meals[0].GetProperty("proteinGrams").GetInt32());
        Assert.Equal(6, meals[0].GetProperty("proteinGramsEach").GetInt32());
        Assert.Equal(1, meals[1].GetProperty("count").GetInt32());
        Assert.False(meals[1].TryGetProperty("proteinGramsEach", out _));
        Assert.Equal(18, day.GetProperty("mealProteinGrams").GetInt32());
    }
}
