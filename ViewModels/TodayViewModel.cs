using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class TodayViewModel : ObservableObject
{
    public ITrackerStore Store;
    public WeatherService Weather;
    public HealthExportService Exporter;
    public ErrorLog Errors;
    bool loading;

    public TodayViewModel(ITrackerStore store, WeatherService weather, HealthExportService exporter, ErrorLog errors)
    {
        Store = store;
        Weather = weather;
        Exporter = exporter;
        Errors = errors;
    }

    public List<string> StressOptions { get; } =
        ["Not rated", "1 - calm", "2 - a little", "3 - moderate", "4 - high", "5 - very high"];

    [ObservableProperty] public partial DateTime Date { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string PlanText { get; set; } = "";
    [ObservableProperty] public partial string TargetText { get; set; } = "";
    [ObservableProperty] public partial string WeatherText { get; set; } = "";
    [ObservableProperty] public partial string SavedText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaffeineText))]
    public partial double CaffeineCups { get; set; }
    public string CaffeineText => $"Caffeine: {(int)CaffeineCups} cup{((int)CaffeineCups == 1 ? "" : "s")}";
    [ObservableProperty] public partial bool HasLastCaffeine { get; set; }
    [ObservableProperty] public partial TimeSpan LastCaffeineTime { get; set; } = new(12, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaterText))]
    public partial double WaterGlasses { get; set; }
    public string WaterText => $"Water: {(int)WaterGlasses} glass{((int)WaterGlasses == 1 ? "" : "es")} (8 oz)";

    [ObservableProperty] public partial bool HasFirstMeal { get; set; }
    [ObservableProperty] public partial TimeSpan FirstMealTime { get; set; } = new(12, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlcoholText))]
    public partial double AlcoholDrinks { get; set; }
    public string AlcoholText => $"Alcohol: {(int)AlcoholDrinks} drink{((int)AlcoholDrinks == 1 ? "" : "s")}";

    [ObservableProperty] public partial int StressIndex { get; set; }

    [ObservableProperty] public partial bool Riboflavin { get; set; }
    [ObservableProperty] public partial bool BComplex { get; set; }
    [ObservableProperty] public partial bool CoQ10 { get; set; }
    [ObservableProperty] public partial bool Magnesium { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProteinStatusText))]
    public partial string ProteinGrams { get; set; } = "";
    public string ProteinStatusText
    {
        get
        {
            try { return TrackerRules.ProteinText(TrackerRules.ParseInt(ProteinGrams, "Protein")); }
            catch (ArgumentException) { return "Enter grams as a whole number"; }
        }
    }

    [ObservableProperty] public partial bool MobilityDone { get; set; }
    [ObservableProperty] public partial string MobilityLabel { get; set; } = "Mobility routine done";
    [ObservableProperty] public partial string Notes { get; set; } = "";

    partial void OnDateChanged(DateTime value)
    {
        if (!loading) _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (loading) return;
        loading = true;
        try
        {
            var day = DateOnly.FromDateTime(Date);
            var planned = WorkoutPlan.PlannedFor(day.DayOfWeek);
            PlanText = planned == null
                ? $"{day:dddd}: off. Rest day."
                : $"{day:dddd}: {planned} (block {WorkoutPlan.Block(day)}, week {WorkoutPlan.WeekInBlock(day)})";
            TargetText = planned == null ? "" : WorkoutPlan.TargetFor(planned, day);
            MobilityLabel = day.DayOfWeek == DayOfWeek.Sunday ? "Sunday mobility routine done" : "Mobility routine done";

            var c = await Store.GetCheckInAsync(day);
            CaffeineCups = c.CaffeineCups;
            HasLastCaffeine = c.LastCaffeineTime.HasValue;
            LastCaffeineTime = c.LastCaffeineTime ?? new TimeSpan(12, 0, 0);
            WaterGlasses = c.WaterGlasses;
            HasFirstMeal = c.FirstMealTime.HasValue;
            FirstMealTime = c.FirstMealTime ?? new TimeSpan(12, 0, 0);
            AlcoholDrinks = c.AlcoholDrinks;
            StressIndex = c.Stress ?? 0;
            Riboflavin = c.Riboflavin;
            BComplex = c.BComplex;
            CoQ10 = c.CoQ10;
            Magnesium = c.Magnesium;
            ProteinGrams = c.ProteinGrams?.ToString(CultureInfo.CurrentCulture) ?? "";
            MobilityDone = c.MobilityDone;
            Notes = c.Notes ?? "";
            SavedText = c.UpdatedAt == DateTime.MinValue
                ? "Not checked in yet"
                : $"Saved {DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc).ToLocalTime():h:mm tt}";

            await RefreshWeatherTextAsync(day);
        }
        catch (Exception exception)
        {
            Errors.Record("Loading check-in", exception);
            SavedText = $"Couldn't load: {exception.Message}";
        }
        finally
        {
            loading = false;
        }
    }

    async Task RefreshWeatherTextAsync(DateOnly day)
    {
        var stored = (await Store.GetWeatherAsync(day, day)).FirstOrDefault();
        WeatherText = Describe(stored);
        if (day == DateOnly.FromDateTime(DateTime.Today))
        {
            var error = await Weather.RefreshAsync(day, 14, interactive: true);
            stored = (await Store.GetWeatherAsync(day, day)).FirstOrDefault();
            WeatherText = stored != null ? Describe(stored) : error ?? Describe(null);
        }
    }

    public static string Describe(WeatherDay? day)
    {
        if (day?.PressureMeanHpa == null) return "No pressure data yet.";
        var text = $"Pressure {day.PressureMeanHpa:0} hPa";
        if (day.PressureChangeHpa is { } change)
            text += change switch
            {
                <= -0.5 => $", down {-change:0.#} since yesterday",
                >= 0.5 => $", up {change:0.#} since yesterday",
                _ => ", steady"
            };
        if (day.PressureMaxDrop6hHpa is >= 3) text += $". Fast drop: {day.PressureMaxDrop6hHpa:0.#} hPa in 6 h";
        if (day.TempMaxF.HasValue) text += $". {day.TempMinF:0}–{day.TempMaxF:0}°F";
        return text + ".";
    }

    [RelayCommand]
    void AllSupplements()
    {
        Riboflavin = BComplex = CoQ10 = Magnesium = true;
    }

    [RelayCommand]
    void CaffeineNow()
    {
        CaffeineCups += 1;
        HasLastCaffeine = true;
        LastCaffeineTime = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
    }

    [RelayCommand]
    void MealNow()
    {
        HasFirstMeal = true;
        FirstMealTime = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
    }

    [RelayCommand]
    Task SaveAsync() => UiActions.RunAsync(Errors, "Saving check-in", async () =>
    {
        var checkIn = new DailyCheckIn
        {
            Date = DailyCheckIn.Key(DateOnly.FromDateTime(Date)),
            CaffeineCups = (int)CaffeineCups,
            LastCaffeineTime = HasLastCaffeine ? LastCaffeineTime : null,
            WaterGlasses = (int)WaterGlasses,
            FirstMealTime = HasFirstMeal ? FirstMealTime : null,
            AlcoholDrinks = (int)AlcoholDrinks,
            Stress = StressIndex > 0 ? StressIndex : null,
            Riboflavin = Riboflavin,
            BComplex = BComplex,
            CoQ10 = CoQ10,
            Magnesium = Magnesium,
            ProteinGrams = TrackerRules.ParseInt(ProteinGrams, "Protein"),
            MobilityDone = MobilityDone,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        };
        await Store.SaveCheckInAsync(checkIn);
        SavedText = $"Saved {DateTime.Now:h:mm tt}";
        _ = Exporter.ExportIfDueAsync();
    });
}
