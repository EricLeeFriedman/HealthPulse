using HealthPulse.Services;

namespace HealthPulse.Tests;

public class HealthPermissionRequestTests
{
    [Theory]
    [InlineData(26, HealthPermissionRequest.Route.HealthConnectAppIntent)]
    [InlineData(33, HealthPermissionRequest.Route.HealthConnectAppIntent)]
    [InlineData(34, HealthPermissionRequest.Route.PlatformRuntimePermissions)]
    [InlineData(35, HealthPermissionRequest.Route.PlatformRuntimePermissions)]
    [InlineData(36, HealthPermissionRequest.Route.PlatformRuntimePermissions)]
    public void Android14AndUp_UsesPlatformRuntimePermissions(int sdk, HealthPermissionRequest.Route expected)
    {
        Assert.Equal(expected, HealthPermissionRequest.ChooseRoute(sdk));
    }

    [Fact]
    public void GrantedFromResults_KeepsOnlyGrantedPermissions()
    {
        string[] perms = ["android.permission.health.READ_STEPS", "android.permission.health.READ_SLEEP", "android.permission.health.READ_WEIGHT"];
        int[] results = [0, -1, 0];

        var granted = HealthPermissionRequest.GrantedFromResults(perms, results);

        Assert.Equal(new HashSet<string> { "android.permission.health.READ_STEPS", "android.permission.health.READ_WEIGHT" }, granted);
    }

    [Fact]
    public void GrantedFromResults_HandlesCancelledAndMismatchedCallbacks()
    {
        // Android reports an empty result when the dialog is interrupted.
        Assert.Empty(HealthPermissionRequest.GrantedFromResults([], []));
        Assert.Empty(HealthPermissionRequest.GrantedFromResults(null, null));
        Assert.Single(HealthPermissionRequest.GrantedFromResults(["a", "b"], [0]));
    }
}
