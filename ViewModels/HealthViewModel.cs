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
    public ErrorLog Errors;

    public HealthViewModel(IHealthDataService healthData, IExportDestination destination,
        HealthExportService exporter, ErrorLog errors)
    {
        HealthData = healthData;
        Destination = destination;
        Exporter = exporter;
        Errors = errors;
    }

    [ObservableProperty] public partial string ConnectionStatus { get; set; } = "";
    [ObservableProperty] public partial bool CanConnect { get; set; }
    [ObservableProperty] public partial string ExportStatus { get; set; } = "";
    [ObservableProperty] public partial string DriveText { get; set; } = "";
    [ObservableProperty] public partial bool IsSignedIn { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string ErrorText { get; set; } = "";
    [ObservableProperty] public partial bool HasErrors { get; set; }
    [ObservableProperty] public partial ObservableCollection<DailyHealthSummary> Days { get; set; } = [];

    public bool IsSignedOut => !IsSignedIn;
    partial void OnIsSignedInChanged(bool value) => OnPropertyChanged(nameof(IsSignedOut));

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            RefreshExportStatus();
            await RefreshConnectionAsync();
            if (!CanConnect && HealthData.GetAvailability() == HealthDataAvailability.Available)
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var days = await HealthData.GetDailySummariesAsync(today.AddDays(-13), today);
                Days = new ObservableCollection<DailyHealthSummary>(days.OrderByDescending(d => d.Date));
            }
        }
        catch (Exception exception)
        {
            Errors.Record("Loading health data", exception);
            ConnectionStatus = $"Couldn't read Health Connect: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshErrors();
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
            ? "Connected. Pixel Watch data arrives once the Fitbit app syncs it to Health Connect."
            : "Not connected yet. Tap Connect and allow all the data types, including \"access data in the background\" for the nightly export.";
    }

    void RefreshExportStatus()
    {
        IsSignedIn = Destination.HasDestination;
        DriveText = IsSignedIn
            ? $"Exports to {Destination.DestinationName}"
            : $"Sign in with Google to export to {DriveExportDestination.DrivePath}.";
        var last = Exporter.LastExportUtc;
        var next = NightlySchedule.NextRun(DateTime.Now, NightlySchedule.ExportTime);
        ExportStatus = (Exporter.LastError is { } error
                ? $"Last export failed: {error}"
                : last.HasValue ? $"Last exported {last.Value.ToLocalTime():ddd MMM d, h:mm tt}." : "Not exported yet.")
            + $" Next nightly export: {next:ddd h:mm tt}. It also updates when you open the app and after each migraine change.";
    }

    void RefreshErrors()
    {
        var entries = Errors.Entries;
        HasErrors = entries.Count > 0;
        ErrorText = string.Join("\n\n", entries.Take(3));
    }

    async Task RunAsync(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // User backed out of a system screen.
        }
        catch (Exception exception)
        {
            Errors.Record(what, exception);
            await Shell.Current.DisplayAlertAsync($"{what} failed", exception.Message, "OK");
        }
        finally
        {
            RefreshExportStatus();
            RefreshErrors();
        }
    }

    [RelayCommand]
    Task ConnectAsync() => RunAsync("Connecting Health Connect", async () =>
    {
        await HealthData.RequestPermissionsAsync();
        await LoadAsync();
        _ = Exporter.ExportIfDueAsync(force: true);
    });

    [RelayCommand]
    Task SignInAsync() => RunAsync("Google sign-in", async () =>
    {
        IsBusy = true;
        try
        {
            if (await Destination.ChooseDestinationAsync(HealthExportService.FileName))
            {
                var error = await Exporter.ExportNowAsync();
                if (error != null)
                    await Shell.Current.DisplayAlertAsync("Export failed", error, "OK");
            }
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    Task SignOutAsync() => RunAsync("Signing out", () =>
    {
        Destination.Disconnect();
        return Task.CompletedTask;
    });

    [RelayCommand]
    Task ExportNowAsync() => RunAsync("Export", async () =>
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
        }
    });

    [RelayCommand]
    Task ShareExportAsync() => RunAsync("Sharing export", async () =>
    {
        var json = await Exporter.BuildJsonAsync(DateOnly.FromDateTime(DateTime.Today));
        var path = Path.Combine(FileSystem.CacheDirectory, HealthExportService.FileName);
        await File.WriteAllTextAsync(path, json);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "HealthPulse export",
            File = new ShareFile(path)
        });
    });

    [RelayCommand]
    void ClearErrors()
    {
        Errors.Clear();
        RefreshErrors();
    }
}
