using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class FrequentFoodsTests
{
    static FoodEntry Food(string text, int day, int? protein = null) =>
        new() { Text = text, Time = new DateTime(2026, 11, day, 8, 0, 0), ProteinGrams = protein };

    [Fact]
    public void Build_SortsByFrequencyThenRecency()
    {
        var list = FrequentFoods.Build(
        [
            Food("Chobani 6.7 oz", 1), Food("chobani 6.7 oz", 2), Food("Chobani  6.7 oz.", 3),
            Food("3 eggs, toast", 1), Food("3 eggs, toast", 4),
            Food("apple", 2), Food("banana", 5),
        ]);

        Assert.Equal(new[] { "Chobani  6.7 oz.", "3 eggs, toast", "banana", "apple" }, list.Select(f => f.Text));
        Assert.Equal(new[] { 3, 2, 1, 1 }, list.Select(f => f.Count));
    }

    [Fact]
    public void Build_UsesLatestSpellingAndLatestProtein()
    {
        var food = Assert.Single(FrequentFoods.Build(
        [
            Food("Chobani 6.7 oz", 1, 15), Food("chobani 6.7 OZ", 2, 16), Food("Chobani 6.7 oz", 3),
        ]));
        Assert.Equal("Chobani 6.7 oz", food.Text);
        Assert.Equal(16, food.ProteinGrams);
        Assert.Equal("Chobani 6.7 oz  ×3", food.Display);
    }

    [Fact]
    public void Build_CapsTheListAndSkipsBlank()
    {
        var entries = Enumerable.Range(1, 20).Select(i => Food($"food {i}", 1)).Append(Food("  ", 2));
        Assert.Equal(FrequentFoods.MaxItems, FrequentFoods.Build(entries).Count);
        Assert.Empty(FrequentFoods.Build([Food(" ", 1)]));
    }

    [Fact]
    public void Window_CoversTheLookBackEndingToday()
    {
        var (from, to) = FrequentFoods.Window(new DateTime(2026, 11, 19, 18, 0, 0));
        Assert.Equal(new DateOnly(2026, 11, 19), to);
        Assert.Equal(FrequentFoods.LookBackDays, to.DayNumber - from.DayNumber + 1);
    }

    [Fact]
    public async Task Build_FromStore_UpdatesAsFoodIsLogged()
    {
        var path = Path.Combine(Path.GetTempPath(), $"freq-{Guid.NewGuid():N}.db3");
        var store = new LocalTrackerStore(path);
        try
        {
            var now = DateTime.Now;
            await store.SaveFoodAsync(new FoodEntry { Text = "apple", Time = now.AddDays(-1) });
            await store.SaveFoodAsync(new FoodEntry { Text = "Chobani 6.7 oz", Time = now.AddDays(-2) });
            await store.SaveFoodAsync(new FoodEntry { Text = "Chobani 6.7 oz", Time = now.AddMinutes(-1) });
            await store.SaveFoodAsync(new FoodEntry { Text = "old cereal", Time = now.AddDays(-(FrequentFoods.LookBackDays + 5)) });

            var (from, to) = FrequentFoods.Window(now);
            var list = FrequentFoods.Build(await store.GetFoodAsync(from, to));
            Assert.Equal(new[] { "Chobani 6.7 oz", "apple" }, list.Select(f => f.Text));

            await store.SaveFoodAsync(new FoodEntry { Text = "apple", Time = now.AddMinutes(-2) });
            await store.SaveFoodAsync(new FoodEntry { Text = "apple", Time = now.AddMinutes(-3) });
            list = FrequentFoods.Build(await store.GetFoodAsync(from, to));
            Assert.Equal(new[] { "apple", "Chobani 6.7 oz" }, list.Select(f => f.Text));
        }
        finally
        {
            await store.Database.CloseAsync();
            File.Delete(path);
        }
    }
}
