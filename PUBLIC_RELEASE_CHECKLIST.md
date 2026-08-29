# Release checklist

## Required before the first public release

- [Completed] Replace RainViewer observed radar with NOAA/NWS MRMS time-enabled
  imagery and use visible NOAA attribution.
- [Completed 2026-08-28] Recheck Open-Meteo, OpenFreeMap, OpenStreetMap,
  GeoNames, NOAA/NWS, and NOAA CO-OPS attribution and usage requirements.
  The hosted Open-Meteo free tier keeps this release non-commercial and below
  its published request limits; monetization requires a paid endpoint or
  self-hosting.
- [Completed] Give the installer a stable application identity and local-data directory.
- [Completed] Remove legacy WeatherDesk migration paths.
- [Completed] Add a privacy statement explaining network providers and local storage.
- [Completed] Add initial third-party notices for data, libraries, icons, and services.
- [Completed] Run a source secret scan and NuGet vulnerability audit.
- [Completed] Compile and validate the installer payload.
- [Completed] Pin and bundle the radar browser libraries locally so releases do
  not execute CDN-hosted JavaScript.
- [Completed] Generate an exact NuGet dependency inventory and collect package
  license/notice files into every release payload.
- [Completed 2026-08-28] Install 0.7.0, upgrade to 0.8.0, launch, preserve
  settings, and uninstall on the development PC. Registry and installed files
  were removed cleanly.
- Repeat the final installer smoke test on a genuinely separate Windows 11 PC.
  Windows Sandbox was attempted twice on 2026-08-28, but the host's Sandbox
  Remote Desktop session failed before the guest startup command ran.
- [Completed] Add deterministic regression coverage for Open-Meteo and NWS
  parsing, cache fallback, settings migration, per-location tide overrides,
  and first-launch-tip persistence.
- Decide whether releases will be code-signed before broad distribution.

## Publication policy

Publish downloadable builds only after provider terms, attribution, privacy,
dependency licensing, and the installer smoke test have been reviewed.
