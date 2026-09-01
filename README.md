# Forecast Center

Forecast Center is an open-source, ad-free Windows weather dashboard built
with C#, .NET, WinUI 3, and the Windows App SDK.

Forecast Center is a weather app for Windows 11. I built it because I wanted a clean desktop forecast with a useful radar and none of the news, ads, accounts, or subscription prompts found in many weather apps.

It is written in C# with WinUI 3. The radar uses Leaflet inside WebView2.

## Features

- Current conditions, hourly forecast, and 10-day forecast
- Animated current radar and NOAA HRRR future radar through roughly six hours
- Severe-weather alerts from the National Weather Service
- Saved locations and per-location tide settings
- Air quality, UV, outdoor comfort, and NOAA tide information
- Light, dark, and system themes
- Optional minimize-to-tray with the current temperature in the tooltip
- Local settings and last-known weather cache

There are no accounts, ads, analytics, tracking SDKs, or backend services.

## Install

The easiest installation method is WinGet:

```powershell
winget install --id Wrenchware.ForecastCenter --exact
```

To upgrade later:

```powershell
winget upgrade --id Wrenchware.ForecastCenter --exact
```

The same installer can be downloaded manually from [GitHub Releases](https://github.com/wrenchware/forecast-center/releases). Installing a newer version preserves settings and saved locations.

Forecast Center checks once a day for a newer stable release. Update checks can also be run manually from Settings > About; downloads open the corresponding GitHub release page.

A copy of the accepted WinGet community manifest is maintained under `winget/`. Current unsigned installers may still trigger a Windows SmartScreen confirmation.

This is currently an x64, per-user installer. Windows may show a SmartScreen warning because the installer is not code-signed.

## Weather data

Forecast Center does not require API keys.

The hosted Open-Meteo free API is intended for non-commercial use and requires
attribution. See [Privacy](PRIVACY.md) and
[Third-party notices](THIRD_PARTY_NOTICES.md) before distributing a build.

- [Open-Meteo](https://open-meteo.com/en/docs) — current conditions, forecasts, air quality, and location search
- [National Weather Service](https://www.weather.gov/documentation/services-web-api) — U.S. alerts and forecast text
- [NOAA/NWS MRMS](https://mapservices.weather.noaa.gov/eventdriven/rest/services/radar/radar_base_reflectivity_time/ImageServer) — time-enabled observed radar
- [NOAA HRRR via NOMADS](https://nomads.ncep.noaa.gov/) — short-range future radar
- [NOAA/NWS Digital Forecast Database](https://digital.weather.gov/) — fallback forecast-weather overlay
- [NOAA Tides & Currents](https://tidesandcurrents.noaa.gov/) — tide stations and predictions
- [GeoNames](https://www.geonames.org/) — nearby city labels on the radar
- [OpenFreeMap](https://openfreemap.org/) and [OpenStreetMap](https://www.openstreetmap.org/copyright) — vector map tiles and map data

Provider attribution is also shown inside the app. Network services remain best-effort and their current terms should be reviewed before any commercial distribution.

### How future radar works

Forecast Center does not download the nationwide HRRR model. It asks NOAA NOMADS for seven small composite-reflectivity extracts surrounding the selected location. Each extract contains four 15-minute frames. The app decodes and colorizes the GRIB2 data locally, normally producing about 22 usable frames for the next five to six hours.

Current radar uses exact recent frame times from NOAA's MRMS image catalog and loads time-specific WMS tiles on demand. NOAA retains a rolling multi-hour window and updates the service approximately every five minutes; Forecast Center displays the latest ten frames from roughly the past 90 minutes. A typical seven-file HRRR batch is roughly 2.5–4.5 MB, depending on the selected region and weather complexity. Rendered future frames stay in memory rather than accumulating on disk. Results are cached per location for 45 minutes, up to the eight most recently used locations; the first downloaded forecast file also serves as the model-availability check to avoid a duplicate request. If HRRR is temporarily unavailable, the app retains the last successful frames and can fall back to NOAA's forecast-weather layer.

## Building from source

You will need Windows, the .NET 8 SDK or newer, and the WebView2 Runtime. Windows 11 normally includes WebView2.

```powershell
dotnet restore .\ForecastCenter.slnx
dotnet build .\src\ForecastCenter\ForecastCenter.csproj -c Debug
dotnet run --project .\src\ForecastCenter\ForecastCenter.csproj -c Debug -p:Platform=x64
```

Visual Studio 2022 with the Windows application development workload can also open the project.

The code is organized around models, view models, and replaceable provider services. Network and cache logic live outside the UI layer. The radar page is the main exception to the otherwise native UI and is kept in `Assets/radar.html`.

## Building the installer

[Inno Setup 6](https://jrsoftware.org/isinfo.php) is used for the installer.
The installer includes Microsoft's Evergreen WebView2 bootstrapper and invokes
it only when the required runtime is missing.

```powershell
$releasePath = Join-Path $PWD 'release\Forecast Center Public'
dotnet build .\src\ForecastCenter\ForecastCenter.csproj -c Release -p:Platform=x64 -p:SelfContained=true "-p:OutDir=$releasePath"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\CollectThirdPartyLicenses.ps1 -AssetsFile .\src\ForecastCenter\obj\project.assets.json -OutputDirectory "$releasePath\ThirdPartyLicenses"
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" .\installer\ForecastCenter.iss
```

Use the self-contained `dotnet build` layout shown above. A plain `dotnet publish` does not produce all of the WinUI resource files needed by this unpackaged application.

## Current limitations

- Radar and live weather providers need an internet connection.
- The current installer is not code-signed and there is no MSIX package yet.

See [CHANGELOG.md](CHANGELOG.md) for release history.
