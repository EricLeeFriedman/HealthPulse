using HealthPulse.Services;

namespace HealthPulse.Tests;

public class HealthSummaryBuilderTests
{
    static readonly DateOnly Nov1 = new(2026, 11, 1);
    static readonly DateOnly Nov3 = new(2026, 11, 3);

    [Fact]
    public void CreateDays_MakesOneEmptySummaryPerDateInclusive()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov3);
        Assert.Equal([Nov1, Nov1.AddDays(1), Nov3], days.Keys.OrderBy(d => d));
        Assert.All(days.Values, d => Assert.Equal("No data", d.Display));
    }

    [Fact]
    public void Sleep_CountsTowardTheDateItEnds_AndNapsAddToTotal()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov3);
        HealthSummaryBuilder.AddSleepSession(days, new DateTime(2026, 11, 1, 23, 0, 0), new DateTime(2026, 11, 2, 6, 30, 0));
        HealthSummaryBuilder.AddSleepSession(days, new DateTime(2026, 11, 2, 14, 0, 0), new DateTime(2026, 11, 2, 14, 45, 0));

        var nov2 = days[new DateOnly(2026, 11, 2)];
        Assert.Equal(8.25, nov2.SleepHours);
        Assert.Equal(new DateTime(2026, 11, 1, 23, 0, 0), nov2.SleepStart);
        Assert.Equal(new DateTime(2026, 11, 2, 6, 30, 0), nov2.SleepEnd);
        Assert.Null(days[Nov1].SleepHours);
    }

    [Fact]
    public void Sleep_MainNightIsTheLongestSession_EvenIfANapCameFirst()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov3);
        HealthSummaryBuilder.AddSleepSession(days, new DateTime(2026, 11, 2, 0, 30, 0), new DateTime(2026, 11, 2, 1, 0, 0));
        HealthSummaryBuilder.AddSleepSession(days, new DateTime(2026, 11, 2, 2, 0, 0), new DateTime(2026, 11, 2, 9, 0, 0));

        var nov2 = days[new DateOnly(2026, 11, 2)];
        Assert.Equal(7.5, nov2.SleepHours);
        Assert.Equal(new DateTime(2026, 11, 2, 2, 0, 0), nov2.SleepStart);
    }

    [Fact]
    public void Sleep_IgnoresSessionsOutsideRangeOrWithBadTimes()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov3);
        HealthSummaryBuilder.AddSleepSession(days, new DateTime(2026, 10, 30, 23, 0, 0), new DateTime(2026, 10, 31, 7, 0, 0));
        HealthSummaryBuilder.AddSleepSession(days, new DateTime(2026, 11, 2, 7, 0, 0), new DateTime(2026, 11, 2, 6, 0, 0));
        Assert.All(days.Values, d => Assert.Null(d.SleepHours));
    }

    [Fact]
    public void Hrv_IsTheDailyMean()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov3);
        HealthSummaryBuilder.SetHrv(days,
        [
            (new DateTime(2026, 11, 1, 3, 0, 0), 40),
            (new DateTime(2026, 11, 1, 4, 0, 0), 50),
            (new DateTime(2026, 11, 3, 4, 0, 0), 61)
        ]);
        Assert.Equal(45, days[Nov1].HrvRmssdMs);
        Assert.Equal(61, days[Nov3].HrvRmssdMs);
        Assert.Null(days[Nov1.AddDays(1)].HrvRmssdMs);
    }

    [Fact]
    public void Exercise_IsListedOnItsStartDateWithReadableType()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov3);
        HealthSummaryBuilder.AddExercise(days, 70, "Upper body", new DateTime(2026, 11, 1, 23, 30, 0), new DateTime(2026, 11, 2, 0, 15, 0));
        HealthSummaryBuilder.AddExercise(days, 79, "  ", new DateTime(2026, 11, 1, 7, 0, 0), new DateTime(2026, 11, 1, 7, 20, 0));

        var exercise = days[Nov1].Exercise;
        Assert.Equal(2, exercise.Count);
        Assert.Equal("strength_training", exercise[0].Type);
        Assert.Equal(45, exercise[0].Minutes);
        Assert.Null(exercise[1].Title);
        Assert.Contains("🏋️ 65 min", days[Nov1].Display);
    }

    [Theory]
    [InlineData(56, "running")]
    [InlineData(79, "walking")]
    [InlineData(81, "weightlifting")]
    [InlineData(0, "other")]
    [InlineData(999, "type_999")]
    public void ExerciseTypeName_MapsHealthConnectConstants(int type, string expected) =>
        Assert.Equal(expected, HealthSummaryBuilder.ExerciseTypeName(type));

    [Fact]
    public void Weight_LastReadingOfTheDayWins()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov3);
        HealthSummaryBuilder.SetWeights(days,
        [
            (new DateTime(2026, 11, 1, 20, 0, 0), 201.4),
            (new DateTime(2026, 11, 1, 7, 0, 0), 199.8)
        ]);
        Assert.Equal(201.4, days[Nov1].WeightLbs);
    }

    [Fact]
    public void Display_SummarisesAvailableValues()
    {
        var days = HealthSummaryBuilder.CreateDays(Nov1, Nov1);
        var day = days[Nov1];
        day.SleepHours = 7.4;
        day.Steps = 10500;
        day.RestingHeartRate = 57.6;
        Assert.Equal("😴 7.4 h  👣 10,500  ❤️ 58 rest", day.Display);
    }
}
