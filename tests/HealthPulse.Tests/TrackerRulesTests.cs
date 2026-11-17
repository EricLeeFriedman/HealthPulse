using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class TrackerRulesTests
{
    [Theory]
    [InlineData(null, TrackerRules.ProteinStatus.NotLogged)]
    [InlineData(90, TrackerRules.ProteinStatus.Below)]
    [InlineData(119, TrackerRules.ProteinStatus.Below)]
    [InlineData(120, TrackerRules.ProteinStatus.OnTarget)]
    [InlineData(150, TrackerRules.ProteinStatus.OnTarget)]
    [InlineData(151, TrackerRules.ProteinStatus.Above)]
    public void Protein_ComparesAgainst120To150(int? grams, TrackerRules.ProteinStatus expected) =>
        Assert.Equal(expected, TrackerRules.Protein(grams));

    [Fact]
    public void ProteinText_SaysHowFarShort()
    {
        Assert.Equal("30 g short of 120 g", TrackerRules.ProteinText(90));
        Assert.Equal("Target 120 to 150 g", TrackerRules.ProteinText(null));
        Assert.StartsWith("On target", TrackerRules.ProteinText(135));
    }

    [Fact]
    public void CheckInValidation_RejectsImpossibleValues()
    {
        TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-17", CaffeineCups = 3, Stress = 2, ProteinGrams = 130 });
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "" }));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-17", Stress = 6 }));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-17", Stress = 0 }));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-17", CaffeineCups = -1 }));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-17", ProteinGrams = 900 }));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new DailyCheckIn { Date = "2026-11-17", LastCaffeineTime = TimeSpan.FromHours(25) }));
    }

    [Fact]
    public void MeasurementAndTestValidation()
    {
        TrackerRules.Validate(new BodyMeasurement { WaistInches = 34.5 });
        var photoOnly = new BodyMeasurement();
        photoOnly.PhotoList = ["/data/photo.jpg"];
        TrackerRules.Validate(photoOnly);
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new BodyMeasurement()));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new BodyMeasurement { WaistInches = 5 }));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new BodyMeasurement { WeightLbs = 1000 }));

        TrackerRules.Validate(new FitnessTest { ToeTouchInches = -3.5 });
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new FitnessTest()));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new FitnessTest { PushUps = -1 }));
    }

    [Fact]
    public void WorkoutValidation()
    {
        TrackerRules.Validate(new WorkoutSession { SessionType = "Run", DurationMinutes = 24, DistanceMiles = 2.1 });
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new WorkoutSession()));
        Assert.Throws<ArgumentException>(() => TrackerRules.Validate(new WorkoutSession
        {
            SessionType = "Strength A", Sets = [new WorkoutSet { Exercise = "Goblet squat", WeightLbs = -5 }]
        }));
    }

    [Theory]
    [InlineData(null, "2026-11-17", true)]
    [InlineData("2026-11-10", "2026-11-17", true)]
    [InlineData("2026-11-11", "2026-11-17", false)]
    public void WeeklyMeasurementDue(string? last, string today, bool due) =>
        Assert.Equal(due, TrackerRules.IsWeeklyMeasurementDue(last == null ? null : DateOnly.Parse(last), DateOnly.Parse(today)));

    [Theory]
    [InlineData(null, "2026-12-06", true)]
    [InlineData("2026-11-30", "2026-12-01", true)]
    [InlineData("2026-12-01", "2026-12-31", false)]
    [InlineData("2025-12-15", "2026-12-20", true)]
    public void MonthlyDue_IsPerCalendarMonth(string? last, string today, bool due) =>
        Assert.Equal(due, TrackerRules.IsMonthlyDue(last == null ? null : DateOnly.Parse(last), DateOnly.Parse(today)));

    [Fact]
    public void Parsing_AcceptsBlankAndRejectsJunk()
    {
        Assert.Null(TrackerRules.ParseInt("  ", "Reps"));
        Assert.Equal(12, TrackerRules.ParseInt(" 12 ", "Reps"));
        Assert.Equal(-3.5, TrackerRules.ParseDouble("-3.5", "Toe touch"));
        Assert.Throws<ArgumentException>(() => TrackerRules.ParseInt("ten", "Reps"));
        Assert.Throws<ArgumentException>(() => TrackerRules.ParseDouble("abc", "Waist"));
    }
}
