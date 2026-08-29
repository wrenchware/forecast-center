using ForecastCenter.Models;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace ForecastCenter.Services;

public sealed class WindowsLocationService
{
    public async Task<LocationResult?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        var access = await Geolocator.RequestAccessAsync();
        if (access != GeolocationAccessStatus.Allowed)
            throw new UnauthorizedAccessException($"Windows location access returned {access}.");
        var locator = new Geolocator { DesiredAccuracyInMeters = 1000, MovementThreshold = 1000 };
        var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(15)).AsTask(cancellationToken);
        var point = position.Coordinate.Point.Position;
        var address = position.CivicAddress;
        var city = address?.City ?? "";
        var region = address?.State ?? "";
        var country = address?.Country ?? "";
        if (string.IsNullOrWhiteSpace(city))
            (city, region, country) = await ResolveUsPlaceAsync(point.Latitude, point.Longitude, cancellationToken);
        return new LocationResult(string.IsNullOrWhiteSpace(city) ? "Current location" : city, region, country, point.Latitude, point.Longitude);
    }

    private static async Task<(string City, string Region, string Country)> ResolveUsPlaceAsync(double latitude, double longitude, CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.NetworkUserAgent);
            using var response = await client.GetAsync($"https://api.weather.gov/points/{latitude:F4},{longitude:F4}", ct);
            if (!response.IsSuccessStatusCode) return ("", "", "");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var relative = json.RootElement.GetProperty("properties").GetProperty("relativeLocation").GetProperty("properties");
            return (relative.GetProperty("city").GetString() ?? "", relative.GetProperty("state").GetString() ?? "", "United States");
        }
        catch { return ("", "", ""); }
    }
}
