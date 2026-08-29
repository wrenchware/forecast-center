namespace ForecastCenter.Models;

public sealed record LocationResult(string Name, string AdminArea, string Country, double Latitude, double Longitude)
{
    public string DisplayName => string.Join(", ", new[] { Name, AdminArea, Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
    public string PickerDisplayName => string.Join(", ", new[] { Name, string.IsNullOrWhiteSpace(AdminArea) ? Country : AdminArea }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
    public string StorageKey => $"{Latitude:R},{Longitude:R}";
}

public sealed record CurrentWeather(
    double Temperature, double FeelsLike, double Humidity, double DewPoint,
    double WindSpeed, double WindDirection, double? WindGust, double? Visibility,
    double Pressure, double? UvIndex, int WeatherCode, bool IsDay, DateTime UpdatedAt);

public sealed record HourlyWeather(DateTime Time, double Temperature, int WeatherCode, int PrecipitationProbability, double? DewPoint = null);
public sealed record MinuteWeather(DateTime Time, double Precipitation, double Rain, double Snowfall, int WeatherCode, int PrecipitationProbability);
public sealed record DailyWeather(DateTime Date, double High, double Low, int WeatherCode, int PrecipitationProbability, DateTime Sunrise, DateTime Sunset);
public sealed record NearbyTemperature(string Name, double Latitude, double Longitude, double Temperature, string Unit, long Population = 0);
public sealed record WeatherSnapshot(LocationResult Location, CurrentWeather Current, IReadOnlyList<HourlyWeather> Hourly, IReadOnlyList<DailyWeather> Daily)
{
    // Kept as an initialized property so caches written by older releases remain readable.
    public IReadOnlyList<MinuteWeather> Minutely15 { get; init; } = [];
}
public sealed record WeatherAlert(string Id, string Event, string Headline, string Severity, DateTimeOffset? Effective, DateTimeOffset? Expires, string Description, string Instruction);

public static class WeatherCode
{
    public static string Description(int code) => code switch
    {
        0 => "Clear sky", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Overcast",
        45 or 48 => "Fog", 51 or 53 or 55 => "Drizzle", 56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain", 66 or 67 => "Freezing rain", 71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Rain showers", 85 or 86 => "Snow showers",
        95 => "Thunderstorm", 96 or 99 => "Thunderstorm with hail", _ => "Unknown"
    };

    public static string Glyph(int code, bool day = true) => code switch
    {
        0 => day ? "☀" : "☾", 1 or 2 => day ? "🌤" : "☁", 3 => "☁", 45 or 48 => "≋",
        >= 51 and <= 67 => "🌧", >= 71 and <= 77 => "❄", >= 80 and <= 82 => "🌦",
        85 or 86 => "🌨", >= 95 => "⛈", _ => "·"
    };
}
