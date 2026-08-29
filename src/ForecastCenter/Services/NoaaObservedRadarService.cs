using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ForecastCenter.Services;

public sealed record NoaaObservedRadarFrame(DateTimeOffset Time);

/// <summary>Gets exact recent MRMS frame times from NOAA's time-enabled radar ImageServer.</summary>
public sealed class NoaaObservedRadarService
{
    private const string ServiceUrl = "https://mapservices.weather.noaa.gov/eventdriven/rest/services/radar/radar_base_reflectivity_time/ImageServer";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, CacheEntry> _cache = [];

    public NoaaObservedRadarService() =>
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.NetworkUserAgent);

    public async Task<IReadOnlyList<NoaaObservedRadarFrame>> GetFramesAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var domain = Domain(latitude, longitude);
        if (_cache.TryGetValue(domain, out var cached) && DateTimeOffset.UtcNow - cached.SavedAt < TimeSpan.FromMinutes(4))
            return cached.Frames;

        var where = Uri.EscapeDataString($"name LIKE '{domain}_%'");
        var url = $"{ServiceUrl}/query?where={where}&outFields=idp_validtime&returnGeometry=false" +
                  "&orderByFields=idp_validtime%20DESC&resultRecordCount=16&f=json";
        var response = await _http.GetFromJsonAsync<QueryResponse>(url, ct);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-95);
        var frames = response?.Features
            .Select(feature => DateTimeOffset.FromUnixTimeMilliseconds(feature.Attributes.ValidTime))
            .Where(time => time >= cutoff && time <= DateTimeOffset.UtcNow.AddMinutes(2))
            .Distinct()
            .OrderBy(time => time)
            .TakeLast(10)
            .Select(time => new NoaaObservedRadarFrame(time))
            .ToList() ?? [];

        if (frames.Count > 0) _cache[domain] = new(DateTimeOffset.UtcNow, frames);
        return frames.Count > 0 ? frames : cached?.Frames ?? [];
    }

    private static string Domain(double latitude, double longitude)
    {
        if (latitude is >= 17 and <= 24 && longitude is >= -162 and <= -153) return "HAWAII";
        if (latitude is >= 16 and <= 21 && longitude is >= -69 and <= -63) return "CARIB";
        if (latitude is >= 12 and <= 16 && (longitude >= 143 || longitude <= -170)) return "GUAM";
        if (latitude >= 50 && longitude <= -130) return "ALASKA";
        return "CONUS";
    }

    private sealed record CacheEntry(DateTimeOffset SavedAt, IReadOnlyList<NoaaObservedRadarFrame> Frames);
    private sealed record QueryResponse([property: JsonPropertyName("features")] List<QueryFeature> Features);
    private sealed record QueryFeature([property: JsonPropertyName("attributes")] QueryAttributes Attributes);
    private sealed record QueryAttributes([property: JsonPropertyName("idp_validtime")] long ValidTime);
}
