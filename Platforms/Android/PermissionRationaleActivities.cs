using Android.App;
using Android.OS;
using Android.Widget;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Health Connect requires the app to explain why it reads health data. On
/// Android 13 and below it opens the ACTION_SHOW_PERMISSIONS_RATIONALE screen;
/// on Android 14+ the VIEW_PERMISSION_USAGE screen. Without these the
/// permission request is refused.
/// </summary>
public abstract class PrivacyExplanationActivity : Activity
{
    public const string Explanation =
        "HealthPulse reads your sleep, steps, heart rate, resting heart rate, heart rate variability, " +
        "exercise sessions, and weight from Health Connect.\n\n" +
        "It uses them only to show you daily summaries next to your migraine log, and to write an export " +
        "file to your own Google Drive (My Drive/HealthPulse). Nothing is sent anywhere else.";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var padding = (int)(24 * (Resources?.DisplayMetrics?.Density ?? 1));
        var text = new TextView(this) { Text = Explanation, TextSize = 17 };
        text.SetPadding(padding, padding, padding, padding);
        var scroll = new ScrollView(this);
        scroll.AddView(text);
        SetContentView(scroll);
    }
}

[Activity(Name = "com.ericfriedman.healthpulse.PermissionsRationaleActivity", Exported = true,
    Theme = "@android:style/Theme.Material.Light.Dialog")]
[IntentFilter(["androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE"])]
public class PermissionsRationaleActivity : PrivacyExplanationActivity
{
}

[Activity(Name = "com.ericfriedman.healthpulse.ViewPermissionUsageActivity", Exported = true,
    Permission = "android.permission.START_VIEW_PERMISSION_USAGE",
    Theme = "@android:style/Theme.Material.Light.Dialog")]
[IntentFilter(["android.intent.action.VIEW_PERMISSION_USAGE"], Categories = ["android.intent.category.HEALTH_PERMISSIONS"])]
public class ViewPermissionUsageActivity : PrivacyExplanationActivity
{
}
