using ForecastCenter.Models;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace ForecastCenter.Services;

public sealed class WindowsLocationService
{
    // RPC_E_DISCONNECTED: on some Windows builds the consent broker fails RequestAccessAsync
    // this way for packaged desktop apps instead of showing the location prompt.
    private const int RpcDisconnected = unchecked((int)0x80010108);

    public async Task<LocationResult?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        GeolocationAccessStatus access;
        try { access = await Geolocator.RequestAccessAsync(); }
        catch (Exception ex) when (ex.HResult == RpcDisconnected) { access = GeolocationAccessStatus.Unspecified; }
        if (access == GeolocationAccessStatus.Denied) throw new LocationPermissionException();

        // Without a clear answer from the broker, ask anyway: access may already be on in Settings.
        var locator = new Geolocator { DesiredAccuracyInMeters = 1000, MovementThreshold = 1000 };
        Geoposition position;
        try { position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(15)).AsTask(cancellationToken); }
        catch (UnauthorizedAccessException) { throw new LocationPermissionException(); }
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

public sealed class LocationPermissionException() : UnauthorizedAccessException(
    "Forecast Center doesn't have permission to use your location. Turn it on in Windows location settings, then choose Automatic location again.");
