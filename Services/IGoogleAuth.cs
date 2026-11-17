namespace HealthPulse.Services;

/// <summary>Gets OAuth access tokens for the Google Drive "files this app creates" scope.</summary>
public interface IGoogleAuth
{
    /// <summary>
    /// Returns an access token. When <paramref name="interactive"/> is false
    /// (background export) and the user must approve again, throws
    /// <see cref="GoogleAuthRequiredException"/>.
    /// </summary>
    Task<string> GetAccessTokenAsync(bool interactive);

    /// <summary>Drops a token Google rejected so the next call fetches a fresh one.</summary>
    Task InvalidateAsync(string accessToken);
}

public class GoogleAuthRequiredException(string message) : Exception(message);

public class UnsupportedGoogleAuth : IGoogleAuth
{
    public Task<string> GetAccessTokenAsync(bool interactive) =>
        throw new PlatformNotSupportedException("Google sign-in is only available on Android.");
    public Task InvalidateAsync(string accessToken) => Task.CompletedTask;
}
