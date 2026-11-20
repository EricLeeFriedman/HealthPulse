---
title: HealthPulse Privacy Policy
---

# HealthPulse Privacy Policy

_Last updated: November 17, 2026 (check-ins, workouts, measurements, weather)_

HealthPulse ("the app") is a personal, open-source Android app for logging migraines and reviewing them next to health data. This policy explains what data the app uses and where it goes.

## Summary

- Your data stays on your phone and in **your own** Google Drive.
- The app has no servers, no accounts of its own, no analytics, no ads, and no third-party SDKs that collect data.
- The developer never receives your data.

## Data the app uses

**Migraine log you enter.** Start and end times, pain level, symptoms, medications (such as triptans), possible triggers, and notes. These are stored in a database on your phone.

**Daily check-in, workouts and measurements you enter.** Caffeine, water, meal times, free-text food entries, alcohol, stress, supplements, protein estimates, mobility, workouts (sets, reps, weights), waist, weight and fitness test results. These are stored on your phone and included in your Drive export.

**Meal photos.** If you attach a photo to a food log entry, it is kept in the app's private storage on your phone and copied to the folder `HealthPulse/meal-photos` in your own Google Drive with the next export, so that tools you choose to read the export can see what you ate. Deleting the entry removes the phone's copy; a copy already in Drive stays until you delete it there.

**Progress photos.** If you add progress photos, they are copied into the app's private storage on your phone. They are never uploaded or included in the export; the export only says how many photos were taken on a date. Taking a photo in the app uses the camera permission.

**Approximate location and weather.** With your permission, the app reads your approximate (city-level) location, rounded to about 1 km, to look up barometric pressure, temperature and humidity from [Open-Meteo](https://open-meteo.com), a free weather service. Only the rounded coordinates are sent to Open-Meteo; no account or identifier is sent. The rounded coordinates are kept on your phone so the nightly export can update the weather. The daily weather summaries are stored on your phone and included in your export.

**Health Connect data (read only).** With your permission, the app reads these types from Android Health Connect: sleep sessions, steps, heart rate, resting heart rate, heart rate variability, exercise sessions, distance, and weight. If you grant background access, it also reads them for the nightly export. The app never writes to or deletes from Health Connect. You can change or revoke these permissions at any time in Health Connect settings.

**Google account and Google Drive.** If you choose **Sign in with Google**, the app requests only the `drive.file` permission. That lets it create and update files that it made itself, and it cannot see any other files in your Drive. It uses this to create the folder `HealthPulse` in My Drive, write `healthpulse-export.json` and copy any meal photos into `HealthPulse/meal-photos`. The export contains your migraine log, daily health summaries for the last 90 days, and recent app error messages (for troubleshooting). It also reads your Google account email address only to show which account is connected.

## How data is used

The data is used only to show you summaries in the app and to write the export file to your Drive. Anything you or tools you choose do with that file afterward is up to you.

## Sharing

The app does not sell, share, or transfer your data to anyone. Data leaves your phone only when it is sent directly to your own Google Drive over an encrypted (HTTPS) connection to Google's API, and when rounded coordinates are sent to Open-Meteo over HTTPS to fetch weather.

The app's use of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements. Health Connect data is used only to provide the app's features to you, and is not used for advertising.

## Retention and deletion

- Delete individual migraine entries in the app. Uninstalling the app deletes all data stored on the phone.
- Tap **Sign out of Google** to stop exports. You can delete the `HealthPulse` folder from your Drive at any time. To remove the app's Drive access entirely, open your Google Account → Security → Third-party apps & services.
- Revoke Health Connect access in Health Connect settings.

## Children

The app is not directed at children under 13.

## Changes

Updates to this policy will be posted on this page with a new "Last updated" date.

## Contact

Open an issue at [github.com/EricLeeFriedman/HealthPulse/issues](https://github.com/EricLeeFriedman/HealthPulse/issues).
