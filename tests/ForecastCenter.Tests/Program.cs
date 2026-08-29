using System.Net;
using System.Text;
using ForecastCenter.Models;
using ForecastCenter.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Open-Meteo forecast parsing", OpenMeteoParsing),
    ("NWS alert parsing and non-US filtering", NwsAlertParsing),
    ("Weather cache fallback", CacheFallback),
    ("Settings migration and per-location tide overrides", SettingsMigration)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception ex) { failures.Add($"{test.Name}: {ex.Message}"); Console.WriteLine($"FAIL  {test.Name}\n      {ex}"); }
}

Console.WriteLine($"\n{tests.Length - failures.Count}/{tests.Length} tests passed.");
if (failures.Count > 0) Environment.Exit(1);

static async Task OpenMeteoParsing()
{
    const string json = """
    {
      "current":{"time":"2026-08-28T12:15","temperature_2m":72.5,"relative_humidity_2m":50,"apparent_temperature":73.0,"is_day":1,"weather_code":1,"surface_pressure":1015.2,"wind_speed_10m":8.0,"wind_direction_10m":180,"wind_gusts_10m":14.0},
      "hourly":{"time":["2026-08-28T00:00","2026-08-28T12:00","2026-08-28T13:00","2026-08-28T14:00"],"temperature_2m":[60,72.5,74,75],"dew_point_2m":[20,61,54,55],"precipitation_probability":[0,10,20,30],"weather_code":[0,1,2,2],"visibility":[1000,16000,15000,14000],"surface_pressure":[1020,1015,1014,1014],"uv_index":[0,5,6,5]},
      "minutely_15":{"time":["2026-08-28T12:00","2026-08-28T12:15"],"precipitation":[0,0.1],"precipitation_probability":[10,20],"rain":[0,0.1],"snowfall":[0,0],"weather_code":[1,51]},
      "daily":{"time":["2026-08-28"],"temperature_2m_max":[78],"temperature_2m_min":[60],"weather_code":[45],"precipitation_probability_max":[30],"sunrise":["2026-08-28T06:10"],"sunset":["2026-08-28T19:28"]}
    }
    """;
    var service = new OpenMeteoService(new HttpClient(new StaticHandler(_ => Json(json))));
    var location = new LocationResult("Test City", "CT", "United States", 41.5, -72.0);
    var result = await service.GetWeatherAsync(location, false, CancellationToken.None);
    Equal(72.5, result.Current.Temperature, "current temperature");
    Near(53, result.Current.DewPoint, .2, "current calculated dew point");
    Near(16, result.Current.Visibility ?? -1, .01, "nearest-hour visibility");
    Near(5, result.Current.UvIndex ?? -1, .01, "nearest-hour UV index");
    Equal(3, result.Hourly.Count, "hour count");
    Equal(2, result.Minutely15.Count, "15-minute count");
    Equal(2, result.Daily[0].WeatherCode, "representative daytime weather code");
}

static async Task NwsAlertParsing()
{
    const string json = """{"features":[{"id":"alert-1","properties":{"event":"Flood Warning","headline":"Flood Warning issued","severity":"Severe","effective":"2026-08-28T12:00:00-04:00","expires":"2026-08-28T18:00:00-04:00","description":"Flooding is occurring.","instruction":"Move to higher ground."}}]}""";
    var handler = new StaticHandler(_ => Json(json));
    var service = new NwsAlertService(new HttpClient(handler));
    var alerts = await service.GetActiveAsync(new("Test City", "CT", "United States", 41.5, -72), CancellationToken.None);
    Equal(1, alerts.Count, "alert count");
    Equal("Flood Warning", alerts[0].Event, "alert event");
    var outsideUs = await service.GetActiveAsync(new("Toronto", "ON", "Canada", 43.65, -79.38), CancellationToken.None);
    Equal(0, outsideUs.Count, "non-US alert count");
    Equal(1, handler.RequestCount, "NWS request count");
}

static async Task CacheFallback()
{
    var folder = TempFolder();
    try
    {
        var location = new LocationResult("Cache City", "CT", "United States", 41.5, -72);
        var snapshot = Snapshot(location);
        var provider = new CachedWeatherProvider(new SequenceWeatherProvider(snapshot), folder);
        var first = await provider.GetWeatherAsync(location, false, CancellationToken.None);
        Equal(false, provider.LastResultWasCached, "first request cache flag");
        Equal(70d, first.Current.Temperature, "live result");
        var fallback = await provider.GetWeatherAsync(location, false, CancellationToken.None);
        Equal(true, provider.LastResultWasCached, "fallback cache flag");
        Equal(70d, fallback.Current.Temperature, "cached result");
    }
    finally { Directory.Delete(folder, true); }
}

static async Task SettingsMigration()
{
    var folder = TempFolder();
    try
    {
        var path = Path.Combine(folder, "settings.json");
        await File.WriteAllTextAsync(path, """{"TideStationId":"8461490","DefaultLocation":{"Name":"New London","AdminArea":"CT","Country":"United States","Latitude":41.355,"Longitude":-72.09},"SavedLocations":[],"TideStationOverrides":{}}""");
        var migrated = new SettingsService(path);
        Equal(null, migrated.Current.TideStationId, "legacy tide override cleared");
        Equal("8461490", migrated.Current.TideStationOverrides[migrated.Current.DefaultLocation.StorageKey], "legacy tide override migrated");

        var second = new LocationResult("Tampa", "FL", "United States", 27.95, -82.46);
        var overrides = new Dictionary<string, string>(migrated.Current.TideStationOverrides) { [second.StorageKey] = "8726520" };
        await migrated.SaveAsync(migrated.Current with { TideStationOverrides = overrides, NavigationTipDismissed = true });
        var reloaded = new SettingsService(path);
        Equal("8461490", reloaded.Current.TideStationOverrides[migrated.Current.DefaultLocation.StorageKey], "first location override");
        Equal("8726520", reloaded.Current.TideStationOverrides[second.StorageKey], "second location override");
        Equal(true, reloaded.Current.NavigationTipDismissed, "navigation tip persistence");
    }
    finally { Directory.Delete(folder, true); }
}

static WeatherSnapshot Snapshot(LocationResult location) => new(location,
    new(70, 70, 50, 50, 5, 180, null, 10, 1015, 4, 1, true, DateTime.Now),
    [new(DateTime.Now, 70, 1, 10, 50)],
    [new(DateTime.Today, 75, 60, 1, 10, DateTime.Today.AddHours(6), DateTime.Today.AddHours(19))]);

static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
static string TempFolder() { var path = Path.Combine(Path.GetTempPath(), "ForecastCenterTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
static void Equal<T>(T expected, T actual, string name) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}"); }
static void Near(double expected, double actual, double tolerance, string name) { if (Math.Abs(expected - actual) > tolerance) throw new InvalidOperationException($"{name}: expected {expected} ± {tolerance}, got {actual}"); }

sealed class StaticHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { RequestCount++; return Task.FromResult(response(request)); }
}

sealed class SequenceWeatherProvider(WeatherSnapshot snapshot) : IWeatherProvider
{
    private int _calls;
    public Task<WeatherSnapshot> GetWeatherAsync(LocationResult location, bool metric, CancellationToken cancellationToken) =>
        ++_calls == 1 ? Task.FromResult(snapshot) : Task.FromException<WeatherSnapshot>(new HttpRequestException("offline"));
}
