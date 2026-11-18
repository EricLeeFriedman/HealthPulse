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
        const string PostNotifications = "android.permission.POST_NOTIFICATIONS";
        const string AskedNotificationsKey = "asked_notifications";
        const int NotificationsRequestCode = 7001;

        protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Android 13+: reminders need notification permission. Ask once; the Today tab explains if it's off.
            if (OperatingSystem.IsAndroidVersionAtLeast(33)
                && CheckSelfPermission(PostNotifications) != Permission.Granted
                && !Microsoft.Maui.Storage.Preferences.Default.Get(AskedNotificationsKey, false))
            {
                Microsoft.Maui.Storage.Preferences.Default.Set(AskedNotificationsKey, true);
                RequestPermissions([PostNotifications], NotificationsRequestCode);
            }
        }

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
