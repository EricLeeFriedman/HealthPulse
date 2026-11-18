using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

/// <summary>Waist, weight, progress photos and the monthly fitness test on one screen.</summary>
public partial class MeasureViewModel : ObservableObject
{
    public ITrackerStore Store;
    public HealthExportService Exporter;
    public ErrorLog Errors;

    public MeasureViewModel(ITrackerStore store, HealthExportService exporter, ErrorLog errors)
    {
        Store = store;
        Exporter = exporter;
        Errors = errors;
    }

    [ObservableProperty] public partial DateTime Date { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string Waist { get; set; } = "";
    [ObservableProperty] public partial string Weight { get; set; } = "";
    [ObservableProperty] public partial string ToeTouch { get; set; } = "";
    [ObservableProperty] public partial string PushUps { get; set; } = "";
    [ObservableProperty] public partial string Plank { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial ObservableCollection<string> Photos { get; set; } = [];
    [ObservableProperty] public partial string PhotoText { get; set; } = "No photos added.";
    [ObservableProperty] public partial ObservableCollection<MeasureHistoryItem> History { get; set; } = [];
    [ObservableProperty] public partial string EditingText { get; set; } = "";
    [ObservableProperty] public partial bool IsEditing { get; set; }

    BodyMeasurement? editingMeasurement;
    FitnessTest? editingTest;

    public async Task LoadHistoryAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            History = new ObservableCollection<MeasureHistoryItem>(MeasureHistory.Build(
                await Store.GetMeasurementsAsync(today.AddDays(-120), today),
                await Store.GetFitnessTestsAsync(today.AddDays(-120), today)));
        }
        catch (Exception exception)
        {
            Errors.Record("Loading measurements", exception);
        }
    }

    [RelayCommand]
    Task HistoryTappedAsync(MeasureHistoryItem? item) => UiActions.RunAsync(Errors, "Editing entry", async () =>
    {
        if (item == null) return;
        var choice = await Shell.Current.DisplayActionSheetAsync(item.Display, "Cancel", "Delete", "Edit");
        if (choice == "Delete")
        {
            if (item.Kind == MeasureHistoryItem.Body) await Store.DeleteMeasurementAsync(item.Id);
            else await Store.DeleteFitnessTestAsync(item.Id);
            if (editingMeasurement?.Id == item.Id || editingTest?.Id == item.Id) ClearForm();
            await LoadHistoryAsync();
            _ = Exporter.ExportIfDueAsync(force: true);
        }
        else if (choice == "Edit")
        {
            await StartEditAsync(item);
        }
    });

    async Task StartEditAsync(MeasureHistoryItem item)
    {
        ClearForm();
        var day = DateOnly.FromDateTime(item.Date);
        if (item.Kind == MeasureHistoryItem.Body)
        {
            var m = (await Store.GetMeasurementsAsync(day, day)).FirstOrDefault(x => x.Id == item.Id);
            if (m == null) return;
            editingMeasurement = m;
            Date = m.Date.Date;
            Waist = m.WaistInches?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
            Weight = m.WeightLbs?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
            Photos = new ObservableCollection<string>(m.PhotoList);
            PhotoText = Photos.Count == 0 ? "No photos added." : $"{Photos.Count} photo{(Photos.Count == 1 ? "" : "s")}.";
            Notes = m.Notes ?? "";
        }
        else
        {
            var t = (await Store.GetFitnessTestsAsync(day, day)).FirstOrDefault(x => x.Id == item.Id);
            if (t == null) return;
            editingTest = t;
            Date = t.Date.Date;
            ToeTouch = t.ToeTouchInches?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
            PushUps = t.PushUps?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
            Plank = t.PlankSeconds?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "";
            Notes = t.Notes ?? "";
        }
        EditingText = $"Editing the {item.Date:ddd MMM d} entry. Change it below and tap Save.";
        IsEditing = true;
    }

    [RelayCommand]
    void CancelEdit() => ClearForm();

