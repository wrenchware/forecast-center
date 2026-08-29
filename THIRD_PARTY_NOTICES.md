# Third-party notices and data sources

Forecast Center is an independent project and is not affiliated with or endorsed
by Microsoft, NOAA, the National Weather Service, Open-Meteo, OpenFreeMap, or
OpenStreetMap.

This notice was reviewed against provider documentation on August 28, 2026.
The release payload also contains `ThirdPartyLicenses/NuGet-Package-Inventory.csv`
and the license/notice files shipped by the exact resolved NuGet packages.

## Weather and environmental data

- **Open-Meteo** supplies current conditions, forecasts, geocoding, UV, and air
  quality data. API data is provided under CC BY 4.0. The free hosted API is for
  non-commercial use and is subject to published request limits. Attribution:
  [Weather data by Open-Meteo.com](https://open-meteo.com/).
- **NOAA / National Weather Service** supplies alerts, narrative forecasts,
  MRMS observed radar, NDFD layers, HRRR model data, and supporting location
  metadata. U.S. government data is generally public domain unless marked
  otherwise and is provided without warranty. NOAA/NWS is the authoritative
  source for official warnings.
- **NOAA Tides & Currents (CO-OPS)** supplies station metadata and tide
  predictions. NOAA is credited as the source.

Weather information can be delayed, unavailable, or inaccurate. Forecast Center
is not a safety, emergency, aviation, navigation, or marine-navigation system.

## Maps and place labels

- Map hosting and styles: [OpenFreeMap](https://openfreemap.org/), provided
  as-is without an SLA. Requested attribution: OpenFreeMap © OpenMapTiles Data
  from OpenStreetMap.
- Map data: © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright),
  available under the Open Database License.
- OpenFreeMap styles incorporate OpenMapTiles components and their applicable
  notices.
- Nearby-city labels are selected from the bundled
  [GeoNames](https://www.geonames.org/) cities15000 dataset, licensed under
  CC BY 4.0.

## Software components

The application uses .NET, Windows App SDK, CommunityToolkit.Mvvm, WebView2,
GribSharp 1.0.16, CSJ2K 3.0.0, Leaflet 1.9.4 (BSD-2-Clause), MapLibre GL JS
5.19.0 (BSD-3-Clause), and maplibre-gl-leaflet 0.1.3 (ISC). Their respective
copyright and license terms remain with their authors.

The browser libraries are bundled at the exact versions listed above, so radar
does not retrieve executable JavaScript from a CDN at runtime. The release-license
collector inventories all resolved NuGet dependencies and copies package-provided
license and notice files into the installer payload.

## Provider and component documentation

- [Open-Meteo terms](https://open-meteo.com/en/terms)
- [Open-Meteo licence](https://open-meteo.com/en/license)
- [NWS API documentation](https://www.weather.gov/documentation/services-web-api)
- [NOAA Tides & Currents metadata API](https://api.tidesandcurrents.noaa.gov/mdapi/prod/)
- [NOAA NOMADS information](https://nomads.ncep.noaa.gov/info.php?page=help)
- [OpenFreeMap attribution and usage](https://openfreemap.org/)
- [OpenStreetMap copyright and licence](https://www.openstreetmap.org/copyright)
- [GeoNames data terms](https://www.geonames.org/export/)
- [Leaflet licence](https://github.com/Leaflet/Leaflet/blob/v1.9.4/LICENSE)
- [MapLibre GL JS licence](https://github.com/maplibre/maplibre-gl-js/blob/v5.19.0/LICENSE.txt)
- [maplibre-gl-leaflet licence](https://github.com/maplibre/maplibre-gl-leaflet/blob/v0.1.3/LICENSE)
