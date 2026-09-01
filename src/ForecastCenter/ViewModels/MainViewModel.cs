using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForecastCenter.Models;
using ForecastCenter.Services;

namespace ForecastCenter.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CachedWeatherProvider _weather;
    private readonly ILocationService _locations;
    private readonly IAlertService _alerts;
    private readonly IForecastNarrativeService _narratives;
    private readonly ISettingsService _settings;
    private readonly OpenMeteoService _openMeteo;
    private readonly OpenMeteoEnvironmentalService _environment;
    private readonly NoaaTideService _tides;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string locationName = "New York, New York";
    [ObservableProperty] private string temperature = "--°";
    [ObservableProperty] private string condition = "Loading weather…";
    [ObservableProperty] private string conditionGlyph = "·";
    [ObservableProperty] private string feelsLike = "Feels like --°";
    [ObservableProperty] private string highLow = "H: --°  L: --°";
    [ObservableProperty] private string forecastSummary = "Forecast details will appear after the first update.";
    [ObservableProperty] private Windows.UI.Color heroStartColor = Windows.UI.Color.FromArgb(255, 23, 58, 99);
    [ObservableProperty] private Windows.UI.Color heroEndColor = Windows.UI.Color.FromArgb(255, 36, 92, 145);
    [ObservableProperty] private Windows.UI.Color dashboardGlowColor = Windows.UI.Color.FromArgb(38, 36, 92, 145);
    [ObservableProperty] private string updated = "Not updated yet";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string humidity = "--";
    [ObservableProperty] private string dewPoint = "--";
    [ObservableProperty] private string wind = "--";
    [ObservableProperty] private string gust = "--";
    [ObservableProperty] private string visibility = "--";
    [ObservableProperty] private string pressure = "--";
    [ObservableProperty] private string uvIndex = "--";
    [ObservableProperty] private string precipitation = "--";
    [ObservableProperty] private string sunrise = "--";
    [ObservableProperty] private string sunset = "--";
    [ObservableProperty] private string sunTimes = "-- / --";
    [ObservableProperty] private string nearTermSummary = "Analyzing the next few hours...";
    [ObservableProperty] private string temperatureTrend = "Temperature trend unavailable";
    [ObservableProperty] private string comfortSummary = "Comfort information unavailable";
    [ObservableProperty] private Microsoft.UI.Xaml.Visibility nextHourVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    [ObservableProperty] private Microsoft.UI.Xaml.Visibility temperatureTrendVisibility = Microsoft.UI.Xaml.Visibility.Visible;
    [ObservableProperty] private bool radarSnowPossible;
    [ObservableProperty] private string nextHourSummary = "";
    [ObservableProperty] private Windows.UI.Color comfortTintColor = Windows.UI.Color.FromArgb(14, 87, 199, 174);
    [ObservableProperty] private Windows.UI.Color comfortAccentColor = Windows.UI.Color.FromArgb(255, 87, 199, 174);
    [ObservableProperty] private double comfortScaleValue = 55;
    [ObservableProperty] private string uvSummary = "UV information unavailable";
    [ObservableProperty] private Windows.UI.Color uvTintColor = Windows.UI.Color.FromArgb(14, 242, 184, 75);
    [ObservableProperty] private Windows.UI.Color uvAccentColor = Windows.UI.Color.FromArgb(255, 242, 184, 75);
    [ObservableProperty] private double uvScaleValue;
    [ObservableProperty] private string airQualitySummary = "Air-quality information unavailable";
    [ObservableProperty] private Windows.UI.Color airQualityTintColor = Windows.UI.Color.FromArgb(14, 87, 199, 174);
    [ObservableProperty] private Windows.UI.Color airQualityAccentColor = Windows.UI.Color.FromArgb(255, 87, 199, 174);
    [ObservableProperty] private double airQualityScaleValue;
    [ObservableProperty] private string daylightStatus = "Daylight information unavailable";
    [ObservableProperty] private double daylightProgress;
    [ObservableProperty] private string alertHeadline = "";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private LocationResult currentLocation;
    [ObservableProperty] private EnvironmentalSnapshot? environmentalData;
    [ObservableProperty] private string tideStationName = "Finding nearest tide station…";
    [ObservableProperty] private string nextHighTide = "—";
    [ObservableProperty] private string nextLowTide = "—";
    [ObservableProperty] private string tideDirectionSummary = "Tide status unavailable";
    [ObservableProperty] private string tideDirectionGlyph = "";
    [ObservableProperty] private string nextLowCountdown = "";
    [ObservableProperty] private string nextHighCountdown = "";
    [ObservableProperty] private string tideCatalogStatus = "Station catalog has not been checked yet";
    [ObservableProperty] private string tideCatalogAttentionText = "";
    [ObservableProperty] private Microsoft.UI.Xaml.Visibility tideCatalogAttentionVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    [ObservableProperty] private Microsoft.UI.Xaml.Visibility tideVisibility = Microsoft.UI.Xaml.Visibility.Visible;
    [ObservableProperty] private string moonPhaseName = "Calculating moon phase…";
    [ObservableProperty] private string moonPhaseGlyph = "🌑";
    [ObservableProperty] private string moonIllumination = "—";
    [ObservableProperty] private string moonAge = "—";
    [ObservableProperty] private string moonNextPhase = "—";
    [ObservableProperty] private string moonTodayDate = "—";
    [ObservableProperty] private string nextNewMoonDate = "—";
    [ObservableProperty] private string nextFullMoonDate = "—";
    private TideSnapshot? _latestTideSnapshot;

    public ObservableCollection<HourlyItem> Hours { get; } = [];
    public ObservableCollection<HourlyTrendPoint> HourlyTrend { get; } = [];
    public ObservableCollection<NextHourPrecipitationItem> NextHourPrecipitation { get; } = [];
    public ObservableCollection<DailyItem> Days { get; } = [];
    public ObservableCollection<LocationResult> SearchResults { get; } = [];
    public ObservableCollection<LocationResult> SavedLocations { get; } = [];
    public ObservableCollection<WeatherAlert> Alerts { get; } = [];
    public ObservableCollection<TideDisplayItem> TidePredictions { get; } = [];
    public ObservableCollection<TideStationChoice> TideStationChoices { get; } =
        [new(null, "Automatic — nearest station"), .. NoaaTideService.Stations.Select(station => new TideStationChoice(station.Id, station.DisplayName))];
    private bool _tideStationCatalogLoaded;
    public bool HasAlerts => Alerts.Count > 0;
    public bool StatusVisible => !string.IsNullOrWhiteSpace(Status);
    public bool Metric => _settings.Current.Metric;
    public int RefreshMinutes => Math.Clamp(_settings.Current.RefreshMinutes, 5, 60);
    public string WindUnit => _settings.Current.WindUnit;
    public string PressureUnit => _settings.Current.PressureUnit;
    public bool AutomaticLocation => _settings.Current.AutomaticLocation;
    public bool StartWithWindows => _settings.Current.StartWithWindows;
    public string Theme => _settings.Current.Theme;
    public bool MinimizeToTray => _settings.Current.MinimizeToTray;
    public bool UseGlobalTideStation => _settings.Current.UseGlobalTideStation;
    public string? TideStationId => UseGlobalTideStation
        ? _settings.Current.GlobalTideStationId
        : _settings.Current.TideStationOverrides.GetValueOrDefault(CurrentLocation.StorageKey);
    public bool IsCurrentLocationDefault => SameLocation(CurrentLocation, _settings.Current.DefaultLocation);
    public string TemperatureUnit => Metric ? "°C" : "°F";

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusVisible));

    public MainViewModel()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppIdentity.NetworkUserAgent);
        var openMeteo = new OpenMeteoService(http);
        _openMeteo = openMeteo;
        _environment = new OpenMeteoEnvironmentalService(http);
        _tides = new NoaaTideService(http);
        _weather = new CachedWeatherProvider(openMeteo);
        _locations = openMeteo;
        _alerts = new NwsAlertService(http);
        _narratives = new NwsForecastNarrativeService(http);
        _settings = new SettingsService();
        currentLocation = _settings.Current.DefaultLocation;
        foreach (var location in _settings.Current.SavedLocations) SavedLocations.Add(location);
        if (!SavedLocations.Any(x => SameLocation(x, currentLocation))) SavedLocations.Insert(0, currentLocation);
    }

    public Task<IReadOnlyList<NearbyTemperature>> GetNearbyTemperaturesAsync(CancellationToken ct = default) =>
        _openMeteo.GetNearbyTemperaturesAsync(CurrentLocation, Metric, ct);

    [RelayCommand]
    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new();
        IsBusy = true;
        Status = "Updating…";
        try
        {
            var snapshot = await _weather.GetWeatherAsync(CurrentLocation, Metric, _loadCts.Token);
            IReadOnlyDictionary<DateOnly, string> narratives = new Dictionary<DateOnly, string>();
            try { narratives = await _narratives.GetDailyNarrativesAsync(CurrentLocation, _loadCts.Token); } catch { }
            Apply(snapshot, narratives);
            try
            {
                EnvironmentalData = await _environment.GetAsync(CurrentLocation, _loadCts.Token);
                ApplyEnvironmentalSummary(EnvironmentalData);
            }
            catch { EnvironmentalData = null; }
            try
            {
                await LoadTideStationCatalogAsync(_loadCts.Token);
                var nearTides = TideStationId is not null || await _tides.IsNearStationAsync(CurrentLocation, 180, _loadCts.Token);
                TideVisibility = nearTides ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
                if (nearTides) ApplyTides(await _tides.GetAsync(CurrentLocation, TideStationId, _loadCts.Token));
                else { _latestTideSnapshot = null; TidePredictions.Clear(); TideStationName = "No nearby tide station"; }
            }
            catch { TideVisibility = Microsoft.UI.Xaml.Visibility.Collapsed; TideStationName = "Tide predictions unavailable"; TidePredictions.Clear(); }
            if (_weather.LastResultWasCached) Status = "You’re offline. Showing the most recently saved forecast.";
            try
            {
                Replace(Alerts, await _alerts.GetActiveAsync(CurrentLocation, _loadCts.Token));
                AlertHeadline = Alerts.FirstOrDefault() is { } alert ? $"{alert.Event}: {alert.Headline}" : "";
                OnPropertyChanged(nameof(HasAlerts));
            }
            catch { Status = "Forecast updated; alerts are temporarily unavailable."; }
            if (Status == "Updating…") Status = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = $"Couldn’t refresh. {Friendly(ex)}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (SearchText.Trim().Length < 2) return;
        try { IsBusy = true; Replace(SearchResults, await _locations.SearchAsync(SearchText.Trim(), CancellationToken.None)); }
        catch (Exception ex) { Status = $"Location search failed. {Friendly(ex)}"; }
        finally { IsBusy = false; }
    }

    public async Task SelectLocationAsync(LocationResult location)
    {
        CurrentLocation = location;
        LocationName = location.PickerDisplayName;
        OnPropertyChanged(nameof(TideStationId));
        SearchResults.Clear();
        SearchText = "";
        OnPropertyChanged(nameof(IsCurrentLocationDefault));
        await LoadAsync();
    }

    public async Task SetDefaultLocationAsync()
    {
        await _settings.SaveAsync(_settings.Current with { DefaultLocation = CurrentLocation });
        OnPropertyChanged(nameof(IsCurrentLocationDefault));
    }

    public async Task SetMinimizeToTrayAsync(bool enabled)
    {
        await _settings.SaveAsync(_settings.Current with { MinimizeToTray = enabled });
        OnPropertyChanged(nameof(MinimizeToTray));
    }

    public async Task SetMeasurementPreferencesAsync(string windUnit, string pressureUnit)
    {
        await _settings.SaveAsync(_settings.Current with { WindUnit = windUnit, PressureUnit = pressureUnit });
        OnPropertyChanged(nameof(WindUnit)); OnPropertyChanged(nameof(PressureUnit));
        await LoadAsync();
    }

    public async Task SetRefreshMinutesAsync(int minutes)
    {
        await _settings.SaveAsync(_settings.Current with { RefreshMinutes = Math.Clamp(minutes, 5, 60) });
        OnPropertyChanged(nameof(RefreshMinutes));
    }

    public async Task SetAutomaticLocationAsync(bool enabled)
    {
        await _settings.SaveAsync(_settings.Current with { AutomaticLocation = enabled });
        OnPropertyChanged(nameof(AutomaticLocation));
    }

    public async Task SetStartWithWindowsAsync(bool enabled)
    {
        await _settings.SaveAsync(_settings.Current with { StartWithWindows = enabled });
        OnPropertyChanged(nameof(StartWithWindows));
    }

    public async Task SetTideStationAsync(string? stationId)
    {
        if (UseGlobalTideStation)
        {
            await _settings.SaveAsync(_settings.Current with { GlobalTideStationId = stationId });
        }
        else
        {
            var overrides = new Dictionary<string, string>(_settings.Current.TideStationOverrides);
            if (stationId is null) overrides.Remove(CurrentLocation.StorageKey);
            else overrides[CurrentLocation.StorageKey] = stationId;
            await _settings.SaveAsync(_settings.Current with { TideStationOverrides = overrides });
        }
        OnPropertyChanged(nameof(TideStationId));
        try
        {
            var visible = stationId is not null || await _tides.IsNearStationAsync(CurrentLocation, 180, CancellationToken.None);
            TideVisibility = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            if (visible) ApplyTides(await _tides.GetAsync(CurrentLocation, stationId, CancellationToken.None)); else TidePredictions.Clear();
        }
        catch { TideVisibility = Microsoft.UI.Xaml.Visibility.Collapsed; TideStationName = "Tide predictions unavailable"; TidePredictions.Clear(); }
    }

    public async Task SetUseGlobalTideStationAsync(bool enabled)
    {
        if (enabled == UseGlobalTideStation) return;
        var effectiveStation = TideStationId;
        var overrides = new Dictionary<string, string>(_settings.Current.TideStationOverrides);
        if (!enabled && !string.IsNullOrWhiteSpace(_settings.Current.GlobalTideStationId))
            overrides[CurrentLocation.StorageKey] = _settings.Current.GlobalTideStationId;
        await _settings.SaveAsync(_settings.Current with
        {
            UseGlobalTideStation = enabled,
            GlobalTideStationId = enabled ? effectiveStation : _settings.Current.GlobalTideStationId,
            TideStationOverrides = overrides
        });
        OnPropertyChanged(nameof(UseGlobalTideStation));
        OnPropertyChanged(nameof(TideStationId));
        try
        {
            var visible = TideStationId is not null || await _tides.IsNearStationAsync(CurrentLocation, 180, CancellationToken.None);
            TideVisibility = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            if (visible) ApplyTides(await _tides.GetAsync(CurrentLocation, TideStationId, CancellationToken.None)); else TidePredictions.Clear();
        }
        catch { TideVisibility = Microsoft.UI.Xaml.Visibility.Collapsed; TideStationName = "Tide predictions unavailable"; TidePredictions.Clear(); }
    }

    private async Task LoadTideStationCatalogAsync(CancellationToken ct)
    {
        if (_tideStationCatalogLoaded) return;
        var stations = await _tides.GetStationsAsync(ct);
        ApplyTideStationChoices(stations);
        ApplyTideCatalogStatus();
        _tideStationCatalogLoaded = true;
    }

    public async Task RefreshTideStationCatalogAsync(CancellationToken ct = default)
    {
        var stations = await _tides.RefreshStationsAsync(ct);
        ApplyTideStationChoices(stations);
        ApplyTideCatalogStatus();
        ApplyTides(await _tides.GetAsync(CurrentLocation, TideStationId, ct));
    }

    private void ApplyTideStationChoices(IReadOnlyList<TideStation> stations)
    {
        TideStationChoices.Clear();
        TideStationChoices.Add(new(null, "Automatic — nearest station"));
        foreach (var station in stations.OrderBy(item => item.State).ThenBy(item => item.Name))
            TideStationChoices.Add(new(station.Id, station.DisplayName));
    }

    private void ApplyTideCatalogStatus()
    {
        var status = _tides.CatalogStatus;
        var updated = status.UpdatedAt is { } date ? date.LocalDateTime.ToString("MMM d, yyyy h:mm tt") : "not available";
        TideCatalogStatus = status.RefreshFailed
            ? $"Refresh failed · Using {status.StationCount:N0} saved stations · Last updated {updated}"
            : $"{status.StationCount:N0} NOAA stations · Updated {updated}";
        TideCatalogAttentionText = status.RefreshFailed ? "Station data needs attention" : "";
        TideCatalogAttentionVisibility = status.RefreshFailed ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void ApplyTides(TideSnapshot snapshot)
    {
        _latestTideSnapshot = snapshot;
        TideStationChoices[0].DisplayName = TideStationId is null
            ? $"Automatic — {snapshot.Station.DisplayName}"
            : "Automatic — nearest station";
        TideStationName = $"{snapshot.Station.DisplayName} · NOAA station {snapshot.Station.Id}";
        var future = snapshot.Predictions.Where(item => item.Time >= DateTime.Now).ToList();
        var high = future.FirstOrDefault(item => item.IsHigh);
        var low = future.FirstOrDefault(item => !item.IsHigh);
        NextHighTide = high is null ? "—" : $"{high.Time:h:mm tt} · {high.Height:0.0} ft";
        NextLowTide = low is null ? "—" : $"{low.Time:h:mm tt} · {low.Height:0.0} ft";
        Replace(TidePredictions, future.Take(6).Select(item => new TideDisplayItem(item.IsHigh ? "HIGH" : "LOW", item.Time.ToString(item.Time.Date == DateTime.Today ? "h:mm tt" : "ddd h:mm tt"), $"{item.Height:0.0} ft", item.IsHigh ? "▲" : "▼")));
        UpdateTideStatus();
    }

    public void UpdateTideStatus()
    {
        if (_latestTideSnapshot is null) return;
        var future = _latestTideSnapshot.Predictions.Where(item => item.Time >= DateTime.Now).ToList();
        var next = future.FirstOrDefault();
        var low = future.FirstOrDefault(item => !item.IsHigh);
        var high = future.FirstOrDefault(item => item.IsHigh);
        TideDirectionSummary = next is null ? "Tide status unavailable" : next.IsHigh ? "The tide is currently rising" : "The tide is currently falling";
        TideDirectionGlyph = next is null ? "" : next.IsHigh ? "↑" : "↓";
        NextLowCountdown = low is null ? "No low tide is available in this forecast" : $"The next low tide is in {FriendlyDuration(low.Time - DateTime.Now)}";
        NextHighCountdown = high is null ? "No high tide is available in this forecast" : $"The next high tide is in {FriendlyDuration(high.Time - DateTime.Now)}";
    }

    private static string FriendlyDuration(TimeSpan duration)
    {
        var totalMinutes = Math.Max(0, (int)Math.Ceiling(duration.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0) return $"{minutes} {(minutes == 1 ? "minute" : "minutes")}";
        if (minutes == 0) return $"{hours} {(hours == 1 ? "hour" : "hours")}";
        return $"{hours} {(hours == 1 ? "hour" : "hours")} and {minutes} {(minutes == 1 ? "minute" : "minutes")}";
    }

    public async Task SaveCurrentLocationAsync()
    {
        if (SavedLocations.Any(x => SameLocation(x, CurrentLocation))) return;
        SavedLocations.Add(CurrentLocation);
        await _settings.SaveAsync(_settings.Current with { SavedLocations = SavedLocations.ToList() });
    }

    public async Task RemoveSavedLocationAsync(string storageKey)
    {
        var location = SavedLocations.FirstOrDefault(x => x.StorageKey == storageKey);
        if (location is null) return;
        SavedLocations.Remove(location);
        await _settings.SaveAsync(_settings.Current with { SavedLocations = SavedLocations.ToList() });
    }

    public async Task SetMetricAsync(bool metric)
    {
        await _settings.SaveAsync(_settings.Current with { Metric = metric });
        OnPropertyChanged(nameof(Metric)); OnPropertyChanged(nameof(TemperatureUnit));
        await LoadAsync();
    }

    public async Task SetThemeAsync(string theme)
    {
        await _settings.SaveAsync(_settings.Current with { Theme = theme });
        OnPropertyChanged(nameof(Theme));
    }

    private void Apply(WeatherSnapshot s, IReadOnlyDictionary<DateOnly, string> narratives)
    {
        var degree = "°";
        // The active location is authoritative. A cached response from a prior
        // request must never put an old/default label back in the title.
        LocationName = CurrentLocation.PickerDisplayName;
        Temperature = $"{s.Current.Temperature:0}{degree}";
        Condition = WeatherCode.Description(s.Current.WeatherCode);
        ConditionGlyph = WeatherCode.Glyph(s.Current.WeatherCode, s.Current.IsDay);
        FeelsLike = $"Feels like {s.Current.FeelsLike:0}{degree}";
        HighLow = s.Daily.Count > 0 ? $"H: {s.Daily[0].High:0}{degree}   L: {s.Daily[0].Low:0}{degree}" : "";
        var fallbackSummary = s.Daily.Count > 0
            ? $"{WeatherCode.Description(s.Daily[0].WeatherCode)} today. High near {s.Daily[0].High:0}{degree}, with a {s.Daily[0].PrecipitationProbability}% chance of precipitation."
            : Condition;
        ForecastSummary = s.Daily.Count > 0 && narratives.TryGetValue(DateOnly.FromDateTime(s.Daily[0].Date), out var todayNarrative) ? todayNarrative : fallbackSummary;
        ApplyHeroPalette(s.Current.WeatherCode, s.Current.IsDay);
        Updated = $"Updated {s.Current.UpdatedAt:t}";
        Humidity = $"{s.Current.Humidity:0}%";
        DewPoint = $"{s.Current.DewPoint:0}{degree}";
        var (windFactor, windLabel) = WindConversion();
        Wind = $"{Compass(s.Current.WindDirection)} {s.Current.WindSpeed * windFactor:0} {windLabel}";
        Gust = s.Current.WindGust is null ? "—" : $"{s.Current.WindGust * windFactor:0} {windLabel}";
        Visibility = s.Current.Visibility is null ? "—" : $"{(Metric ? s.Current.Visibility : s.Current.Visibility * .621371):0.#} {(Metric ? "km" : "mi")}";
        Pressure = FormatPressure(s.Current.Pressure);
        UvIndex = s.Current.UvIndex?.ToString("0.#") ?? "—";
        Precipitation = s.Hourly.Count > 0 ? $"{s.Hourly[0].PrecipitationProbability}%" : "—";
        Sunrise = s.Daily.Count > 0 ? s.Daily[0].Sunrise.ToString("t") : "—";
        Sunset = s.Daily.Count > 0 ? s.Daily[0].Sunset.ToString("t") : "—";
        SunTimes = $"{Sunrise} / {Sunset}";
        ApplyMoonPhase();
        ApplyCommandCenterSummaries(s);
        ApplyNextHourPrecipitation(s);
        RadarSnowPossible = s.Hourly.Take(48).Any(x =>
            x.WeatherCode is 56 or 57 or 66 or 67 or 71 or 73 or 75 or 77 or 85 or 86 ||
            x.Temperature <= (Metric ? 3 : 37));
        Replace(HourlyTrend, s.Hourly.Select(x => new HourlyTrendPoint(x.Time, x.DewPoint ?? x.Temperature, x.PrecipitationProbability, x.WeatherCode)));
        var forecastDate = s.Current.UpdatedAt.Date;
        Replace(Hours, s.Hourly.Select(x => new HourlyItem(x.Time, x.Time.ToString(x.Time.Date == forecastDate ? "h tt" : "ddd h tt"), WeatherCode.Glyph(x.WeatherCode), WeatherCode.Description(x.WeatherCode), $"{x.Temperature:0}{degree}", $"{x.PrecipitationProbability}%", x.DewPoint is double dewPoint ? $"{dewPoint:0}{degree}" : "—", ForecastTint(x.WeatherCode, x.Time.Hour is >= 7 and < 19))));
        Replace(Days, s.Daily.Select((x, i) => new DailyItem(
            i == 0 ? "Today" : x.Date.ToString("ddd"), x.Date.ToString("dddd, MMMM d"),
            WeatherCode.Glyph(x.WeatherCode), WeatherCode.Description(x.WeatherCode),
            $"{x.High:0}{degree}", $"{x.Low:0}{degree}", $"{x.PrecipitationProbability}%", ForecastTint(x.WeatherCode, true),
            x.Sunrise.ToString("t"), x.Sunset.ToString("t"),
            narratives.TryGetValue(DateOnly.FromDateTime(x.Date), out var narrative) ? narrative : $"{WeatherCode.Description(x.WeatherCode)} with a high near {x.High:0}{degree} and a low near {x.Low:0}{degree}. The chance of precipitation is {x.PrecipitationProbability}%.")));
    }

    private void ApplyMoonPhase()
    {
        const double lunarCycleDays = 29.530588853;
        var epoch = new DateTimeOffset(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
        var elapsedDays = (DateTimeOffset.UtcNow - epoch).TotalDays;
        var phase = ((elapsedDays / lunarCycleDays) % 1 + 1) % 1;
        var age = phase * lunarCycleDays;
        var illumination = (1 - Math.Cos(phase * Math.PI * 2)) / 2;
        var phaseIndex = (int)Math.Round(phase * 8) % 8;
        string[] names = ["New Moon", "Waxing Crescent", "First Quarter", "Waxing Gibbous", "Full Moon", "Waning Gibbous", "Last Quarter", "Waning Crescent"];
        string[] glyphs = ["🌑", "🌒", "🌓", "🌔", "🌕", "🌖", "🌗", "🌘"];

        MoonPhaseName = names[phaseIndex];
        MoonPhaseGlyph = glyphs[phaseIndex];
        MoonIllumination = $"{illumination * 100:0}% illuminated";
        MoonAge = $"Lunar day {age:0.0} of 29.5";
        MoonTodayDate = DateTime.Now.ToString("ddd, MMM d");

        var daysToNewMoon = (1 - phase) * lunarCycleDays;
        if (daysToNewMoon < .1) daysToNewMoon = lunarCycleDays;
        var daysToFullMoon = (phase < .5 ? .5 - phase : 1.5 - phase) * lunarCycleDays;
        NextNewMoonDate = DateTime.Now.AddDays(daysToNewMoon).ToString("ddd, MMM d");
        NextFullMoonDate = DateTime.Now.AddDays(daysToFullMoon).ToString("ddd, MMM d");

        var milestones = new (double Phase, string Name)[]
        {
            (.25, "First Quarter"), (.5, "Full Moon"), (.75, "Last Quarter"), (1, "New Moon")
        };
        var next = milestones.First(item => item.Phase > phase);
        var until = TimeSpan.FromDays((next.Phase - phase) * lunarCycleDays);
        MoonNextPhase = $"{next.Name} in {FormatMoonDuration(until)}";
    }

    private static string FormatMoonDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 2) return $"{Math.Round(duration.TotalDays):0} days";
        if (duration.TotalDays >= 1) return $"1 day, {duration.Hours} hr";
        return $"{Math.Max(1, (int)Math.Round(duration.TotalHours))} hr";
    }

    private void ApplyNextHourPrecipitation(WeatherSnapshot snapshot)
    {
        var periods = Environment.GetEnvironmentVariable("FORECASTCENTER_DEMO_PRECIP") == "1"
            ? DemoNextHourPrecipitation()
            : snapshot.Minutely15
                .Where(x => x.Time >= DateTime.Now.AddMinutes(-15))
                .Take(4)
                .ToList();
        var wet = periods.Where(IsPlausiblePrecipitation).ToList();
        if (periods.Count == 0 || wet.Count == 0)
        {
            NextHourVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            TemperatureTrendVisibility = Microsoft.UI.Xaml.Visibility.Visible;
            NextHourPrecipitation.Clear();
            return;
        }

        var first = wet[0];
        var kind = PrecipitationKind(first);
        var firstIndex = periods.IndexOf(first);
        NextHourSummary = firstIndex <= 0
            ? $"{kind} possible now and during the next hour"
            : $"{kind} may begin in about {firstIndex * 15} minutes";

        Replace(NextHourPrecipitation, periods.Select(x =>
        {
            var probability = Math.Clamp(x.PrecipitationProbability, 0, 100);
            var color = PrecipitationColor(x);
            return new NextHourPrecipitationItem(x.Time.ToString("h:mm"), $"{probability}%", 6 + probability * .34, color);
        }));
        NextHourVisibility = Microsoft.UI.Xaml.Visibility.Visible;
        TemperatureTrendVisibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private static List<MinuteWeather> DemoNextHourPrecipitation()
    {
        var start = DateTime.Now.AddMinutes(15 - DateTime.Now.Minute % 15);
        return
        [
            new(start, 0, 0, 0, 3, 12),
            new(start.AddMinutes(15), .02, .02, 0, 61, 42),
            new(start.AddMinutes(30), .08, .08, 0, 63, 76),
            new(start.AddMinutes(45), .04, .04, 0, 61, 58)
        ];
    }

    private static bool IsPlausiblePrecipitation(MinuteWeather item) => item.PrecipitationProbability >= 25 || item.Precipitation > 0.001 || item.Snowfall > 0.001;

    private static string PrecipitationKind(MinuteWeather item) => item.WeatherCode switch
    {
        >= 95 => "Thunderstorms",
        71 or 73 or 75 or 77 or 85 or 86 => "Snow",
        56 or 57 or 66 or 67 => "Freezing precipitation",
        51 or 53 or 55 => "Drizzle",
        _ => item.Snowfall > 0 ? "Snow" : "Rain"
    };

    private static Windows.UI.Color PrecipitationColor(MinuteWeather item) => item.WeatherCode switch
    {
        >= 95 => ParseColor("#FB923C"),
        71 or 73 or 75 or 77 or 85 or 86 => ParseColor("#A78BFA"),
        56 or 57 or 66 or 67 => ParseColor("#67E8F9"),
        _ => ParseColor("#3399FF")
    };

    private void ApplyCommandCenterSummaries(WeatherSnapshot snapshot)
    {
        ApplyComfortSummary(snapshot.Current);
        var upcoming = snapshot.Hourly.Where(hour => hour.Time >= DateTime.Now.AddMinutes(-30)).Take(12).ToList();
        var likely = upcoming.FirstOrDefault(hour => hour.PrecipitationProbability >= 50);
        var possible = upcoming.FirstOrDefault(hour => hour.PrecipitationProbability >= 25);
        NearTermSummary = likely is not null
            ? $"Precipitation likely around {likely.Time:h tt} ({likely.PrecipitationProbability}%)"
            : possible is not null
                ? $"A chance of precipitation around {possible.Time:h tt} ({possible.PrecipitationProbability}%)"
                : "Dry conditions expected over the next 12 hours";

        if (upcoming.Count >= 2)
        {
            var comparison = upcoming[Math.Min(6, upcoming.Count - 1)];
            var change = comparison.Temperature - upcoming[0].Temperature;
            TemperatureTrend = Math.Abs(change) < 1
                ? $"Holding nearly steady through {comparison.Time:h tt}"
                : change > 0
                    ? $"About {Math.Abs(change):0}{TemperatureUnit} warmer by {comparison.Time:h tt}"
                    : $"About {Math.Abs(change):0}{TemperatureUnit} cooler by {comparison.Time:h tt}";
        }

        if (snapshot.Daily.Count == 0) return;
        var sunriseTime = snapshot.Daily[0].Sunrise;
        var sunsetTime = snapshot.Daily[0].Sunset;
        var now = DateTime.Now;
        if (now < sunriseTime)
        {
            DaylightProgress = 0;
            DaylightStatus = $"Sunrise at {sunriseTime:t}";
        }
        else if (now >= sunsetTime)
        {
            DaylightProgress = 100;
            DaylightStatus = $"Sunset was at {sunsetTime:t}";
        }
        else
        {
            DaylightProgress = Math.Clamp((now - sunriseTime).TotalMinutes / (sunsetTime - sunriseTime).TotalMinutes * 100, 0, 100);
            DaylightStatus = $"{DaylightProgress:0}% elapsed · Sunset {sunsetTime:t}";
        }
    }

    private void ApplyComfortSummary(CurrentWeather current)
    {
        var dewPointF = Metric ? current.DewPoint * 9d / 5d + 32d : current.DewPoint;
        var (description, accent) = dewPointF switch
        {
            < 40 => ("Very dry", "#70AEEF"),
            < 50 => ("Dry and crisp", "#68C7B7"),
            < 60 => ("Comfortable", "#57C7AE"),
            < 63 => ("Comfortable, slightly humid", "#8FC77A"),
            < 68 => ("Comfortable, somewhat humid", "#D2B85B"),
            < 72 => ("Humid", "#E5A24F"),
            < 75 => ("Very humid", "#E8895E"),
            _ => ("Oppressive humidity", "#E66E62")
        };

        ComfortSummary = $"{description} · Dew point {DewPoint}, humidity {Humidity}";
        ComfortScaleValue = dewPointF;
        ComfortAccentColor = ParseColor(accent);
        ComfortTintColor = Windows.UI.Color.FromArgb(14, ComfortAccentColor.R, ComfortAccentColor.G, ComfortAccentColor.B);
    }

    private void ApplyEnvironmentalSummary(EnvironmentalSnapshot data)
    {
        var uvCategory = data.UvIndex switch { < 3 => "Low", < 6 => "Moderate", < 8 => "High", < 11 => "Very high", _ => "Extreme" };
        var uvColor = data.UvIndex switch { < 3 => "#57C7AE", < 6 => "#F2B84B", < 8 => "#F29A4A", < 11 => "#E66E62", _ => "#B678D1" };
        UvSummary = data.PeakUvTime is { } peakTime
            ? $"{data.UvIndex:0.#} {uvCategory} · Peak {data.PeakUvIndex:0.#} around {peakTime:h tt}"
            : $"{data.UvIndex:0.#} {uvCategory}";
        UvAccentColor = ParseColor(uvColor);
        UvScaleValue = data.UvIndex;
        UvTintColor = Windows.UI.Color.FromArgb(14, UvAccentColor.R, UvAccentColor.G, UvAccentColor.B);

        var aqiCategory = data.UsAqi switch { <= 50 => "Good", <= 100 => "Moderate", <= 150 => "Unhealthy for sensitive groups", <= 200 => "Unhealthy", <= 300 => "Very unhealthy", _ => "Hazardous" };
        var aqiColor = data.UsAqi switch { <= 50 => "#57C7AE", <= 100 => "#D2B85B", <= 150 => "#E5A24F", <= 200 => "#E66E62", <= 300 => "#B678D1", _ => "#9A5870" };
        AirQualitySummary = $"AQI {data.UsAqi} {aqiCategory} · Mainly {data.DominantPollutant}";
        AirQualityScaleValue = data.UsAqi;
        AirQualityAccentColor = ParseColor(aqiColor);
        AirQualityTintColor = Windows.UI.Color.FromArgb(14, AirQualityAccentColor.R, AirQualityAccentColor.G, AirQualityAccentColor.B);
    }

    private void ApplyHeroPalette(int code, bool isDay)
    {
        (string Start, string End) palette = !isDay ? ("#111C3E", "#253B6E") : code switch
        {
            0 or 1 => ("#1478B8", "#35A5D7"),
            2 or 3 => ("#334A63", "#58758E"),
            45 or 48 => ("#485B66", "#71848D"),
            >= 51 and <= 67 or >= 80 and <= 82 => ("#193B5C", "#29627C"),
            >= 71 and <= 77 or 85 or 86 => ("#537A99", "#89AABE"),
            >= 95 => ("#241B4B", "#4B376D"),
            _ => ("#173A63", "#245C91")
        };
        HeroStartColor = ParseColor(palette.Start);
        HeroEndColor = ParseColor(palette.End);
        DashboardGlowColor = Windows.UI.Color.FromArgb(38, HeroEndColor.R, HeroEndColor.G, HeroEndColor.B);
    }

    private static Windows.UI.Color ParseColor(string hex) => Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    private static Windows.UI.Color ForecastTint(int code, bool isDay)
    {
        var (r, g, b) = !isDay ? (55, 78, 145) : code switch
        {
            0 or 1 => (43, 158, 218),
            2 or 3 => (104, 137, 163),
            45 or 48 => (111, 139, 145),
            >= 51 and <= 67 or >= 80 and <= 82 => (43, 128, 180),
            >= 71 and <= 77 or 85 or 86 => (118, 174, 205),
            >= 95 => (119, 87, 173),
            _ => (67, 137, 196)
        };
        return Windows.UI.Color.FromArgb(25, (byte)r, (byte)g, (byte)b);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var item in values) target.Add(item); }
    private static bool SameLocation(LocationResult a, LocationResult b) => Math.Abs(a.Latitude - b.Latitude) < .001 && Math.Abs(a.Longitude - b.Longitude) < .001;
    private (double Factor, string Label) WindConversion()
    {
        var sourceIsKmh = Metric;
        var unit = WindUnit.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? (Metric ? "km/h" : "mph") : WindUnit;
        return unit switch { "km/h" => (sourceIsKmh ? 1 : 1.609344, "km/h"), "kn" => (sourceIsKmh ? 0.539957 : 0.868976, "kn"), _ => (sourceIsKmh ? 0.621371 : 1, "mph") };
    }

    private string FormatPressure(double hPa)
    {
        var unit = PressureUnit.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? (Metric ? "hPa" : "inHg") : PressureUnit;
        return unit switch { "inHg" => $"{hPa * 0.0295299831:0.00} inHg", "mmHg" => $"{hPa * 0.750061683:0} mmHg", _ => $"{hPa:0} hPa" };
    }

    private static string Compass(double degrees) { string[] d = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]; return d[(int)Math.Round(degrees / 45) % 8]; }
    private static string Friendly(Exception ex) => ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ? "The provider is rate-limiting requests; try again shortly." : ex.Message;
}

public sealed record HourlyItem(DateTime Timestamp, string Time, string Glyph, string Condition, string Temperature, string Precipitation, string DewPoint, Windows.UI.Color TintColor);
public sealed record HourlyTrendPoint(DateTime Time, double DewPoint, int PrecipitationProbability, int WeatherCode);
public sealed record NextHourPrecipitationItem(string Time, string Probability, double CompactBarHeight, Windows.UI.Color Color);
public sealed record TideDisplayItem(string Type, string Time, string Height, string Glyph);
public sealed partial class TideStationChoice : ObservableObject
{
    public string? Id { get; }
    [ObservableProperty] private string displayName;

    public TideStationChoice(string? id, string displayName)
    {
        Id = id;
        this.displayName = displayName;
    }
}
public sealed record DailyItem(string Day, string FullDate, string Glyph, string Condition, string High, string Low, string Precipitation, Windows.UI.Color TintColor, string Sunrise, string Sunset, string Summary);