    void ClearForm()
    {
        editingMeasurement = null;
        editingTest = null;
        IsEditing = false;
        EditingText = "";
        Date = DateTime.Today;
        Waist = Weight = ToeTouch = PushUps = Plank = Notes = "";
        Photos = [];
        PhotoText = "No photos added.";
    }

    public static string PhotoFolder => Path.Combine(FileSystem.AppDataDirectory, "photos");

    [RelayCommand]
    Task TakePhotoAsync() => UiActions.RunAsync(Errors, "Taking photo", async () =>
    {
        if (!MediaPicker.Default.IsCaptureSupported) throw new InvalidOperationException("This phone can't take photos from the app.");
        var photo = await MediaPicker.Default.CapturePhotoAsync();
        if (photo != null) await KeepAsync(photo);
    });

    [RelayCommand]
    Task ChoosePhotoAsync() => UiActions.RunAsync(Errors, "Choosing photo", async () =>
    {
#pragma warning disable CS0618 // PickPhotoAsync is superseded by PickPhotosAsync but still supported.
        var photo = await MediaPicker.Default.PickPhotoAsync();
#pragma warning restore CS0618
        if (photo != null) await KeepAsync(photo);
    });

    /// <summary>Copies the photo into the app's private storage. Photos never leave the phone.</summary>
    async Task KeepAsync(FileResult photo)
    {
        Directory.CreateDirectory(PhotoFolder);
        var extension = Path.GetExtension(photo.FileName);
        if (string.IsNullOrEmpty(extension)) extension = ".jpg";
        var path = Path.Combine(PhotoFolder, $"{Date:yyyyMMdd}-{Guid.NewGuid():N}{extension}");
        await using (var source = await photo.OpenReadAsync())
        await using (var target = File.Create(path))
            await source.CopyToAsync(target);
        Photos.Add(path);
        PhotoText = $"{Photos.Count} photo{(Photos.Count == 1 ? "" : "s")} added. They stay on this phone.";
    }

    [RelayCommand]
    Task SaveAsync() => UiActions.RunAsync(Errors, "Saving measurements", async () =>
    {
        var original = editingMeasurement?.Date ?? editingTest?.Date;
        var when = Date.Date + (original?.TimeOfDay ?? DateTime.Now.TimeOfDay);
        var waist = TrackerRules.ParseDouble(Waist, "Waist");
        var weight = TrackerRules.ParseDouble(Weight, "Weight");
        var toe = TrackerRules.ParseDouble(ToeTouch, "Toe touch");
        var pushUps = TrackerRules.ParseInt(PushUps, "Push-ups");
        var plank = TrackerRules.ParseInt(Plank, "Plank");
        var notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        var hasBody = waist.HasValue || weight.HasValue || Photos.Count > 0;
        var hasTest = toe.HasValue || pushUps.HasValue || plank.HasValue;
        if (!hasBody && !hasTest) throw new ArgumentException("Enter a waist, weight, photo or test result.");
        var editing = IsEditing;

        if (hasBody)
        {
            var measurement = editingMeasurement ?? new BodyMeasurement();
            measurement.Date = when;
            measurement.WaistInches = waist;
            measurement.WeightLbs = weight;
            measurement.Notes = notes;
            measurement.PhotoList = [.. Photos];
            await Store.SaveMeasurementAsync(measurement);
        }
        else if (editingMeasurement != null)
        {
            await Store.DeleteMeasurementAsync(editingMeasurement.Id);
        }
        if (hasTest)
        {
            var test = editingTest ?? new FitnessTest();
            test.Date = when;
            test.ToeTouchInches = toe;
            test.PushUps = pushUps;
            test.PlankSeconds = plank;
            test.Notes = hasBody ? null : notes;
            await Store.SaveFitnessTestAsync(test);
        }
        else if (editingTest != null)
        {
            await Store.DeleteFitnessTestAsync(editingTest.Id);
        }
        _ = Exporter.ExportIfDueAsync(force: editing);
        if (editing)
        {
            ClearForm();
            await LoadHistoryAsync();
            return;
        }
        await Shell.Current.GoToAsync("..");
    });
}
