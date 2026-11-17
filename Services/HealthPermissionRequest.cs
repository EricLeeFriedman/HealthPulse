namespace HealthPulse.Services;

/// <summary>
/// Pure logic for asking Android for Health Connect read permissions.
/// On Android 14+ Health Connect is part of the OS and its permissions are
/// ordinary runtime permissions, so they must be requested with
/// Activity.requestPermissions. Starting the androidx contract's intent by
/// hand fails there with "No Activity found to handle Intent
/// { act=androidx.activity.result.contract.action.REQUEST_PERMISSIONS }".
/// On Android 13 and below the contract's intent opens the Health Connect app.
/// </summary>
public static class HealthPermissionRequest
{
    /// <summary>Android 14 (API 34) moved Health Connect into the platform.</summary>
    public const int PlatformPermissionsMinSdk = 34;

    /// <summary>Android's PackageManager.PERMISSION_GRANTED.</summary>
    public const int PermissionGranted = 0;

    public enum Route
    {
        /// <summary>Activity.requestPermissions + OnRequestPermissionsResult.</summary>
        PlatformRuntimePermissions,
        /// <summary>Start the Health Connect app's permission screen for a result.</summary>
        HealthConnectAppIntent,
    }

    public static Route ChooseRoute(int sdkInt) =>
        sdkInt >= PlatformPermissionsMinSdk ? Route.PlatformRuntimePermissions : Route.HealthConnectAppIntent;

    /// <summary>Parses an OnRequestPermissionsResult callback into the granted set.</summary>
    public static HashSet<string> GrantedFromResults(IReadOnlyList<string>? permissions, IReadOnlyList<int>? grantResults)
    {
        var granted = new HashSet<string>();
        if (permissions == null || grantResults == null) return granted;
        var count = Math.Min(permissions.Count, grantResults.Count);
        for (var i = 0; i < count; i++)
        {
            if (grantResults[i] == PermissionGranted && !string.IsNullOrEmpty(permissions[i]))
                granted.Add(permissions[i]);
        }
        return granted;
    }
}
