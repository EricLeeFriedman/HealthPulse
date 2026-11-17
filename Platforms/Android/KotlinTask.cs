using Kotlin.Coroutines;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Bridges Kotlin suspend functions (exposed to C# as methods that take an
/// IContinuation) to .NET tasks.
/// </summary>
public static class KotlinTask
{
    public static async Task<Java.Lang.Object?> AwaitAsync(Func<IContinuation, Java.Lang.Object?> call)
    {
        var continuation = new TaskContinuation();
        var immediate = call(continuation);
        if (IsSuspended(immediate))
            return await continuation.Task;
        return Unwrap(immediate);
    }

    static bool IsSuspended(Java.Lang.Object? value) =>
        value != null
        && value.Class.Name == "kotlin.coroutines.intrinsics.CoroutineSingletons"
        && value.ToString() == "COROUTINE_SUSPENDED";

    static Java.Lang.Object? Unwrap(Java.Lang.Object? value)
    {
        // Kotlin delivers failures as an internal kotlin.Result$Failure wrapper.
        if (value != null && value.Class.Name == "kotlin.Result$Failure")
        {
            var field = value.Class.GetDeclaredField("exception");
            field.Accessible = true;
            var throwable = field.Get(value) as Java.Lang.Throwable;
            throw throwable ?? new InvalidOperationException("Health Connect call failed.");
        }
        return value;
    }

    sealed class TaskContinuation : Java.Lang.Object, IContinuation
    {
        readonly TaskCompletionSource<Java.Lang.Object?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Java.Lang.Object?> Task => completion.Task;

        public ICoroutineContext Context => EmptyCoroutineContext.Instance;

        public void ResumeWith(Java.Lang.Object result)
        {
            try
            {
                completion.TrySetResult(Unwrap(result));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }
    }
}
