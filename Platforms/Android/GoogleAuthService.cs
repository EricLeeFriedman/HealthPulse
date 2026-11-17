using Android.App;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using HealthPulse.Services;

namespace HealthPulse.Platforms.Android;

/// <summary>
/// Google sign-in for Drive using Google Identity Services. The app is matched
/// to its Google Cloud OAuth client by package name + signing certificate, so
/// no client ID or secret lives in the code.
/// </summary>
public class GoogleAuthService : IGoogleAuth
{
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";

    static AuthorizationRequest Request => AuthorizationRequest.InvokeBuilder()
        .SetRequestedScopes([new Scope(DriveFileScope)])
        .Build();

    public async Task<string> GetAccessTokenAsync(bool interactive)
    {
        var activity = interactive ? Microsoft.Maui.ApplicationModel.Platform.CurrentActivity : null;
        var client = activity != null
            ? Identity.GetAuthorizationClient(activity)
            : Identity.GetAuthorizationClient(global::Android.App.Application.Context);

        AuthorizationResult result;
        try
        {
            result = await client.Authorize(Request).AsAsync<AuthorizationResult>();
        }
        catch (ApiException exception) when (exception.StatusCode == 10)
        {
            throw new InvalidOperationException(
                "Google sign-in isn't set up for this app yet (DEVELOPER_ERROR). The Google Cloud OAuth client " +
                "needs package com.ericfriedman.healthpulse and this app's SHA-1 signing fingerprint.", exception);
        }

        if (result.HasResolution)
        {
            if (!interactive || result.PendingIntent == null)
                throw new GoogleAuthRequiredException("Google Drive access needs to be approved again. Open HealthPulse and tap Sign in with Google.");

            var (resultCode, data) = await ActivityResultBridge.StartIntentSenderAsync(result.PendingIntent.IntentSender);
            if (resultCode != Result.Ok)
                throw new OperationCanceledException("Google sign-in was cancelled.");
            result = client.GetAuthorizationResultFromIntent(data);
        }

        return result.AccessToken
            ?? throw new InvalidOperationException("Google didn't return a Drive access token.");
    }

    public async Task InvalidateAsync(string accessToken)
    {
        var client = Identity.GetAuthorizationClient(global::Android.App.Application.Context);
        var request = ClearTokenRequest.InvokeBuilder().SetToken(accessToken).Build();
        await client.ClearToken(request).AsAsync();
    }
}
