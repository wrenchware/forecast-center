# Provider audit

Reviewed August 28, 2026 against the providers' published documentation.
Forecast Center is an independent client and is not endorsed by any provider.

## Open-Meteo

- Used for forecasts, current conditions, environmental data, and geocoding.
- API data is CC BY 4.0 and must be attributed with a link near displayed data.
- The free hosted endpoint is limited to non-commercial use and currently
  publishes limits of 10,000 calls/day, 5,000/hour, and 600/minute.
- Forecast Center is ad-free and non-commercial. A commercial fork must use an
  eligible paid endpoint or a compliant self-hosted deployment.
- References: <https://open-meteo.com/en/terms> and
  <https://open-meteo.com/en/license>.

## NOAA and National Weather Service

- Used for alerts, narrative forecasts, MRMS observed radar, HRRR model data,
  NDFD fallback data, station metadata, and tide predictions.
- NWS API data is open and free for any purpose, subject to reasonable abuse
  controls. API clients must send a distinct User-Agent; contact information is
  strongly recommended.
- NOAA-created data is generally public domain unless a product says otherwise.
  NOAA must not be presented as endorsing Forecast Center, and NOAA is credited
  as the source.
- NWS requests identify the client with the public repository URL in
  `AppIdentity.NetworkUserAgent`.
- References: <https://www.weather.gov/documentation/services-web-api>,
  <https://oceanservice.noaa.gov/about/faq.html>, and
  <https://api.tidesandcurrents.noaa.gov/mdapi/prod/>.

## OpenFreeMap, OpenMapTiles, and OpenStreetMap

- OpenFreeMap permits commercial use, provides no SLA, and requires attribution.
- The requested credit is “OpenFreeMap © OpenMapTiles Data from OpenStreetMap.”
- OpenStreetMap attribution links to its copyright page and identifies its
  contributors. Forecast Center shows map attribution in radar UI and the
  Sources & data page.
- References: <https://openfreemap.org/> and
  <https://www.openstreetmap.org/copyright>.

## GeoNames

- The bundled cities15000 dataset is used only to choose nearby city labels.
- GeoNames data is CC BY, permits commercial use, and requires credit with a
  link or other reference. Forecast Center includes GeoNames in radar and
  Sources & data attribution.
- Reference: <https://www.geonames.org/export/>.

## Release conclusion

The current provider set is suitable for a free, ad-free, non-commercial public
release with the visible attributions retained. Provider terms must be reviewed
again before any commercial distribution.
