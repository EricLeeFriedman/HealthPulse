using Android.App;
using Android.Content;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Lets services start an activity for a result and await it. MainActivity
/// forwards OnActivityResult here.
/// </summary>
public static class ActivityResultBridge
{
    static int nextRequestCode = 4100;
    static readonly Dictionary<int, TaskCompletionSource<(Result ResultCode, Intent? Data)>> Pending = [];

    public static Task<(Result ResultCode, Intent? Data)> StartAsync(Intent intent)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("No foreground activity.");
        var requestCode = Interlocked.Increment(ref nextRequestCode);
        var completion = new TaskCompletionSource<(Result, Intent?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Pending) Pending[requestCode] = completion;
#pragma warning disable CS0618, CA1422 // startActivityForResult is deprecated but still supported.
        activity.StartActivityForResult(intent, requestCode);
#pragma warning restore CS0618, CA1422
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
}
