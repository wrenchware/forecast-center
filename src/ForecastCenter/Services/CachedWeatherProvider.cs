using System.Text.Json;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class CachedWeatherProvider : IWeatherProvider
{
    private readonly IWeatherProvider _inner;
    private readonly string _folder;
    private readonly string _diagnosticPath;
    public bool LastResultWasCached { get; private set; }

    public CachedWeatherProvider(IWeatherProvider inner, string? folder = null)
    {
        _inner = inner;
        _folder = folder ?? Path.Combine(AppIdentity.DataRoot, "cache");
        _diagnosticPath = folder is null
            ? Path.Combine(AppIdentity.DataRoot, "weather-refresh-status.txt")
            : Path.Combine(folder, "weather-refresh-status.txt");
    }

    public async Task<WeatherSnapshot> GetWeatherAsync(LocationResult location, bool metric, CancellationToken cancellationToken)
    {
        var path = CachePath(location, metric);
        try
        {
            var result = await _inner.GetWeatherAsync(location, metric, cancellationToken);
            Directory.CreateDirectory(_folder);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result), cancellationToken);
            LastResultWasCached = false;
            WriteDiagnostic("Last refresh succeeded.");
            return result;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            WriteDiagnostic($"Refresh failed: {ex}");
            var cached = await TryGetCachedAsync(location, metric);
            if (cached is null) throw;
            LastResultWasCached = true;
            return cached;
        }
    }

    /// <summary>Reads the last saved forecast without touching the network, so launch can paint immediately.</summary>
    public async Task<WeatherSnapshot?> TryGetCachedAsync(LocationResult location, bool metric, TimeSpan? maxAge = null)
    {
        var path = CachePath(location, metric);
        try
        {
            return await Task.Run(async () =>
                File.Exists(path) && (maxAge is null || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= maxAge)
                    ? JsonSerializer.Deserialize<WeatherSnapshot>(await File.ReadAllTextAsync(path))
                    : null);
        }
        catch { return null; }
    }

    private string CachePath(LocationResult location, bool metric) =>
        Path.Combine(_folder, $"{location.Latitude:F3}_{location.Longitude:F3}_{(metric ? "metric" : "imperial")}.json");

    private void WriteDiagnostic(string message)
    {
        var path = _diagnosticPath;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{message}");
            }
            catch { }
        });
    }
}
