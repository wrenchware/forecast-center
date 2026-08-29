# Project notes

## Decisions

- Chosen stack: C# + WinUI 3 + Windows App SDK, with WebView2 only for the radar map.
- Initial distribution model: unpackaged x64 and Windows App SDK self-contained, because the development machine has the .NET 10 CLI but no Visual Studio installation or WinUI templates.
- Installer payloads must come from `dotnet build -p:SelfContained=true -p:OutDir=...`, not a plain `dotnet publish -o` folder. In this CLI/unpackaged WinUI configuration, `publish` omitted generated `.xbf`/`.pri` resources; a non-self-contained `build` omitted the .NET runtime. Validate both `App.xbf` and `coreclr.dll` before compiling the installer.
- Target framework: `net8.0-windows10.0.19041.0`; Windows App SDK stable 1.8.10 (`1.8.260710003`).
- Providers: Open-Meteo for forecast/geocoding and 15-minute precipitation, NWS for U.S. alerts/narratives, NOAA/NWS MRMS ImageServer/WMS for current radar, NOAA HRRR/NOMADS for six-hour future radar, NOAA NDFD WMS as the future-radar fallback, and a bundled GeoNames cities15000 index for nearby radar labels. Network providers are kept out of the UI layer.
- Default location is New York; manual search does not require location permission.
- Settings live under `%LOCALAPPDATA%/Forecast Center Public/settings.json`.
- Radar map is a local HTML asset mapped to an HTTPS virtual host in WebView2, allowing secure remote tile/API requests.
- Successful forecasts are cached per rounded coordinate/unit system under `%LOCALAPPDATA%/Forecast Center Public/cache`; refresh failures fall back to that snapshot.
- The executable is `ForecastCenter.Public.exe` and uses the `ForecastCenter.Public` AppUserModelID and installer update chain.
- Update checks query stable GitHub releases at most once every 24 hours, cache the result locally, and provide manual checking under Settings > About. The app only opens the release page; it never downloads or installs an update silently.
- WinGet package identifier: `Wrenchware.ForecastCenter`. Version 0.9.0 passed local-manifest validation and a clean Windows Sandbox installation test. Its unsigned installer can still trigger SmartScreen; investigate trusted signing before the next release.
- The app icon is an original transparent Fluent-style cloud/sun/radar mark; `tools/IconBuilder` converts the source PNG into a multi-resolution Windows ICO.
- Appearance supports persistent System, Light, and Dark modes. The dashboard uses a theme-aware neutral base with a restrained condition-colored atmospheric wash shared across the custom title bar and content surface.
- The command-center cards cover near-term precipitation, six-hour temperature movement, daylight, and dew-point-based outdoor comfort.
- A Dark Sky-inspired next-hour panel appears only when 15-minute precipitation is plausible. It uses four 15-minute periods, precipitation-type colors, and collapses without leaving space when dry. Set `FORECASTCENTER_DEMO_PRECIP=1` before launch to exercise its deterministic UI demo.
- The conditional Next Hour presentation is one of the six command cards: it replaces Temperature Trend and takes the top-left position while precipitation is relevant. The normal dry-weather card order returns automatically.
- Weather refreshes at the configured interval (15 minutes by default), radar metadata refreshes every 10 minutes, and activation catches up stale weather after the app has been idle.
- Radar uses theme-aware OpenFreeMap vector basemaps and the user-facing modes Current and Future. Current queries NOAA's time-enabled MRMS ImageServer for exact domain-specific frame times and animates the latest ten WMS frames from roughly the past 90 minutes. Future downloads regional NOAA HRRR composite-reflectivity GRIB2 extracts for forecast hours 1–7, decodes four 15-minute frames per file locally with GribSharp, and normally retains about 22 upcoming frames through six hours. Both feeds use a shared NOAA-style radar-intensity palette. The first HRRR file doubles as the availability probe. Rendered forecast batches are held in an eight-location, 45-minute memory cache; typical HRRR network transfer is roughly 2.5–4.5 MB per location/model refresh. NOAA NDFD remains a graceful fallback.
- Future radar enhancement: evaluate combining MRMS reflectivity with NOAA precipitation-classification data so snow or mixed precipitation can be identified spatially. Do not infer snow solely from reflectivity colors or a location-wide temperature flag.
- NOAA tide stations come from the nationwide CO-OPS metadata catalog, cached locally for 30 days with bundled fallback stations. Tide overrides are per weather location by default, with an optional global override.
- Initial dashboard loading uses a shape-matched skeleton with shimmer. It is capped at roughly 1.1 seconds so slow network providers never hold the interface behind a blocking loading state.
- First-run guidance is scheduled immediately after the dashboard reveal and is independent of weather and WebView2 completion. Dashboard radar initializes in the background and replaces its loading message after 12 seconds if WebView2 or navigation has not completed.
- Radar startup stages and failures are recorded in `%LOCALAPPDATA%/Forecast Center Public/radar-startup.log`. The installer checks Microsoft's documented Evergreen Runtime registry keys and runs the bundled official WebView2 bootstrapper only if neither a per-user nor per-machine runtime is present.
- `RestartIfNeededByRun=no` prevents the WebView2 bootstrapper's restart-recommended exit status from producing an unnecessary Setup reboot prompt; runtime failures remain visible through the radar fallback message and startup log.
- Navigation pages use a restrained 750 ms fade/rise entrance. The sidebar intentionally retains immediate collapse behavior because forcing a custom close animation leaves WinUI in an inconsistent compact-layout state.

## Next implementation sequence

1. Complete a clean-machine install, upgrade, launch, and uninstall test.
2. Keep the repository URL current in the NWS contact-style User-Agent.
3. Decide whether to code-sign the first broadly distributed installer.
4. Add optional MSIX/winget packaging after the Inno Setup release is proven stable.

## Operational notes

- Network requests use `ForecastCenter.Public/0.8 (+https://github.com/wrenchware/forecast-center)` so NOAA/NWS operators can identify the client and reach the project owner.
- GribSharp's CSJ2K dependency targets NETStandard.Library 1.6.1. Direct pins to System.Net.Http 4.3.4 and System.Text.RegularExpressions 4.3.1 retain compatibility while resolving the two high-severity advisories attached to its original 4.3.0 assets.
- Do not poll NWS alerts more frequently than 30 seconds.
- Preserve visible NOAA/NWS, Open-Meteo, OpenStreetMap, OpenFreeMap, and GeoNames attribution.
- Local 0.7.0-to-0.8.0 installer lifecycle testing passed on 2026-08-28,
  including launch, WebView2 data isolation, settings preservation,
  uninstall registry cleanup, and zero installed files remaining.
  A Windows Sandbox clean-machine attempt was blocked by a host Remote Desktop
  session error and must be repeated on another Windows 11 environment.
