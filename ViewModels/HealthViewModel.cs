using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthPulse.Models;
using HealthPulse.Services;

namespace HealthPulse.ViewModels;

public partial class HealthViewModel : ObservableObject
{
    public IHealthDataService HealthData;
    public IExportDestination Destination;
    public HealthExportService Exporter;

    public HealthViewModel(IHealthDataService healthData, IExportDestination destination, HealthExportService exporter)
    {
        HealthData = healthData;
        Destination = destination;
        Exporter = exporter;
    }

    [ObservableProperty] public partial string ConnectionStatus { get; set; } = "";
    [ObservableProperty] public partial bool CanConnect { get; set; }
    [ObservableProperty] public partial string ExportStatus { get; set; } = "";
    [ObservableProperty] public partial string ExportFileText { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial ObservableCollection<DailyHealthSummary> Days { get; set; } = [];

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await RefreshConnectionAsync();
            RefreshExportStatus();
            if (!CanConnect && HealthData.GetAvailability() == HealthDataAvailability.Available)
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var days = await HealthData.GetDailySummariesAsync(today.AddDays(-13), today);
                Days = new ObservableCollection<DailyHealthSummary>(days.OrderByDescending(d => d.Date));
            }
        }
        catch (Exception exception)
        {
            ConnectionStatus = $"Couldn't read Health Connect: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task RefreshConnectionAsync()
    {
        switch (HealthData.GetAvailability())
        {
            case HealthDataAvailability.NotSupported:
                ConnectionStatus = "Health Connect isn't available on this device.";
                CanConnect = false;
                return;
            case HealthDataAvailability.NeedsInstallOrUpdate:
                ConnectionStatus = "Health Connect needs to be installed or updated from the Play Store.";
                CanConnect = false;
                return;
        }

        var granted = await HealthData.HasPermissionsAsync();
        CanConnect = !granted;
        ConnectionStatus = granted
            ? "Connected to Health Connect. Fitbit / Pixel Watch / Google Fit data shows up here once it syncs."
            : "Not connected yet. Tap Connect and allow all the health data types.";
    }

    void RefreshExportStatus()
    {
        ExportFileText = Destination.HasDestination
            ? $"Export file: {Destination.DestinationName ?? HealthExportService.FileName}"
            : "No export file chosen yet. Choose one and pick Drive in the file picker.";
        var last = Exporter.LastExportUtc;
        ExportStatus = Exporter.LastError is { } error
            ? $"Last export failed: {error}"
            : last.HasValue
                ? $"Last exported {last.Value.ToLocalTime():MMM d, h:mm tt}. Updates when you open the app (every 3 h) and after each migraine change."
                : "Not exported yet.";
    }

    [RelayCommand]
    async Task ConnectAsync()
    {
        await HealthData.RequestPermissionsAsync();
        await LoadAsync();
        _ = Exporter.ExportIfDueAsync(force: true);
    }

    [RelayCommand]
    async Task ChooseExportFileAsync()
    {
        if (await Destination.ChooseDestinationAsync(HealthExportService.FileName))
            await ExportNowAsync();
        RefreshExportStatus();
    }

    [RelayCommand]
    async Task ExportNowAsync()
    {
        IsBusy = true;
        try
        {
            var error = await Exporter.ExportNowAsync();
            if (error != null)
                await Shell.Current.DisplayAlertAsync("Export failed", error, "OK");
        }
        finally
        {
            IsBusy = false;
            RefreshExportStatus();
        }
    }

    [RelayCommand]
    async Task ShareExportAsync()
    {
        var json = await Exporter.BuildJsonAsync(DateOnly.FromDateTime(DateTime.Today));
        var path = Path.Combine(FileSystem.CacheDirectory, HealthExportService.FileName);
        await File.WriteAllTextAsync(path, json);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Migraine + health export",
            File = new ShareFile(path)
        });
    }
}
