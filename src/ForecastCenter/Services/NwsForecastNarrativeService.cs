using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class NwsForecastNarrativeService(HttpClient http) : IForecastNarrativeService
{
    private readonly Dictionary<string, (string Url, DateTimeOffset Expires)> _pointCache = [];

    public async Task<IReadOnlyDictionary<DateOnly, string>> GetDailyNarrativesAsync(LocationResult location, CancellationToken ct)
    {
        if (!location.Country.Contains("United States", StringComparison.OrdinalIgnoreCase) && location.Country != "US") return new Dictionary<DateOnly, string>();
        var key = $"{location.Latitude:F3},{location.Longitude:F3}";
        if (!_pointCache.TryGetValue(key, out var cached) || cached.Expires < DateTimeOffset.UtcNow)
        {
            var point = await http.GetFromJsonAsync<PointResponse>($"https://api.weather.gov/points/{location.Latitude:F4},{location.Longitude:F4}", ct);
            var url = point?.Properties.Forecast ?? throw new InvalidOperationException("NWS did not provide a forecast URL for this location.");
            cached = (url, DateTimeOffset.UtcNow.AddDays(7));
            _pointCache[key] = cached;
        }
        var forecast = await http.GetFromJsonAsync<ForecastResponse>(cached.Url, ct);
        return forecast?.Properties.Periods
            .Where(x => x.IsDaytime && !string.IsNullOrWhiteSpace(x.DetailedForecast))
            .GroupBy(x => DateOnly.FromDateTime(x.StartTime.LocalDateTime))
            .ToDictionary(x => x.Key, x => x.First().DetailedForecast!) ?? new Dictionary<DateOnly, string>();
    }

    private sealed record PointResponse([property: JsonPropertyName("properties")] PointProperties Properties);
    private sealed record PointProperties([property: JsonPropertyName("forecast")] string? Forecast);
    private sealed record ForecastResponse([property: JsonPropertyName("properties")] ForecastProperties Properties);
    private sealed record ForecastProperties([property: JsonPropertyName("periods")] List<ForecastPeriod> Periods);
    private sealed record ForecastPeriod([property: JsonPropertyName("startTime")] DateTimeOffset StartTime, [property: JsonPropertyName("isDaytime")] bool IsDaytime, [property: JsonPropertyName("detailedForecast")] string? DetailedForecast);
}
