using Android.App;
using Android.Content;
using Android.Content.PM;
using HealthPulse.Platforms.Android;

namespace HealthPulse
{
    [Activity(
        Name = "com.ericfriedman.healthpulse.MainActivity",
        Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, Exported = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    // Health Connect opens this to show why the app wants health data (Android 13 and below).
    [IntentFilter(["androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE"])]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            if (ActivityResultBridge.TryComplete(requestCode, resultCode, data))
                return;
            base.OnActivityResult(requestCode, resultCode, data);
        }
    }
}
