using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public sealed class WaterLogTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"water-{Guid.NewGuid():N}.db3");
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
    public void OldGlassOnlyDays_CountEightOuncesPerGlass()
    {
        Assert.Equal(48, WaterLog.Ounces(new DailyCheckIn { WaterGlasses = 6 }));
        Assert.Equal(0, WaterLog.Ounces(new DailyCheckIn()));
        // Once ounces are stored they win over the glass count.
        Assert.Equal(60, WaterLog.Ounces(new DailyCheckIn { WaterGlasses = 6, WaterOunces = 60 }));
    }

    [Fact]
    public void Set_KeepsGlassesAsNearestWholeGlass()
    {
        var checkIn = new DailyCheckIn();
        WaterLog.Set(checkIn, 60);
        Assert.Equal(60, checkIn.WaterOunces);
        Assert.Equal(8, checkIn.WaterGlasses); // 7.5 rounds up
        WaterLog.Set(checkIn, 40);
        Assert.Equal(5, checkIn.WaterGlasses);
        WaterLog.Set(checkIn, 3);
        Assert.Equal(0, checkIn.WaterGlasses);
    }

    [Fact]
    public void Add_ClampsBetweenZeroAndMax()
    {
        Assert.Equal(60, WaterLog.Add(20, WaterLog.StanleyOunces));
        Assert.Equal(0, WaterLog.Add(10, -20));
        Assert.Equal(WaterLog.MaxOunces, WaterLog.Add(390, 40));
    }

    [Fact]
    public void QuickAmounts_IncludeStanleyAndHalf()
    {
        Assert.Contains(WaterLog.QuickAmounts, a => a.Ounces == 40 && a.Label.Contains("Stanley"));
        Assert.Contains(WaterLog.QuickAmounts, a => a.Ounces == 20 && a.Label.Contains("½ Stanley"));
        Assert.Contains(WaterLog.QuickAmounts, a => a.Ounces == 8);
    }

    [Fact]
    public void TextAndStatus_ShowOuncesAgainstTarget()
    {
        Assert.Equal("Water: 52 of 80 oz", WaterLog.Text(52));
        Assert.Equal("28 oz to go", WaterLog.Status(52));
        Assert.Equal("Target reached", WaterLog.Status(80));
        Assert.Equal("Target reached", WaterLog.Status(100));
    }

    [Theory]
    [InlineData("16", 16)]
    [InlineData(" 40 ", 40)]
    [InlineData("12.5", 13)]
    public void ParseOunces_AcceptsWholeAndDecimalAmounts(string text, int expected) =>
        Assert.Equal(expected, WaterLog.ParseOunces(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1000")]
    public void ParseOunces_RejectsUnusableAmounts(string text) =>
        Assert.Throws<ArgumentException>(() => WaterLog.ParseOunces(text));

    [Fact]
    public void Validate_ChecksOunces()
    {
        TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-19", WaterOunces = 120, WaterGlasses = 15 });
        TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-19", WaterGlasses = 30 });
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-19", WaterOunces = 500 }));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-19", WaterOunces = -1 }));
    }

    [Fact]
    public async Task Store_RoundTripsOunces_AndOldRowsStillCount()
    {
        var oldDay = new DailyCheckIn { Date = "2026-11-18", WaterGlasses = 7 };
        await store.SaveCheckInAsync(oldDay);
        var newDay = new DailyCheckIn { Date = "2026-11-19" };
        WaterLog.Set(newDay, 60);
        await store.SaveCheckInAsync(newDay);

        var old = await store.GetCheckInAsync(new DateOnly(2026, 11, 18));
        Assert.Null(old.WaterOunces);
        Assert.Equal(56, WaterLog.Ounces(old));
        var saved = await store.GetCheckInAsync(new DateOnly(2026, 11, 19));
        Assert.Equal(60, saved.WaterOunces);
        Assert.Equal(60, WaterLog.Ounces(saved));
    }
}
