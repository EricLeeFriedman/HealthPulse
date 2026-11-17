# HealthPulse

A personal Android app (.NET MAUI) that:

1. **Logs migraines**: start/end, peak pain 0–10, aura and symptoms, triptan taken plus time and 2-hour relief, other meds, possible triggers, notes. A one-tap "Took triptan now" / "It's over" card covers attacks in progress.
2. **Reads Android Health Connect**: sleep, steps, heart rate (avg/min/max), resting heart rate, HRV (RMSSD), exercise sessions, weight. Pixel Watch data comes in through the Fitbit app's Health Connect sync.
3. **Exports to Google Drive**: after you tap **Sign in with Google**, the app writes the last 90 days to `My Drive/HealthPulse/healthpulse-export.json` every night at 11:45 PM, when you open it (at most every 3 hours), after each migraine change, or when you tap "Export now". Recent app errors are included in the export so problems can be diagnosed remotely.

No health data is stored in this repository. Everything stays on the phone and in your Drive file.

## Install

Each push to `main` builds an APK in GitHub Actions and publishes it as a [release](../../releases/latest).

1. On your phone, open the latest release and download `HealthPulse-1.0.N.apk`.
2. Open it and allow "install unknown apps" for your browser when asked.
3. Open HealthPulse → **Health** tab → **Connect** and allow all data types.
4. In the Fitbit app, turn on Health Connect sync (Profile/You → Settings → Health Connect) so Pixel Watch data flows in.
5. **Health** tab → **Choose export file** → pick **Drive** in the picker → save `healthpulse-export.json`.

Updates install over the old version as long as the `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASSWORD` Actions secrets stay the same.

## Google Cloud setup (one time)

Google sign-in needs an OAuth client in a Google Cloud project owned by the Drive account:

1. Create a project and enable the **Google Drive API**.
2. OAuth consent screen: External, then **Publish app** (In production) so sign-in doesn't expire every 7 days. Only the non-sensitive `drive.file` scope is used, so no Google review is needed.
3. Create an OAuth client of type **Android** with package `com.ericfriedman.healthpulse` and the SHA-1 of the release signing key.

## Export format

`healthpulse-export.json` has a `days` array (one row per date: sleep, steps, heart rate, HRV, exercise, weight, whether a migraine happened and its peak pain) and a `migraines` array with every logged attack. Times are local.

## Development

- App: `HealthPulse.csproj` (net10.0-android, min Android 8.0).
- Tests: `dotnet test tests/HealthPulse.Tests` runs the platform-neutral code (migraine storage, export) without MAUI.
- Health Connect code lives in `Platforms/Android/`.
