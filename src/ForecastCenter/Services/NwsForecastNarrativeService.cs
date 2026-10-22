using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class NwsForecastNarrativeService(HttpClient http) : IForecastNarrativeService
{
    private readonly Dictionary<string, (string Url, DateTimeOffset Expires)> _pointCache = [];
    private readonly string _cacheFolder = Path.Combine(AppIdentity.DataRoot, "cache", "narratives");

    public async Task<IReadOnlyDictionary<DateOnly, string>> GetDailyNarrativesAsync(LocationResult location, CancellationToken ct)
    {
        if (!IsUnitedStates(location)) return new Dictionary<DateOnly, string>();
        var key = $"{location.Latitude:F3},{location.Longitude:F3}";
        if (!_pointCache.TryGetValue(key, out var cached) || cached.Expires < DateTimeOffset.UtcNow)
        {
            var point = await http.GetFromJsonAsync<PointResponse>($"https://api.weather.gov/points/{location.Latitude:F4},{location.Longitude:F4}", ct);
            var url = point?.Properties.Forecast ?? throw new InvalidOperationException("NWS did not provide a forecast URL for this location.");
            cached = (url, DateTimeOffset.UtcNow.AddDays(7));
            _pointCache[key] = cached;
        }
        var forecast = await http.GetFromJsonAsync<ForecastResponse>(cached.Url, ct);
        var narratives = forecast?.Properties.Periods
            .Where(x => x.IsDaytime && !string.IsNullOrWhiteSpace(x.DetailedForecast))
            .GroupBy(x => DateOnly.FromDateTime(x.StartTime.LocalDateTime))
            .ToDictionary(x => x.Key, x => x.First().DetailedForecast!) ?? new Dictionary<DateOnly, string>();
        SaveCache(location, narratives);
        return narratives;
    }

    /// <summary>Reads the last saved narratives so a cached launch shows the same text the refresh will.</summary>
    public async Task<IReadOnlyDictionary<DateOnly, string>> TryGetCachedAsync(LocationResult location)
    {
        if (!IsUnitedStates(location)) return new Dictionary<DateOnly, string>();
        var path = CachePath(location);
        try
        {
            return await Task.Run(async () =>
            {
                if (!File.Exists(path)) return new Dictionary<DateOnly, string>();
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path)) ?? [];
                return values
                    .Where(item => DateOnly.TryParseExact(item.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    .ToDictionary(item => DateOnly.ParseExact(item.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), item => item.Value);
            });
        }
        catch { return new Dictionary<DateOnly, string>(); }
    }

    private void SaveCache(LocationResult location, IReadOnlyDictionary<DateOnly, string> narratives)
    {
        var path = CachePath(location);
        var values = narratives.ToDictionary(item => item.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), item => item.Value);
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(_cacheFolder);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(values));
            }
            catch { }
        });
    }

    private string CachePath(LocationResult location) => Path.Combine(_cacheFolder, $"{location.Latitude:F3}_{location.Longitude:F3}.json");
    private static bool IsUnitedStates(LocationResult location) => location.Country.Contains("United States", StringComparison.OrdinalIgnoreCase) || location.Country == "US";

    private sealed record PointResponse([property: JsonPropertyName("properties")] PointProperties Properties);
    private sealed record PointProperties([property: JsonPropertyName("forecast")] string? Forecast);
    private sealed record ForecastResponse([property: JsonPropertyName("properties")] ForecastProperties Properties);
    private sealed record ForecastProperties([property: JsonPropertyName("periods")] List<ForecastPeriod> Periods);
    private sealed record ForecastPeriod([property: JsonPropertyName("startTime")] DateTimeOffset StartTime, [property: JsonPropertyName("isDaytime")] bool IsDaytime, [property: JsonPropertyName("detailedForecast")] string? DetailedForecast);
}
