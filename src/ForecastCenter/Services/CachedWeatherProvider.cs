using System.Text.Json;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class CachedWeatherProvider : IWeatherProvider
{
    private readonly IWeatherProvider _inner;
    private readonly string _folder;
    public bool LastResultWasCached { get; private set; }

    public CachedWeatherProvider(IWeatherProvider inner, string? folder = null)
    {
        _inner = inner;
        _folder = folder ?? Path.Combine(AppIdentity.DataRoot, "cache");
    }

    public async Task<WeatherSnapshot> GetWeatherAsync(LocationResult location, bool metric, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_folder, $"{location.Latitude:F3}_{location.Longitude:F3}_{(metric ? "metric" : "imperial")}.json");
        try
        {
            var result = await _inner.GetWeatherAsync(location, metric, cancellationToken);
            Directory.CreateDirectory(_folder);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result), cancellationToken);
            LastResultWasCached = false;
            return result;
        }
        catch when (!cancellationToken.IsCancellationRequested && File.Exists(path))
        {
            var cached = JsonSerializer.Deserialize<WeatherSnapshot>(await File.ReadAllTextAsync(path, cancellationToken));
            if (cached is null) throw;
            LastResultWasCached = true;
            return cached;
        }
    }
}
