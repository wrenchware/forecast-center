using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForecastCenter.Models;

namespace ForecastCenter.Services;

public sealed class OpenMeteoEnvironmentalService(HttpClient http)
{
    private readonly string _cacheFolder = Path.Combine(
        AppIdentity.DataRoot, "cache", "environment");

    public async Task<EnvironmentalSnapshot> GetAsync(LocationResult location, CancellationToken ct)
    {
        var cachePath = Path.Combine(_cacheFolder, $"{location.Latitude:F3}_{location.Longitude:F3}.json");
        try
        {
            var c = CultureInfo.InvariantCulture;
            var current = "uv_index,us_aqi,us_aqi_pm2_5,us_aqi_pm10,us_aqi_nitrogen_dioxide,us_aqi_ozone,us_aqi_sulphur_dioxide,us_aqi_carbon_monoxide,pm2_5";
            var url = $"https://air-quality-api.open-meteo.com/v1/air-quality?latitude={location.Latitude.ToString(c)}&longitude={location.Longitude.ToString(c)}&current={current}&hourly=uv_index&forecast_days=2&timezone=auto";
            var response = await http.GetFromJsonAsync<AirQualityResponse>(url, ct)
                ?? throw new InvalidOperationException("Open-Meteo returned no environmental data.");

            var futureUv = response.Hourly.Time.Select((time, index) =>
                    (Time: DateTime.Parse(time), Value: At(response.Hourly.UvIndex, index)))
                .Where(item => item.Time >= DateTime.Now.AddHours(-1) && item.Time < DateTime.Now.AddHours(30))
                .ToList();
            var peak = futureUv.OrderByDescending(item => item.Value).FirstOrDefault();
            var pollutant = DominantPollutant(response.Current);
            var snapshot = new EnvironmentalSnapshot(
                response.Current.UvIndex,
                peak.Value,
                peak == default ? null : peak.Time,
                (int)Math.Round(response.Current.UsAqi),
                pollutant,
                response.Current.Pm25,
                DateTime.Parse(response.Current.Time));

            Directory.CreateDirectory(_cacheFolder);
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(snapshot), ct);
            return snapshot;
        }
        catch when (!ct.IsCancellationRequested && File.Exists(cachePath))
        {
            return JsonSerializer.Deserialize<EnvironmentalSnapshot>(await File.ReadAllTextAsync(cachePath, ct))
                ?? throw new InvalidOperationException("The cached environmental data could not be read.");
        }
    }

    private static string DominantPollutant(CurrentAirQuality current)
    {
        var values = new (string Name, double Value)[]
        {
            ("PM2.5", current.AqiPm25), ("PM10", current.AqiPm10),
            ("Ozone", current.AqiOzone), ("Nitrogen dioxide", current.AqiNitrogenDioxide),
            ("Sulphur dioxide", current.AqiSulphurDioxide), ("Carbon monoxide", current.AqiCarbonMonoxide)
        };
        return values.OrderByDescending(item => item.Value).First().Name;
    }

    private static double At(List<double> values, int index) => index >= 0 && index < values.Count ? values[index] : 0;

    private sealed record AirQualityResponse(
        [property: JsonPropertyName("current")] CurrentAirQuality Current,
        [property: JsonPropertyName("hourly")] HourlyAirQuality Hourly);
    private sealed record HourlyAirQuality(
        [property: JsonPropertyName("time")] List<string> Time,
        [property: JsonPropertyName("uv_index")] List<double> UvIndex);
    private sealed record CurrentAirQuality(
        [property: JsonPropertyName("time")] string Time,
        [property: JsonPropertyName("uv_index")] double UvIndex,
        [property: JsonPropertyName("us_aqi")] double UsAqi,
        [property: JsonPropertyName("us_aqi_pm2_5")] double AqiPm25,
        [property: JsonPropertyName("us_aqi_pm10")] double AqiPm10,
        [property: JsonPropertyName("us_aqi_nitrogen_dioxide")] double AqiNitrogenDioxide,
        [property: JsonPropertyName("us_aqi_ozone")] double AqiOzone,
        [property: JsonPropertyName("us_aqi_sulphur_dioxide")] double AqiSulphurDioxide,
        [property: JsonPropertyName("us_aqi_carbon_monoxide")] double AqiCarbonMonoxide,
        [property: JsonPropertyName("pm2_5")] double? Pm25);
}
