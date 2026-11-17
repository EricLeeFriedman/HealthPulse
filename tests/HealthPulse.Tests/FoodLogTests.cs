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
}
