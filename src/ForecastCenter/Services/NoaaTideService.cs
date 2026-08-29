using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class NoaaTideService(HttpClient http)
{
    private const string StationCatalogUrl = "https://api.tidesandcurrents.noaa.gov/mdapi/prod/webapi/stations.json?type=tidepredictions&units=english";
    public static IReadOnlyList<TideStation> Stations { get; } =
    [
        new("8461490", "New London", "CT", 41.355, -72.090),
        new("8465705", "New Haven", "CT", 41.283, -72.908),
        new("8467150", "Bridgeport", "CT", 41.175, -73.181),
        new("8454000", "Providence", "RI", 41.807, -71.401),
        new("8452660", "Newport", "RI", 41.505, -71.327),
        new("8458022", "Charlestown — Weekapaug Point", "RI", 41.3283, -71.7617),
        new("8510560", "Montauk", "NY", 41.048, -71.960),
        new("8518750", "The Battery", "NY", 40.700, -74.014),
        new("8443970", "Boston", "MA", 42.354, -71.053),
        new("8418150", "Portland", "ME", 43.658, -70.245)
    ];

    private readonly string _cacheFolder = Path.Combine(AppIdentity.DataRoot, "cache", "tides");
    private IReadOnlyList<TideStation>? _stationCatalog;
    public TideCatalogStatus CatalogStatus { get; private set; } = new(0, null, false, false);

    public async Task<IReadOnlyList<TideStation>> GetStationsAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (_stationCatalog is not null && !forceRefresh) return _stationCatalog;
        var cachePath = Path.Combine(_cacheFolder, "noaa-tide-stations.json");
        try
        {
            if (!forceRefresh && File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromDays(30))
            {
                var cached = JsonSerializer.Deserialize<List<TideStation>>(await File.ReadAllTextAsync(cachePath, ct));
                if (cached is { Count: > 1000 })
                {
                    CatalogStatus = new(cached.Count, File.GetLastWriteTime(cachePath), true, false);
                    return _stationCatalog = cached;
                }
            }

            var response = await http.GetFromJsonAsync<StationResponse>(StationCatalogUrl, ct)
                ?? throw new InvalidOperationException("NOAA returned no tide-station metadata.");
            var stations = response.Stations
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => new TideStation(item.Id, NormalizeName(item.Name), string.IsNullOrWhiteSpace(item.State) ? "US" : item.State, item.Latitude, item.Longitude))
                .DistinctBy(item => item.Id)
                .ToList();
            if (stations.Count < 1000) throw new InvalidOperationException("NOAA returned an incomplete tide-station catalog.");
            Directory.CreateDirectory(_cacheFolder);
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(stations), ct);
            CatalogStatus = new(stations.Count, DateTimeOffset.Now, false, false);
            return _stationCatalog = stations;
        }
        catch when (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(cachePath) && JsonSerializer.Deserialize<List<TideStation>>(await File.ReadAllTextAsync(cachePath, ct)) is { Count: > 0 } cached)
                {
                    CatalogStatus = new(cached.Count, File.GetLastWriteTime(cachePath), true, true);
                    return _stationCatalog = cached;
                }
            }
            catch { }
            CatalogStatus = new(Stations.Count, null, false, true);
            return _stationCatalog = Stations;
        }
    }

    public async Task<IReadOnlyList<TideStation>> RefreshStationsAsync(CancellationToken ct) => await GetStationsAsync(ct, true);

    public async Task<bool> IsNearStationAsync(LocationResult location, double maximumDistanceKm, CancellationToken ct)
    {
        var stations = await GetStationsAsync(ct);
        return stations.Count > 0 && stations.Min(item => DistanceKm(location.Latitude, location.Longitude, item.Latitude, item.Longitude)) <= maximumDistanceKm;
    }

    public async Task<TideSnapshot> GetAsync(LocationResult location, string? stationOverride, CancellationToken ct)
    {
        var stations = await GetStationsAsync(ct);
        var station = stations.FirstOrDefault(item => item.Id == stationOverride) ?? stations.MinBy(item => DistanceKm(location.Latitude, location.Longitude, item.Latitude, item.Longitude))!;
        var cachePath = Path.Combine(_cacheFolder, $"{station.Id}.json");
        try
        {
            var url = "https://api.tidesandcurrents.noaa.gov/api/prod/datagetter" +
                      $"?product=predictions&application=ForecastCenter&station={station.Id}&datum=MLLW" +
                      $"&time_zone=lst_ldt&units=english&interval=hilo&format=json&begin_date={DateTime.Now:yyyyMMdd}&range=48";
            var response = await http.GetFromJsonAsync<PredictionResponse>(url, ct)
                ?? throw new InvalidOperationException("NOAA returned no tide data.");
            var predictions = response.Predictions
                .Select(item => new TidePrediction(DateTime.ParseExact(item.Time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), double.Parse(item.Height, CultureInfo.InvariantCulture), item.Type == "H"))
                .Where(item => item.Time >= DateTime.Now.AddHours(-2))
                .ToList();
            var snapshot = new TideSnapshot(station, predictions, DateTimeOffset.Now);
            Directory.CreateDirectory(_cacheFolder);
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(snapshot), ct);
            return snapshot;
        }
        catch when (!ct.IsCancellationRequested && File.Exists(cachePath))
        {
            return JsonSerializer.Deserialize<TideSnapshot>(await File.ReadAllTextAsync(cachePath, ct))
                ?? throw new InvalidOperationException("The cached tide data could not be read.");
        }
    }

    private static string NormalizeName(string name) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.Trim().ToLowerInvariant());

    private static double DistanceKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double radius = 6371;
        var lat1 = latitude1 * Math.PI / 180;
        var lat2 = latitude2 * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (longitude2 - longitude1) * Math.PI / 180;
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return radius * 2 * Math.Asin(Math.Sqrt(h));
    }

    private sealed record PredictionResponse([property: JsonPropertyName("predictions")] List<PredictionDto> Predictions);
    private sealed record PredictionDto([property: JsonPropertyName("t")] string Time, [property: JsonPropertyName("v")] string Height, [property: JsonPropertyName("type")] string Type);
    private sealed record StationResponse([property: JsonPropertyName("stations")] List<StationDto> Stations);
    private sealed record StationDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("lat")] double Latitude,
        [property: JsonPropertyName("lng")] double Longitude);
}

public sealed record TideCatalogStatus(int StationCount, DateTimeOffset? UpdatedAt, bool UsingCache, bool RefreshFailed);
