# HealthPulse

A personal Android app (.NET MAUI) that:

1. **Logs migraines**: start/end, peak pain 0–10, aura and symptoms, triptan taken plus time and 2-hour relief, other meds, possible triggers, notes. A one-tap "Took triptan now" / "It's over" card covers attacks in progress.
2. **Reads Android Health Connect**: sleep, steps, heart rate (avg/min/max), resting heart rate, HRV (RMSSD), exercise sessions, weight. Pixel Watch data comes in through the Fitbit app's Health Connect sync.
3. **Exports to a Google Drive file**: you pick the file once with the system picker (choose Drive), and the app rewrites it with the last 90 days of data when you open it (at most every 3 hours), after each migraine change, or when you tap "Export now".

No health data is stored in this repository. Everything stays on the phone and in your Drive file.

## Install

Each push to `main` builds an APK in GitHub Actions and publishes it as a [release](../../releases/latest).

1. On your phone, open the latest release and download `HealthPulse-1.0.N.apk`.
2. Open it and allow "install unknown apps" for your browser when asked.
3. Open HealthPulse → **Health** tab → **Connect** and allow all data types.
4. In the Fitbit app, turn on Health Connect sync (Profile/You → Settings → Health Connect) so Pixel Watch data flows in.
5. **Health** tab → **Choose export file** → pick **Drive** in the picker → save `healthpulse-export.json`.

Updates install over the old version as long as the `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASSWORD` Actions secrets stay the same.

## Export format

`healthpulse-export.json` has a `days` array (one row per date: sleep, steps, heart rate, HRV, exercise, weight, whether a migraine happened and its peak pain) and a `migraines` array with every logged attack. Times are local.

## Development

- App: `HealthPulse.csproj` (net10.0-android, min Android 8.0).
- Tests: `dotnet test tests/HealthPulse.Tests` runs the platform-neutral code (migraine storage, export) without MAUI.
- Health Connect code lives in `Platforms/Android/`.
