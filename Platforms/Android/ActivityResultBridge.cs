using Android.App;
using Android.Content;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Lets services start an activity (or intent sender) for a result and await
/// it. MainActivity forwards OnActivityResult here.
/// </summary>
public static class ActivityResultBridge
{
    static int nextRequestCode = 4100;
    static readonly Dictionary<int, TaskCompletionSource<(Result ResultCode, Intent? Data)>> Pending = [];
    static readonly Dictionary<int, TaskCompletionSource<(string[] Permissions, int[] GrantResults)>> PendingPermissions = [];

    static Activity CurrentActivity =>
        Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
        ?? throw new InvalidOperationException("HealthPulse isn't in the foreground.");

    public static Task<(Result ResultCode, Intent? Data)> StartAsync(Intent intent) =>
        Start((activity, code) =>
        {
#pragma warning disable CS0618, CA1422 // startActivityForResult is deprecated but still supported.
            activity.StartActivityForResult(intent, code);
#pragma warning restore CS0618, CA1422
        });

    public static Task<(Result ResultCode, Intent? Data)> StartIntentSenderAsync(IntentSender sender) =>
        Start((activity, code) =>
        {
#pragma warning disable CS0618, CA1422
            activity.StartIntentSenderForResult(sender, code, null, 0, 0, 0);
#pragma warning restore CS0618, CA1422
        });

    static Task<(Result ResultCode, Intent? Data)> Start(Action<Activity, int> launch)
    {
        var activity = CurrentActivity;
        var requestCode = Interlocked.Increment(ref nextRequestCode) & 0xFFFF;
        var completion = new TaskCompletionSource<(Result, Intent?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Pending) Pending[requestCode] = completion;
        try
        {
            launch(activity, requestCode);
        }
        catch
        {
            lock (Pending) Pending.Remove(requestCode);
            throw;
        }
        return completion.Task;
    }

    public static bool TryComplete(int requestCode, Result resultCode, Intent? data)
    {
        TaskCompletionSource<(Result, Intent?)>? completion;
        lock (Pending)
        {
            if (!Pending.Remove(requestCode, out completion)) return false;
        }
        completion.TrySetResult((resultCode, data));
        return true;
    }

    /// <summary>
    /// Shows Android's runtime permission dialog and awaits the answer.
    /// MainActivity forwards OnRequestPermissionsResult to TryCompletePermissions.
    /// </summary>
    public static Task<(string[] Permissions, int[] GrantResults)> RequestPermissionsAsync(string[] permissions)
    {
        var activity = CurrentActivity;
        var requestCode = Interlocked.Increment(ref nextRequestCode) & 0xFFFF;
        var completion = new TaskCompletionSource<(string[], int[])>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (PendingPermissions) PendingPermissions[requestCode] = completion;
        try
        {
            activity.RequestPermissions(permissions, requestCode);
        }
        catch
        {
            lock (PendingPermissions) PendingPermissions.Remove(requestCode);
            throw;
        }
        return completion.Task;
    }

    public static bool TryCompletePermissions(int requestCode, string[] permissions, int[] grantResults)
    {
        TaskCompletionSource<(string[], int[])>? completion;
        lock (PendingPermissions)
        {
            if (!PendingPermissions.Remove(requestCode, out completion)) return false;
        }
        completion.TrySetResult((permissions, grantResults));
        return true;
    }
}
