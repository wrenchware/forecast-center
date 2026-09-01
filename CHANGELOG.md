# Changelog

Notable user-facing changes to Forecast Center are recorded here. GitHub Releases contain matching downloadable installer notes.

## 1.0.1 - 2026-09-01

### Added

- Added an optional Ko-fi support card to About. Donations do not unlock features or benefits.

### Improved

- Keep longer hero forecasts compact at three lines and show the complete narrative in the app's standard forecast tooltip only when the text is truncated.
- Use a consistent home-icon Dashboard action across Settings, Radar, and Alerts pages.
- Refined the description on the About page.
- Keep animated hourly and 10-day pages full-width with aligned, rounded pager controls and matching hourly hover feedback.
- Use a cleaner borderless treatment for detailed forecast and trend tooltips.

### Fixed

- Prevent a WinUI layout cycle when paging forecasts and resizing the window.

## 1.0.0 - 2026-08-31

### Added

- A moon-phase card showing today, the next New Moon, and the next Full Moon.
- Click-to-expand dashboard radar with a responsive immersive view.

### Improved

- Reworked the dashboard footer into balanced Tide and Moon cards.
- Replaced the sidebar with direct dashboard actions and a discoverable Settings button beside the location switcher.
- Refined radar hover controls, attribution behavior, and full-screen map framing.
- Expanded tide high/low panels to use card space more evenly at wide window sizes.

### Fixed

- Removed obsolete sidebar state and first-run guidance after the dashboard navigation redesign.
- Improved expanded future-radar framing on wide displays.

## 0.9.0 - 2026-08-29

### Added

- Automatic daily update checks and a manual check in Settings > About.
- Update choices to open the GitHub release, receive a reminder the next day, or skip a version.

### Fixed

- Current dew point now uses the same timestamp as current temperature and humidity, preventing stale comfort classifications near category boundaries.

## 0.8.0 - 2026-08-28

### Added

- Automatic Windows location support with a manual-location fallback.
- A one-time navigation tip that introduces Radar, Alerts, Settings, and the pin-open menu option.
- An **Add location** entry in the dashboard location picker and a clear dashboard return action in Settings.
- Automated regression checks for provider parsing, cached forecasts, settings migration, and tide overrides.

### Improved

- Adaptive first-launch window sizing for smaller desktop work areas.
- Clean installs begin with the navigation pane collapsed, and first-run guidance no longer waits for radar or network initialization.
- Dashboard radar reports a delayed or unavailable state instead of remaining indefinitely on its initialization screen.
- The installer detects a missing Microsoft Edge WebView2 Runtime and invokes Microsoft's bundled Evergreen bootstrapper only when needed.
- Setup no longer prompts for a Windows restart after installing WebView2 when Forecast Center can launch immediately.
- NOAA observed radar, matching forecast-radar colors, and a simplified Light-to-Heavy scale.
- Provider attribution for OpenMapTiles and GeoNames, plus an exact release dependency-license bundle.
- Bundled pinned radar browser libraries instead of downloading executable JavaScript from a CDN at runtime.
- Location, settings-navigation, and tide-station selection behavior.

### Fixed

- Current dew point is calculated from the same current temperature and humidity observation, while UV and visibility use the nearest hourly timestamp instead of falling back to midnight.
- Opening Add location no longer crashes during first-use navigation.
- Automatic location updates both the dashboard title and location picker.
- Tide station selection remains visible when automatic station choice is active.
- WebView2 cache data is stored with the app's LocalAppData instead of leaving a runtime folder in the installation directory.

## 0.7.0 - 2026-08-27

### Added

- A categorized Settings experience for General, Locations, Sources & data, and About.
- An in-app About card showing the installed version and release date.

### Improved

- Redesigned every Settings category with Fluent cards, clearer descriptions, icon accents, and more compact controls.
- Replaced the anonymous CARTO radar basemap with OpenFreeMap vector styles and improved light-theme radar controls.
- Refined light-theme title-bar buttons, forecast tooltips, current-condition statistics, tide colors, and navigation blending.
- Improved data attribution and local-cache status presentation.

### Fixed

- Dew-point tooltips can escape chart bounds without restoring the resize cursor or hover flicker.
- Forecast tooltips and tide information remain readable in both light and dark themes.
- Radar map styling now follows theme changes reliably.

## 0.6.0 - 2026-08-25

### Added

- NOAA HRRR future radar with locally decoded 15-minute frames through roughly six hours.
- A unified, time-based radar scrubber that plays continuously from observed radar through future radar.
- Persistent radar timestamps and responsive timeline labels.
- Version-aware installer messaging that identifies the installed and incoming versions.
- Auto-hiding navigation with an optional pin-open control.

### Improved

- Matched current and future radar reflectivity colors, smoother frame transitions, and a wider Fluent playback control.
- Reduced HRRR network use with per-location memory caching and reuse of the availability-check download.
- Stabilized and polished hourly, daily, and dew-point chart tooltips.
- Simplified the default-location action and refined sidebar behavior.

### Fixed

- Dew-point hover no longer exposes a resize cursor or flickers between hourly regions.
- Forecast tooltip sizing and wrapping now adapt to their content.

## 0.5.0 - 2026-08-24

### Added

- Nationwide NOAA tide-station catalog with 30-day local caching, manual status/refresh controls, and bundled offline fallbacks.
- Per-location tide-station overrides plus an optional global override.
- Downloaded-data health information in Settings.
- Native-style system-tray weather icon and live temperature/location tooltip.
- Dashboard skeleton loading treatment and subtle page entrance motion.

### Improved

- Refined Fluent navigation sizing, icons, selection treatment, and Settings separation.
- Improved environmental-card layout and controls.
- Polished environmental scale indicators, forecast tooltips, and alert/status corner treatment.
- Improved tide-station labeling when Automatic selection is active.

### Fixed

- Hourly and 10-day tooltip data no longer persists across location changes.
- Tide overrides now follow the correct saved weather location.
- Settings content scrolls correctly at smaller window sizes.
- Dashboard loading can no longer remain blocked by a slow weather request.

## Earlier releases

Release history before 0.5.0 is available in the repository's GitHub Releases.
