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
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            if (ActivityResultBridge.TryComplete(requestCode, resultCode, data))
                return;
            base.OnActivityResult(requestCode, resultCode, data);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (ActivityResultBridge.TryCompletePermissions(requestCode, permissions, grantResults.Select(g => (int)g).ToArray()))
                return;
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}
