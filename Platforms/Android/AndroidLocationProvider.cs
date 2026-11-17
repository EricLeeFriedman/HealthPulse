using Android.Content.PM;
using Android.Locations;
using AndroidLocation = Android.Locations.Location;
using HealthPulse.Services;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// City-level location from Android's LocationManager, using only
/// ACCESS_COARSE_LOCATION. Used to look up barometric pressure.
/// </summary>
public class AndroidLocationProvider : ILocationProvider
{
    const string CoarseLocation = global::Android.Manifest.Permission.AccessCoarseLocation;

    static global::Android.Content.Context Context => global::Android.App.Application.Context;

    static bool HasPermission() =>
        AndroidX.Core.Content.ContextCompat.CheckSelfPermission(Context, CoarseLocation) == Permission.Granted;

    public async Task<(double Latitude, double Longitude)?> GetCoarseLocationAsync(bool interactive)
    {
        if (!HasPermission())
        {
            if (!interactive || Microsoft.Maui.ApplicationModel.Platform.CurrentActivity == null) return null;
            var (_, results) = await MainThread.InvokeOnMainThreadAsync(() =>
                ActivityResultBridge.RequestPermissionsAsync([CoarseLocation]));
            if (results.Length == 0 || results[0] != (int)Permission.Granted) return null;
        }

        if (Context.GetSystemService(global::Android.Content.Context.LocationService) is not LocationManager manager)
            return null;

        var last = LastKnown(manager);
        if (last != null && (DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(last.Time).UtcDateTime) < TimeSpan.FromHours(12))
            return (last.Latitude, last.Longitude);

        var current = await CurrentAsync(manager);
        if (current != null) return (current.Latitude, current.Longitude);
        return last != null ? (last.Latitude, last.Longitude) : null;
    }

    static AndroidLocation? LastKnown(LocationManager manager)
    {
        AndroidLocation? best = null;
        foreach (var provider in manager.GetProviders(true))
        {
            try
            {
                var location = manager.GetLastKnownLocation(provider);
                if (location != null && (best == null || location.Time > best.Time)) best = location;
            }
            catch (Java.Lang.SecurityException)
            {
                // Provider needs fine location; skip it.
            }
        }
        return best;
    }

    static async Task<AndroidLocation?> CurrentAsync(LocationManager manager)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return null;
        var provider = manager.IsProviderEnabled(LocationManager.NetworkProvider)
            ? LocationManager.NetworkProvider
            : OperatingSystem.IsAndroidVersionAtLeast(31) && manager.IsProviderEnabled(LocationManager.FusedProvider)
                ? LocationManager.FusedProvider
                : null;
        if (provider == null) return null;

        var completion = new TaskCompletionSource<AndroidLocation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new global::Android.OS.CancellationSignal();
        try
        {
            manager.GetCurrentLocation(provider, cancel, Context.MainExecutor!, new LocationConsumer(completion));
        }
        catch (Exception)
        {
            return null;
        }
        var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        if (finished != completion.Task)
        {
            cancel.Cancel();
            return null;
        }
        return await completion.Task;
    }

    sealed class LocationConsumer(TaskCompletionSource<AndroidLocation?> completion) : Java.Lang.Object, Java.Util.Functions.IConsumer
    {
        public void Accept(Java.Lang.Object? value) => completion.TrySetResult(value as AndroidLocation);
    }
}
