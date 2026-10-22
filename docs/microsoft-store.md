# Microsoft Store release

The public edition ships to the Microsoft Store as an MSIX package. Microsoft signs
and hosts the package and delivers updates, so no code-signing certificate is needed.
The GitHub installer build is unaffected.

## How the Store build differs

The same code runs in both builds; it detects at runtime whether it is packaged
(`AppIdentity.IsPackaged`). When installed from the Store:

- The in-app GitHub update check and the "App updates" card are hidden. The Store
  updates the app and does not allow prompting users to download updates elsewhere.
- "Start Forecast Center with Windows" uses the manifest's `StartupTask` instead of the
  `Run` registry key (which is virtualized for packaged apps). The checkbox reflects
  changes made in Windows Settings > Apps > Startup.
- Settings and caches live in the package's private storage and are removed on uninstall.
- The process takes its identity from the package rather than setting an AppUserModelID.

## One-time Partner Center setup (done by the developer)

1. Register at <https://storedeveloper.microsoft.com> as an **Individual developer**
   (free). Identity verification uses a government ID and a selfie.
2. In Partner Center, **Apps and games > New product > MSIX or PWA app**, and reserve
   the name **Forecast Center** (or a variant if it is taken).
3. Open **Product management > Product identity** and copy these three values into
   `src/ForecastCenter/Packaging/Package.appxmanifest`, replacing the `PARTNER-CENTER`
   placeholders:
   - `Package/Identity/Name`
   - `Package/Identity/Publisher` (starts with `CN=`)
   - `Package/Properties/PublisherDisplayName`

## Building the upload package

```powershell
dotnet build .\src\ForecastCenter\ForecastCenter.csproj -c Release -p:Platform=x64 -p:StoreBuild=true
```

The upload file is written to `release\store\ForecastCenter_<version>_x64.msixupload`.
Keep `Package.appxmanifest`'s `Identity Version` in step with the app version; the
fourth number must stay `0` for the Store.

## Submission checklist

**Packages:** upload the `.msixupload` file. Device family: Windows 10/11 Desktop.

**Properties**

- Category: Weather
- Privacy policy URL: <https://github.com/wrenchware/forecast-center/blob/main/PRIVACY.md>
- Website: <https://github.com/wrenchware/forecast-center>
- Support contact: <https://github.com/wrenchware/forecast-center/issues>
- Product declarations: the app does not collect personal information beyond what is
  described in the privacy policy; it accesses location only if the user turns on
  Automatic location.
- Restricted capability `runFullTrust` justification: "Forecast Center is a WinUI 3
  desktop app (Windows App SDK). runFullTrust is required for all packaged desktop
  apps of this type."

**Age ratings (IARC questionnaire):** app category "Utility / Productivity / Other";
answer **No** to violence, sexual content, gambling, user-to-user communication,
user-generated content sharing, sharing of the user's location with other users, and
digital purchases. Expected rating: Everyone / 3+.

**Pricing and availability:** Free, all markets.

**Store listing (English)**

- Product name: Forecast Center
- Short description:
  > A fast, ad-free weather dashboard with live radar, forecasts, alerts, and tides.
- Description:
  > Forecast Center is an open-source, ad-free weather app for Windows. It opens in
  > moments with your latest forecast and keeps everything on one clean dashboard:
  > current conditions, hourly and 10-day forecasts, and an animated radar that runs
  > from recent observations into about six hours of NOAA HRRR forecast.
  >
  > U.S. locations also get National Weather Service alerts and forecast narratives,
  > NOAA tide predictions, and air-quality and UV details. There are no accounts,
  > ads, analytics, or tracking.
- Features (one per line):
  - Current conditions, hourly forecast, and 10-day forecast with illustrated artwork
  - Animated current radar and NOAA HRRR future radar up to about six hours
  - National Weather Service severe-weather alerts
  - Air quality, UV index, outdoor comfort, daylight, and moon phase
  - NOAA tide predictions with per-location station choice
  - Saved locations and optional automatic location
  - Light, dark, and system themes, with optional minimize-to-tray
  - No accounts, ads, analytics, or tracking
- Keywords: weather, radar, forecast, alerts, tides
- Screenshots (1920 × 1080): `docs/store/screenshots/` — dashboard dark, dashboard
  light, forecast details, expanded radar.
- Copyright and trademark: © Wrenchware
- Additional license terms: <https://github.com/wrenchware/forecast-center/blob/main/LICENSE>

## Releasing an update

1. Bump the version in `ForecastCenter.csproj` and `Package.appxmanifest`.
2. Build with `-p:StoreBuild=true`.
3. In Partner Center, create a new submission, replace the package, and submit.
