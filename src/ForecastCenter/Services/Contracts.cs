using ForecastCenter.Models;

namespace ForecastCenter.Services;

public interface IWeatherProvider { Task<WeatherSnapshot> GetWeatherAsync(LocationResult location, bool metric, CancellationToken cancellationToken); }
public interface ILocationService { Task<IReadOnlyList<LocationResult>> SearchAsync(string query, CancellationToken cancellationToken); }
public interface IAlertService { Task<IReadOnlyList<WeatherAlert>> GetActiveAsync(LocationResult location, CancellationToken cancellationToken); }
public interface IForecastNarrativeService { Task<IReadOnlyDictionary<DateOnly, string>> GetDailyNarrativesAsync(LocationResult location, CancellationToken cancellationToken); }
public interface ISettingsService { AppSettings Current { get; } Task SaveAsync(AppSettings settings); }

public sealed record AppSettings
{
    public bool Metric { get; init; }
    public string Theme { get; init; } = "System";
    public int RefreshMinutes { get; init; } = 15;
    public string WindUnit { get; init; } = "Auto";
    public string PressureUnit { get; init; } = "Auto";
    public bool AutomaticLocation { get; init; }
    public bool StartWithWindows { get; init; }
    public LocationResult DefaultLocation { get; init; } = new("New York", "New York", "United States", 40.7128, -74.0060);
    public List<LocationResult> SavedLocations { get; init; } = [new("New York", "New York", "United States", 40.7128, -74.0060)];
    public bool SidebarVisible { get; init; } = true;
    public bool SidebarPinned { get; init; }
    public bool NavigationTipDismissed { get; init; }
    public bool MinimizeToTray { get; init; }
    // Kept for one-way migration from Forecast Center 0.4.x settings.
    public string? TideStationId { get; init; }
    public bool UseGlobalTideStation { get; init; }
    public string? GlobalTideStationId { get; init; }
    public Dictionary<string, string> TideStationOverrides { get; init; } = [];
}
