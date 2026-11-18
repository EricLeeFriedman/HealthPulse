using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.Tests;

public class DailyRemindersTests
{
    static readonly DateOnly Monday = new(2026, 11, 23);
    static readonly DateOnly Saturday = new(2026, 11, 28);
    static readonly DateOnly Sunday = new(2026, 11, 29);

    static DailyCheckIn Done(DateOnly date) => new()
    {
        Date = DailyCheckIn.Key(date),
        Riboflavin = true, BComplex = true, CoQ10 = true, Magnesium = true,
        ProteinGrams = 130,
        UpdatedAt = DateTime.UtcNow,
    };

    static WorkoutSession Workout(DateOnly date, string type) =>
        new() { Date = date.ToDateTime(new TimeOnly(7, 0)), SessionType = type };

    [Theory]
    [InlineData("2026-11-18 06:00", "2026-11-18 09:00")]
    [InlineData("2026-11-18 09:00", "2026-11-18 12:00")]
    [InlineData("2026-11-18 13:30", "2026-11-18 15:00")]
    [InlineData("2026-11-18 20:59", "2026-11-18 21:00")]
    [InlineData("2026-11-18 21:00", "2026-11-19 09:00")]
    public void NextTime_EveryThreeHoursFromNineToNine(string now, string expected) =>
        Assert.Equal(DateTime.Parse(expected), DailyReminders.NextTime(DateTime.Parse(now)));

    [Fact]
    public void NothingDone_ListsEveryTask()
    {
        var tasks = DailyReminders.OpenTasks(Monday, new DailyCheckIn { UpdatedAt = DateTime.MinValue }, [], null);

        Assert.Equal(new[] { "preventives", "workout", "checkin", "measure" }, tasks.Select(t => t.Key));
        Assert.Contains("riboflavin, B complex, CoQ10, magnesium", tasks[0].Text);
        Assert.Equal("Do today's workout: Strength A", tasks[1].Text);
    }

    [Fact]
    public void AllDone_IsEmpty()
    {
        var tasks = DailyReminders.OpenTasks(Monday, Done(Monday), [Workout(Monday, WorkoutPlan.StrengthA)], Monday);
        Assert.Empty(tasks);
    }

    [Fact]
    public void PartialPreventives_NamesOnlyTheMissingOnes()
    {
        var checkIn = Done(Monday);
        checkIn.CoQ10 = false;
        var task = Assert.Single(DailyReminders.OpenTasks(Monday, checkIn, [Workout(Monday, WorkoutPlan.StrengthA)], Monday));
        Assert.Equal("Take preventives (CoQ10)", task.Text);
    }

    [Fact]
    public void Saturday_HasNoWorkout()
    {
        Assert.Empty(DailyReminders.OpenTasks(Saturday, Done(Saturday), [], Saturday));
    }

    [Fact]
    public void AnyStrengthSessionCountsOnAStrengthDay_ButARunDoesNot()
    {
        Assert.Empty(DailyReminders.OpenTasks(Monday, Done(Monday), [Workout(Monday, WorkoutPlan.StrengthC)], Monday));
        var task = Assert.Single(DailyReminders.OpenTasks(Monday, Done(Monday), [Workout(Monday, WorkoutPlan.Run)], Monday));
        Assert.Equal("workout", task.Key);
    }

    [Fact]
    public void HiddenOrOtherDayWorkouts_DontCount()
    {
        var hidden = Workout(Monday, WorkoutPlan.StrengthA);
        hidden.Hidden = true;
        var yesterday = Workout(Monday.AddDays(-1), WorkoutPlan.StrengthA);
        Assert.Single(DailyReminders.OpenTasks(Monday, Done(Monday), [hidden, yesterday], Monday));
    }

    [Fact]
    public void SundayMobility_DoneByCheckboxOrSession()
    {
        var checkIn = Done(Sunday);
        Assert.Equal("Do your mobility routine (steps on the Today tab)", Assert.Single(DailyReminders.OpenTasks(Sunday, checkIn, [], Sunday)).Text);
        checkIn.MobilityDone = true;
        Assert.Empty(DailyReminders.OpenTasks(Sunday, checkIn, [], Sunday));
        Assert.Empty(DailyReminders.OpenTasks(Sunday, Done(Sunday), [Workout(Sunday, WorkoutPlan.Mobility)], Sunday));
    }

