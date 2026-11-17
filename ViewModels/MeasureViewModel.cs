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
        var when = Date.Date + DateTime.Now.TimeOfDay;
        var waist = TrackerRules.ParseDouble(Waist, "Waist");
        var weight = TrackerRules.ParseDouble(Weight, "Weight");
        var toe = TrackerRules.ParseDouble(ToeTouch, "Toe touch");
        var pushUps = TrackerRules.ParseInt(PushUps, "Push-ups");
        var plank = TrackerRules.ParseInt(Plank, "Plank");
        var notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        var hasBody = waist.HasValue || weight.HasValue || Photos.Count > 0;
        var hasTest = toe.HasValue || pushUps.HasValue || plank.HasValue;
        if (!hasBody && !hasTest) throw new ArgumentException("Enter a waist, weight, photo or test result.");

        if (hasBody)
        {
            var measurement = new BodyMeasurement { Date = when, WaistInches = waist, WeightLbs = weight, Notes = notes };
            measurement.PhotoList = [.. Photos];
            await Store.SaveMeasurementAsync(measurement);
        }
        if (hasTest)
        {
            await Store.SaveFitnessTestAsync(new FitnessTest
            {
                Date = when, ToeTouchInches = toe, PushUps = pushUps, PlankSeconds = plank, Notes = hasBody ? null : notes
            });
        }
        _ = Exporter.ExportIfDueAsync();
        await Shell.Current.GoToAsync("..");
    });
}
