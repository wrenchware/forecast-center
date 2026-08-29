using System.Text.Json;

namespace ForecastCenter.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly string _path;
    public AppSettings Current { get; private set; }
    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(AppIdentity.DataRoot, "settings.json");
        try
        {
            Current = File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new() : new();
            if (!string.IsNullOrWhiteSpace(Current.TideStationId) && Current.TideStationOverrides.Count == 0 && string.IsNullOrWhiteSpace(Current.GlobalTideStationId))
            {
                Current = Current with
                {
                    TideStationOverrides = new Dictionary<string, string> { [Current.DefaultLocation.StorageKey] = Current.TideStationId },
                    TideStationId = null
                };
            }
        }
        catch { Current = new(); }
    }
    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        Current = settings;
    }
}
