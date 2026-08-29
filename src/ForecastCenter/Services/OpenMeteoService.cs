using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class OpenMeteoService(HttpClient http) : IWeatherProvider, ILocationService
{
    private readonly Dictionary<string, List<CityCandidate>> _nearbyCityCache = [];
    private List<CityCandidate>? _globalCities;
    private static readonly LocationResult[] RegionalCities =
    [
        new("Hartford", "CT", "United States", 41.7658, -72.6734),
        new("Providence", "RI", "United States", 41.8240, -71.4128),
        new("New Haven", "CT", "United States", 41.3083, -72.9279),
        new("New London", "CT", "United States", 41.3557, -72.0995),
        new("Worcester", "MA", "United States", 42.2626, -71.8023),
        new("Springfield", "MA", "United States", 42.1015, -72.5898),
        new("Boston", "MA", "United States", 42.3601, -71.0589),
        new("New York", "NY", "United States", 40.7128, -74.0060),
        new("Albany", "NY", "United States", 42.6526, -73.7562),
        new("Bridgeport", "CT", "United States", 41.1792, -73.1894),
        new("Norwich", "CT", "United States", 41.5243, -72.0759),
        new("Newport", "RI", "United States", 41.4901, -71.3128)
    ];

    public async Task<IReadOnlyList<NearbyTemperature>> GetNearbyTemperaturesAsync(LocationResult center, bool metric, CancellationToken ct)
    {
        var cities = await DiscoverNearbyCitiesAsync(center, ct);
        if (cities.Count == 0) return [];

        var c = CultureInfo.InvariantCulture;
        var latitudes = string.Join(',', cities.Select(city => city.Location.Latitude.ToString(c)));
        var longitudes = string.Join(',', cities.Select(city => city.Location.Longitude.ToString(c)));
        var unit = metric ? "celsius" : "fahrenheit";
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitudes}&longitude={longitudes}&current=temperature_2m&temperature_unit={unit}";
        var responses = await http.GetFromJsonAsync<List<NearbyForecastDto>>(url, ct) ?? [];
        return cities.Zip(responses, (city, response) => new NearbyTemperature(city.Location.Name, city.Location.Latitude, city.Location.Longitude, response.Current.Temperature, metric ? "°C" : "°F", city.Population)).ToList();
    }

    private async Task<List<CityCandidate>> DiscoverNearbyCitiesAsync(LocationResult center, CancellationToken ct)
    {
        var cacheKey = $"{Math.Round(center.Latitude, 1):0.0},{Math.Round(center.Longitude, 1):0.0}";
        if (_nearbyCityCache.TryGetValue(cacheKey, out var cached)) return cached;

        try
        {
            var candidates = await Task.Run(() => LoadGlobalCities()
                .Where(item => DistanceKm(center, item.Location) is >= 18 and <= 260)
                .OrderByDescending(item => item.Population)
                .ThenBy(item => DistanceKm(center, item.Location))
                .ToList(), ct);

            var selected = new List<CityCandidate>();
            foreach (var candidate in candidates)
            {
                if (selected.Any(existing => DistanceKm(existing.Location, candidate.Location) < 35)) continue;
                selected.Add(candidate);
                if (selected.Count == 14) break;
            }
            foreach (var candidate in candidates.Where(candidate => !selected.Contains(candidate)))
            {
                if (selected.Count >= 14) break;
                selected.Add(candidate);
            }
            if (selected.Count > 0) return _nearbyCityCache[cacheKey] = selected;
        }
        catch when (!ct.IsCancellationRequested) { }

        var fallback = RegionalCities
            .Select(city => new CityCandidate(city, 0))
            .Where(item => DistanceKm(center, item.Location) is >= 18 and <= 260)
            .ToList();
        return _nearbyCityCache[cacheKey] = fallback;
    }

    private List<CityCandidate> LoadGlobalCities()
    {
        if (_globalCities is not null) return _globalCities;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "cities15000.zip");
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.First(item => item.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open());
        var cities = new List<CityCandidate>(30_000);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            if (fields.Length < 15 ||
                !double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)) continue;
            long.TryParse(fields[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);
            cities.Add(new CityCandidate(new LocationResult(fields[1], fields[10], fields[8], latitude, longitude), population));
        }
        return _globalCities = cities;
    }

    private static double DistanceKm(LocationResult a, LocationResult b)
    {
        const double radius = 6371;
        var lat1 = a.Latitude * Math.PI / 180;
        var lat2 = b.Latitude * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (b.Longitude - a.Longitude) * Math.PI / 180;
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return radius * 2 * Math.Asin(Math.Sqrt(h));
    }
    public async Task<IReadOnlyList<LocationResult>> SearchAsync(string query, CancellationToken ct)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=8&language=en&format=json";
        var data = await http.GetFromJsonAsync<GeoResponse>(url, ct);
        return data?.Results?.Select(x => new LocationResult(x.Name ?? "Unknown", x.Admin1 ?? "", x.Country ?? "", x.Latitude, x.Longitude)).ToList() ?? [];
    }

    public async Task<WeatherSnapshot> GetWeatherAsync(LocationResult location, bool metric, CancellationToken ct)
    {
        var c = CultureInfo.InvariantCulture;
        var units = metric ? "&temperature_unit=celsius&wind_speed_unit=kmh&precipitation_unit=mm" : "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch";
        var fields = "current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,surface_pressure,wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
                     "&hourly=temperature_2m,apparent_temperature,relative_humidity_2m,dew_point_2m,precipitation_probability,weather_code,visibility,surface_pressure,uv_index,wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
                     "&minutely_15=precipitation,precipitation_probability,rain,snowfall,weather_code" +
                     "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max";
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude.ToString(c)}&longitude={location.Longitude.ToString(c)}&{fields}&forecast_days=10&forecast_minutely_15=8&timezone=auto{units}";
        var d = await http.GetFromJsonAsync<ForecastResponse>(url, ct) ?? throw new InvalidOperationException("Open-Meteo returned no weather data.");
        var nowIndex = FindNearest(d.Hourly.Time, d.Current.Time);
        var current = new CurrentWeather(d.Current.Temperature, d.Current.FeelsLike, d.Current.Humidity,
            CalculateDewPoint(d.Current.Temperature, d.Current.Humidity, metric), d.Current.WindSpeed, d.Current.WindDirection, d.Current.WindGust,
            At(d.Hourly.Visibility, nowIndex) / 1000, d.Current.Pressure, AtNullable(d.Hourly.UvIndex, nowIndex),
            d.Current.WeatherCode, d.Current.IsDay == 1, DateTime.Parse(d.Current.Time));
        var providerNow = DateTime.Parse(d.Current.Time);
        var hours = d.Hourly.Time.Select((t, i) => new HourlyWeather(DateTime.Parse(t), At(d.Hourly.Temperature, i), At(d.Hourly.WeatherCode, i), At(d.Hourly.Precipitation, i), At(d.Hourly.DewPoint, i))).Where(x => x.Time >= providerNow.AddHours(-1)).Take(12).ToList();
        var minutes = d.Minutely15.Time.Select((t, i) => new MinuteWeather(DateTime.Parse(t), At(d.Minutely15.Precipitation, i), At(d.Minutely15.Rain, i), At(d.Minutely15.Snowfall, i), At(d.Minutely15.WeatherCode, i), At(d.Minutely15.Probability, i)))
            .Where(x => x.Time >= providerNow.AddMinutes(-15)).Take(5).ToList();
        var days = d.Daily.Time.Select((t, i) =>
        {
            var date = DateTime.Parse(t);
            var representativeCode = RepresentativeDayCode(d.Hourly, date, At(d.Daily.WeatherCode, i));
            return new DailyWeather(date, At(d.Daily.High, i), At(d.Daily.Low, i), representativeCode, At(d.Daily.Precipitation, i), DateTime.Parse(d.Daily.Sunrise[i]), DateTime.Parse(d.Daily.Sunset[i]));
        }).ToList();
        return new(location, current, hours, days) { Minutely15 = minutes };
    }

    private static int RepresentativeDayCode(HourlyDto hourly, DateTime date, int fallback)
    {
        // Open-Meteo's daily weather code is the most severe condition anywhere
        // in the 24-hour period, which can label a sunny day "Fog" for one dawn
        // hour. Use the modal daytime condition for a glanceable daily tile.
        var daytimeCodes = hourly.Time.Select((time, index) => (Time: DateTime.Parse(time), Code: At(hourly.WeatherCode, index)))
            .Where(item => item.Time.Date == date.Date && item.Time.Hour is >= 9 and <= 17)
            .Select(item => item.Code)
            .ToList();
        if (daytimeCodes.Count == 0) return fallback;
        return daytimeCodes.GroupBy(code => code)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => DaytimeConditionPriority(group.Key))
            .First().Key;
    }

    private static int DaytimeConditionPriority(int code) => code switch
    {
        >= 95 => 0,
        >= 51 and <= 86 => 1,
        45 or 48 => 2,
        3 => 3,
        2 => 4,
        1 => 5,
        0 => 6,
        _ => 7
    };

    private static int FindNearest(List<string> values, string value)
    {
        if (values.Count == 0) return 0;
        var target = DateTime.Parse(value);
        return values.Select((item, index) => (Index: index, Distance: Math.Abs((DateTime.Parse(item) - target).Ticks)))
            .OrderBy(item => item.Distance)
            .First().Index;
    }

    private static double CalculateDewPoint(double temperature, double relativeHumidity, bool metric)
    {
        var temperatureC = metric ? temperature : (temperature - 32d) * 5d / 9d;
        var humidityRatio = Math.Clamp(relativeHumidity, 1d, 100d) / 100d;
        const double a = 17.625;
        const double b = 243.04;
        var gamma = Math.Log(humidityRatio) + a * temperatureC / (b + temperatureC);
        var dewPointC = b * gamma / (a - gamma);
        return metric ? dewPointC : dewPointC * 9d / 5d + 32d;
    }
    private static T At<T>(List<T> values, int i) => i >= 0 && i < values.Count ? values[i] : default!;
    private static double? AtNullable(List<double> values, int i) => i >= 0 && i < values.Count ? values[i] : null;

    private sealed record GeoResponse([property: JsonPropertyName("results")] List<GeoItem>? Results);
    private sealed record NearbyForecastDto([property: JsonPropertyName("current")] NearbyCurrentDto Current);
    private sealed record NearbyCurrentDto([property: JsonPropertyName("temperature_2m")] double Temperature);
    private sealed record CityCandidate(LocationResult Location, long Population);
    private sealed record GeoItem([property: JsonPropertyName("name")] string? Name, [property: JsonPropertyName("admin1")] string? Admin1, [property: JsonPropertyName("country")] string? Country, [property: JsonPropertyName("latitude")] double Latitude, [property: JsonPropertyName("longitude")] double Longitude);
    private sealed record ForecastResponse([property: JsonPropertyName("current")] CurrentDto Current, [property: JsonPropertyName("hourly")] HourlyDto Hourly, [property: JsonPropertyName("minutely_15")] MinutelyDto Minutely15, [property: JsonPropertyName("daily")] DailyDto Daily);
    private sealed record CurrentDto([property: JsonPropertyName("time")] string Time, [property: JsonPropertyName("temperature_2m")] double Temperature, [property: JsonPropertyName("relative_humidity_2m")] double Humidity, [property: JsonPropertyName("apparent_temperature")] double FeelsLike, [property: JsonPropertyName("is_day")] int IsDay, [property: JsonPropertyName("weather_code")] int WeatherCode, [property: JsonPropertyName("surface_pressure")] double Pressure, [property: JsonPropertyName("wind_speed_10m")] double WindSpeed, [property: JsonPropertyName("wind_direction_10m")] double WindDirection, [property: JsonPropertyName("wind_gusts_10m")] double? WindGust);
    private sealed record HourlyDto([property: JsonPropertyName("time")] List<string> Time, [property: JsonPropertyName("temperature_2m")] List<double> Temperature, [property: JsonPropertyName("dew_point_2m")] List<double> DewPoint, [property: JsonPropertyName("precipitation_probability")] List<int> Precipitation, [property: JsonPropertyName("weather_code")] List<int> WeatherCode, [property: JsonPropertyName("visibility")] List<double> Visibility, [property: JsonPropertyName("surface_pressure")] List<double> Pressure, [property: JsonPropertyName("uv_index")] List<double> UvIndex);
    private sealed record MinutelyDto([property: JsonPropertyName("time")] List<string> Time, [property: JsonPropertyName("precipitation")] List<double> Precipitation, [property: JsonPropertyName("precipitation_probability")] List<int> Probability, [property: JsonPropertyName("rain")] List<double> Rain, [property: JsonPropertyName("snowfall")] List<double> Snowfall, [property: JsonPropertyName("weather_code")] List<int> WeatherCode);
    private sealed record DailyDto([property: JsonPropertyName("time")] List<string> Time, [property: JsonPropertyName("temperature_2m_max")] List<double> High, [property: JsonPropertyName("temperature_2m_min")] List<double> Low, [property: JsonPropertyName("weather_code")] List<int> WeatherCode, [property: JsonPropertyName("precipitation_probability_max")] List<int> Precipitation, [property: JsonPropertyName("sunrise")] List<string> Sunrise, [property: JsonPropertyName("sunset")] List<string> Sunset);
}
