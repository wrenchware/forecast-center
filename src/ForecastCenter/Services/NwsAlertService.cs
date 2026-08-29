using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class NwsAlertService(HttpClient http) : IAlertService
{
    public async Task<IReadOnlyList<WeatherAlert>> GetActiveAsync(LocationResult location, CancellationToken ct)
    {
        if (!location.Country.Contains("United States", StringComparison.OrdinalIgnoreCase) && location.Country != "US") return [];
        var url = $"https://api.weather.gov/alerts/active?point={location.Latitude:F4},{location.Longitude:F4}&status=actual";
        var response = await http.GetFromJsonAsync<NwsResponse>(url, ct);
        return response?.Features?.Select(x => new WeatherAlert(x.Id ?? "", x.Properties.Event ?? "Weather alert", x.Properties.Headline ?? x.Properties.Event ?? "Weather alert", x.Properties.Severity ?? "Unknown", x.Properties.Effective, x.Properties.Expires, x.Properties.Description ?? "", x.Properties.Instruction ?? "")).ToList() ?? [];
    }
    private sealed record NwsResponse([property: JsonPropertyName("features")] List<NwsFeature>? Features);
    private sealed record NwsFeature([property: JsonPropertyName("id")] string? Id, [property: JsonPropertyName("properties")] NwsProperties Properties);
    private sealed record NwsProperties([property: JsonPropertyName("event")] string? Event, [property: JsonPropertyName("headline")] string? Headline, [property: JsonPropertyName("severity")] string? Severity, [property: JsonPropertyName("effective")] DateTimeOffset? Effective, [property: JsonPropertyName("expires")] DateTimeOffset? Expires, [property: JsonPropertyName("description")] string? Description, [property: JsonPropertyName("instruction")] string? Instruction);
}
