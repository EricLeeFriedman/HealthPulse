using Android.Runtime;
using Kotlin.Coroutines;
using Kotlin.Jvm.Functions;
using Xamarin.KotlinX.Coroutines;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Runs Kotlin suspend functions (exposed to C# as methods that take an
/// IContinuation) and returns their result as a .NET task.
/// </summary>
/// <remarks>
/// Each call runs inside kotlinx runBlocking on its own background thread, so it
/// gets a real coroutine (a Job and an event loop) rather than a bare
/// continuation, and inside withTimeout, so a Health Connect call that never
/// answers is cancelled instead of hanging the app.
/// </remarks>
public static class KotlinTask
{
    /// <summary>How long one Health Connect call may take before it counts as stuck.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(25);

    public static Task<Java.Lang.Object?> AwaitAsync(Func<IContinuation, Java.Lang.Object?> call,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var work = Task.Factory.StartNew(() =>
        {
            var inner = new SuspendBlock((_, continuation) => call(continuation));
            var withTimeout = new SuspendBlock((_, continuation) =>
                TimeoutKt.WithTimeout((long)limit.TotalMilliseconds, inner, continuation));
            try
            {
                return BuildersKt.RunBlocking(EmptyCoroutineContext.Instance, withTimeout);
            }
            catch (Java.Lang.Throwable throwable) when (throwable.Class.Name.EndsWith("TimeoutCancellationException"))
            {
                throw new TimeoutException(
                    $"Health Connect didn't answer within {HealthPulse.Services.Timeouts.Describe(limit)}.");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // Backstop in case the coroutine ignores cancellation.
        return HealthPulse.Services.Timeouts.Limit(work, limit + TimeSpan.FromSeconds(5), "A Health Connect call");
    }

    /// <summary>A Kotlin suspend lambda: (receiver, continuation) → result or COROUTINE_SUSPENDED.</summary>
    sealed class SuspendBlock(Func<Java.Lang.Object?, IContinuation, Java.Lang.Object?> body)
        : Java.Lang.Object, IFunction2
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? receiver, Java.Lang.Object? continuation) =>
            body(receiver, continuation!.JavaCast<IContinuation>());
    }
}
