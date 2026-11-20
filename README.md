# HealthPulse

A personal Android app (.NET MAUI) that:

1. **Logs migraines**: start/end, peak pain 0–10, aura and symptoms, triptan taken plus time and 2-hour relief, other meds, possible triggers, notes. A one-tap "Took triptan now" / "It's over" card covers attacks in progress.
2. **Reads Android Health Connect**: sleep, steps, heart rate (avg/min/max), resting heart rate, HRV (RMSSD), exercise sessions, weight. Pixel Watch data comes in through the Fitbit app's Health Connect sync.
3. **Daily check-in** (Today tab): caffeine cups and time of the last one, water in ounces against an 80 oz target (one-tap Glass 8 oz, ¼, ½ and full 40 oz Stanley buttons, any other amount typed in, Undo; days logged as glasses before still count 8 oz each), first meal time, alcohol, stress 1 to 5, the four migraine supplements, a food log (time, what you ate in your own words, plus optional extras for meals without exact measurements: a rough portion such as small, medium or large bowl, cup, fist, palm, handful or plate; ingredient chips to tick, with your own additions remembered; and a photo, taken or chosen; frequent foods remember their portion and ingredients, and a protein estimate fills the day's protein total), a protein estimate against the 120 to 150 g target, the Sunday mobility checkbox, and a daily **Applied Jublia** checkbox (its own item, separate from the migraine supplements; it's in the export as `jubliaApplied` and in the daily reminders until it's ticked). Shows today's planned session and barometric pressure. **⇄ Alternatives or swap a day** offers backups that still count as the session: treadmill, stationary bike, stairs or a brisk indoor walk on run days (same intervals as the plan), a body-weight or short version on strength days, or a swap with another day of the same week (undoable).
4. **Fitness** tab: workouts recorded on the watch (runs, strength training, stretching or yoga) are imported from Health Connect automatically and matched to the plan's sessions; plain walks are skipped. Sets, reps and weights can optionally be added to any of them. On a run day, indoor cardio from the watch (treadmill, stationary bike, stairs, elliptical, rowing) counts as the run. It logs workouts against the 12-week plan (Mon Strength A, Tue Run, Wed Strength B, Thu Run, Fri Strength C, Sat off, Sun mobility) with weight, reps and effort for each set (reps left in the tank, 0 to 3+). Each exercise shows what to do today using double progression: once every set reaches the top of the week's rep range with 2 or more reps left, it says go up (about 5 lb per dumbbell, 10 lb on the goblet squat once it's 30 lb or more) and pre-fills the new weight; two sessions short of the range drops a step, body-weight moves add reps and planks add seconds. A **Lifting progress** card lists every exercise's weight change, what's next and anything stuck for 3+ sessions. It counts sessions done out of 6 each week, and logs weekly waist and weight, monthly progress photos (kept on the phone only) and the monthly toe-touch, push-up and plank test.
5. **Weather**: hourly sea-level pressure, temperature and humidity from [Open-Meteo](https://open-meteo.com) (free, no key) for the phone's city-level location, summarized per day with the day-over-day change and the biggest 6-hour drop.
6. **Exports to Google Drive**: after you tap **Sign in with Google**, the app writes the last 90 days to `My Drive/HealthPulse/healthpulse-export.json` every night at 11:45 PM, when you open it (at most every 3 hours), after each migraine change, or when you tap "Export now". Meal photos are copied to `My Drive/HealthPulse/meal-photos/` with the export (each one once, resized to at most 1600 px), so tools reading the export can look at them. Recent app errors are included in the export so problems can be diagnosed remotely.

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

`healthpulse-export.json` has a `days` array (one row per date: sleep, steps, heart rate, HRV, exercise, weight, whether a migraine happened and its peak pain, the planned and logged sessions, the `checkIn`, `meals`, `weather` and any waist or weight logged; each meal has its text and, when given, `portion` with its `portionSize`, `ingredients`, and `photo`, the path of its photo under `My Drive/HealthPulse/meal-photos/`), a `weeks` array (sessions done out of 6), and `migraines`, `workouts` (with sets, each with `rir` reps in reserve when rated, and `volumeLbs`), `strength` (per exercise: weight and estimated one-rep max over time, every session's reps and effort, and the `next` recommendation with its reason; `strength.rule` explains the rule), `measurements` (photo counts only) and `fitnessTests` arrays. Times are local.

## Development

- App: `HealthPulse.csproj` (net10.0-android, min Android 8.0).
- Tests: `dotnet test tests/HealthPulse.Tests` runs the platform-neutral code (storage, workout plan, check-in rules, weather parsing, export) without MAUI.
- Health Connect code lives in `Platforms/Android/`.
