# Privacy

Forecast Center has no accounts, advertising, analytics, telemetry, or tracking
SDK. The app does not sell or intentionally collect personal information.

## Network requests

Weather features contact these services directly from the user's PC:

- Open-Meteo receives the selected coordinates and requested forecast fields.
- NOAA and National Weather Service services receive selected coordinates or a
  nearby station identifier for alerts, forecast text, radar, model data, and
  tide predictions.
- OpenFreeMap receives map-tile requests for the area currently visible on the
  radar map. Its tiles contain OpenStreetMap data.
- Leaflet 1.9.4, MapLibre GL JS 5.19.0, and maplibre-gl-leaflet 0.1.3
  are bundled with the app and do not create separate CDN requests.

As with an ordinary web request, each provider can receive network metadata such
as the user's IP address and user-agent string. Refer to each provider's privacy
policy for its handling and retention practices.

Manual location searches send the entered search text to Open-Meteo's geocoding
service. If automatic location is enabled, Forecast Center asks Windows for the
device location and sends its coordinates to the providers needed to populate
the dashboard. Location permission is optional; manual locations remain usable
without it.

## Local data

Settings, saved locations, window state, recent successful weather responses,
radar metadata, tide-station data, and diagnostic status files are stored under:

`%LOCALAPPDATA%\Forecast Center Public`

This data stays on the PC unless it is included in a backup or deliberately
shared by the user. Uninstalling the app may leave this folder in place so an
upgrade or reinstall can retain preferences. It can be deleted manually after
Forecast Center is closed.

## Changes

Provider behavior and this notice may change as features evolve. Material
changes should be recorded in the changelog before a release.