    [Fact]
    public void Protein_IsNeverAReminder()
    {
        var checkIn = Done(Saturday);
        checkIn.ProteinGrams = null;
        Assert.Empty(DailyReminders.OpenTasks(Saturday, checkIn, [], Saturday));
    }

    [Fact]
    public void WeeklyMeasurement_DueAfterSevenDays()
    {
        Assert.Empty(DailyReminders.OpenTasks(Saturday, Done(Saturday), [], Saturday.AddDays(-6)));
        Assert.Equal("measure", Assert.Single(DailyReminders.OpenTasks(Saturday, Done(Saturday), [], Saturday.AddDays(-7))).Key);
    }

    [Fact]
    public void ShouldNotify_RespectsStopForTodayOffSwitchHoursAndEmptyList()
    {
        var settings = new InMemorySettingsStore();
        var noon = new DateTime(2026, 11, 23, 12, 0, 0);
        var open = new List<ReminderTask> { new("checkin", "Log today's check-in") };

        Assert.True(DailyReminders.ShouldNotify(noon, settings, open));
        Assert.False(DailyReminders.ShouldNotify(noon, settings, []));
        Assert.False(DailyReminders.ShouldNotify(noon.Date.AddHours(8), settings, open));
        Assert.True(DailyReminders.ShouldNotify(noon.Date.AddHours(21).AddMinutes(30), settings, open));
        Assert.False(DailyReminders.ShouldNotify(noon.Date.AddHours(22), settings, open));

        DailyReminders.StopFor(settings, DateOnly.FromDateTime(noon));
        Assert.False(DailyReminders.ShouldNotify(noon, settings, open));
        Assert.True(DailyReminders.ShouldNotify(noon.AddDays(1), settings, open));
        DailyReminders.Resume(settings);
        Assert.True(DailyReminders.ShouldNotify(noon, settings, open));

        DailyReminders.SetEnabled(settings, false);
        Assert.False(DailyReminders.ShouldNotify(noon, settings, open));
        DailyReminders.SetEnabled(settings, true);
        Assert.True(DailyReminders.IsEnabled(settings));
    }

    [Fact]
    public void Text_TitleBodyAndStatus()
    {
        var settings = new InMemorySettingsStore();
        var tasks = new List<ReminderTask> { new("a", "Take preventives (CoQ10)"), new("b", "Log weekly waist and weight") };
        var noon = new DateTime(2026, 11, 23, 12, 0, 0);

        Assert.Equal("2 things left today", DailyReminders.Title(tasks));
        Assert.Equal("• Take preventives (CoQ10)\n• Log weekly waist and weight", DailyReminders.Body(tasks));
        Assert.Equal("Next reminder at 3 PM for 2 open items.", DailyReminders.StatusText(noon, settings, tasks));
        Assert.StartsWith("Everything's done", DailyReminders.StatusText(noon, settings, []));
        DailyReminders.StopFor(settings, DateOnly.FromDateTime(noon));
        Assert.StartsWith("Reminders are stopped for today", DailyReminders.StatusText(noon, settings, tasks));
    }

    [Fact]
    public async Task LoadOpenTasks_ReadsTheStore()
    {
        var store = new LocalTrackerStore(Path.Combine(Path.GetTempPath(), $"remind-{Guid.NewGuid():N}.db3"));
        await store.SaveCheckInAsync(Done(Saturday));
        await store.SaveMeasurementAsync(new BodyMeasurement { Date = Saturday.ToDateTime(new TimeOnly(8, 0)), WaistInches = 34 });

        Assert.Empty(await DailyReminders.LoadOpenTasksAsync(store, Saturday));
        var monday = await DailyReminders.LoadOpenTasksAsync(store, Saturday.AddDays(2));
        Assert.Contains(monday, t => t.Key == "workout");
        await store.Database.CloseAsync();
    }
}
