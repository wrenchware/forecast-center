using System.Xml.Linq;

namespace ForecastCenter.Services;

public sealed class NdfdPrecipitationService
{
    private const string CapabilitiesUrl = "https://digital.weather.gov/ndfd.conus/wms?SERVICE=WMS&REQUEST=GetCapabilities";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private IReadOnlyList<DateTimeOffset> _cached = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public NdfdPrecipitationService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.NetworkUserAgent);

    public async Task<IReadOnlyList<DateTimeOffset>> GetValidTimesAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && _cached.Count > 0 && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromMinutes(30)) return _cached;
        var xml = await _http.GetStringAsync(CapabilitiesUrl, cancellationToken);
        var document = XDocument.Parse(xml);
        var layer = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Layer" &&
            element.Elements().Any(child => child.Name.LocalName == "Name" && child.Value == "ndfd.conus.wx"));
        var dimension = layer?.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Dimension" && (string?)element.Attribute("name") == "vtit");
        var now = DateTimeOffset.UtcNow;
        var through = now.AddHours(6.5);
        _cached = (dimension?.Value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => DateTimeOffset.TryParse(value + "Z", out var time) ? time : (DateTimeOffset?)null)
            .Where(time => time is not null && time >= now.AddMinutes(-10) && time <= through)
            .Select(time => time!.Value)
            .Take(7)
            .ToList();
        _cachedAt = DateTimeOffset.UtcNow;
        return _cached;
    }
}
