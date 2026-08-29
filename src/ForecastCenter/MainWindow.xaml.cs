using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using ForecastCenter.Models;
using ForecastCenter.Services;
using ForecastCenter.ViewModels;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace ForecastCenter;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    public bool StatusVisible => !string.IsNullOrWhiteSpace(ViewModel.Status);
    private bool _radarReady;
    private bool _initialized;
    private readonly HrrrRadarService _hrrrRadarService = new();
    private readonly NoaaObservedRadarService _observedRadarService = new();
    private readonly NdfdPrecipitationService _ndfdPrecipitationService = new();
    private readonly Dictionary<string, RadarTemperatureCache> _radarTemperatureCache = [];
    private readonly Dictionary<string, Task<IReadOnlyList<NearbyTemperature>>> _radarTemperatureRequests = [];
    private bool _radarTemperatureCacheLoaded;
    private readonly string _radarTemperatureCachePath = Path.Combine(AppIdentity.DataRoot, "radar-temperatures.json");
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueueTimer _resizeTimer;
    private readonly DispatcherQueueTimer _windowStateTimer;
    private readonly DispatcherQueueTimer _weatherRefreshTimer;
    private readonly DispatcherQueueTimer _tideStatusTimer;
    private readonly DispatcherQueueTimer _sidebarAutoHideTimer;
    private DateTimeOffset _lastWeatherRefresh = DateTimeOffset.MinValue;
    private double _pendingDashboardWidth;
    private int _visibleHourlyCards = 1;
    private RadioButtons? _themeRadio;
    private ComboBox? _tideStationPicker;
    private CheckBox? _minimizeToTrayCheckBox;
    private bool _updatingLocationPicker;
    private CheckBox? _startWithWindowsCheckBox;
    private ComboBox? _windUnitPicker;
    private ComboBox? _pressureUnitPicker;
    private ComboBox? _refreshFrequencyPicker;
    private readonly WindowsLocationService _windowsLocationService = new();
    private CheckBox? _globalTideStationCheckBox;
    private FrameworkElement? _downloadedDataSettingsSection;
    private TextBlock? _weatherCacheStatusText;
    private TextBlock? _radarCacheStatusText;
    private TextBlock? _tideCacheStatusText;
    private bool _updatingTideStationSelection;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private readonly AppWindow _appWindow;
    private Windows.Graphics.SizeInt32 _lastRestoredWindowSize;
    private readonly string _windowStatePath = Path.Combine(AppIdentity.DataRoot, "window-state.json");
    private Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? _webViewEnvironmentTask;
    private ToolTip? _activeForecastToolTip;
    private bool _pointerInsideNavigationPane;
    private bool _updatingSidebarPin;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TideStationName))
                DispatcherQueue.TryEnqueue(SyncTideStationPickers);
        };
        NormalizeCommandCardTints();
        SetRadarAttributionOpacity(0);
        StartLoadingShimmer(RadarLoadingShimmer);
        StartLoadingShimmer(DashboardSkeletonShimmer);
        ElementCompositionPreview.GetElementVisual(WeatherPage).Opacity = 0;
        // Keep startup focus off the location picker without removing its normal
        // keyboard focus visual when the user tabs to it later.
        WeatherPage.Loaded += (_, _) =>
        {
            WeatherPage.Focus(FocusState.Programmatic);
        };
        InitializeThemeSettings();
        ApplyTheme(ViewModel.Theme);
        _resizeTimer = DispatcherQueue.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(90);
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick += (_, _) => UpdateForecastLayout(_pendingDashboardWidth);
        _windowStateTimer = DispatcherQueue.CreateTimer();
        _windowStateTimer.Interval = TimeSpan.FromMilliseconds(500);
        _windowStateTimer.IsRepeating = false;
        _windowStateTimer.Tick += (_, _) => SaveWindowState();
        _weatherRefreshTimer = DispatcherQueue.CreateTimer();
        _weatherRefreshTimer.Interval = TimeSpan.FromMinutes(ViewModel.RefreshMinutes);
        _weatherRefreshTimer.IsRepeating = true;
        _weatherRefreshTimer.Tick += async (_, _) => await RefreshWeatherAndRadarAsync();
        _tideStatusTimer = DispatcherQueue.CreateTimer();
        _tideStatusTimer.Interval = TimeSpan.FromMinutes(1);
        _tideStatusTimer.IsRepeating = true;
        _tideStatusTimer.Tick += (_, _) => ViewModel.UpdateTideStatus();
        _sidebarAutoHideTimer = DispatcherQueue.CreateTimer();
        _sidebarAutoHideTimer.Interval = TimeSpan.FromSeconds(2);
        _sidebarAutoHideTimer.IsRepeating = false;
        _sidebarAutoHideTimer.Tick += SidebarAutoHideTimer_Tick;
        Nav.IsPaneVisible = ViewModel.SidebarVisible;
        Nav.IsPaneOpen = ViewModel.SidebarVisible;
        UpdateNavigationContentCorner();
        UpdateSidebarToggleIcon();
        Title = "Forecast Center";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        SystemBackdrop = MicaController.IsSupported() ? new MicaBackdrop { Kind = MicaKind.Base } : null;
        var windowHandle = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
        _appWindow = appWindow;
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            appWindow.TitleBar.BackgroundColor = Colors.Transparent;
            appWindow.TitleBar.InactiveBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            var nativeTitleBarHeight = appWindow.TitleBar.Height;
            TitleBarRow.Height = new GridLength(nativeTitleBarHeight);
            TitleBarDragRegion.Height = nativeTitleBarHeight;
            TitleBarDragRegion.Padding = new Thickness(12, 0, appWindow.TitleBar.RightInset + 12, 0);
        }
        RestoreWindowState();
        appWindow.Changed += AppWindow_Changed;
        appWindow.Closing += (_, _) => SaveWindowState();
        Closed += MainWindow_Closed;
        InitializeTrayIcon();
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "forecast-center.ico"));
        ApplyNativeTitleBarTheme(windowHandle);
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            UpdateTrayIconAppearance();
            if (ViewModel.Theme.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                ApplyNativeTitleBarTheme(windowHandle);
                await ApplyRadarMapThemeAsync(ResolveMapTheme("System"));
            }
        });
        Activated += MainWindow_Activated;
        Activated += MainWindow_RefreshWhenStale;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) CloseActiveForecastToolTip();
        };
    }

    private void RestoreWindowState()
    {
        var hasSavedState = File.Exists(_windowStatePath);
        var state = new WindowState();
        try
        {
            if (hasSavedState)
                state = JsonSerializer.Deserialize<WindowState>(File.ReadAllText(_windowStatePath)) ?? new();
        }
        catch { }

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var maximumWidth = Math.Max(1, Math.Min(3840, workArea.Width - 64));
        var maximumHeight = Math.Max(1, Math.Min(2160, workArea.Height - 48));
        var minimumWidth = Math.Min(720, maximumWidth);
        var minimumHeight = Math.Min(540, maximumHeight);
        var width = Math.Clamp(state.Width, minimumWidth, maximumWidth);
        var height = Math.Clamp(state.Height, minimumHeight, maximumHeight);
        _lastRestoredWindowSize = new Windows.Graphics.SizeInt32(width, height);

        // Center the preferred first-launch size. Returning users keep the
        // normal Windows placement unless their saved size no longer fits the
        // current monitor, in which case center the safely constrained window.
        if (!hasSavedState || width != state.Width || height != state.Height)
        {
            var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
        }
        else
        {
            _appWindow.Resize(_lastRestoredWindowSize);
        }
        if (state.Maximized && _appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
            _lastRestoredWindowSize = sender.Size;
        if (args.DidSizeChange || args.DidPresenterChange)
        {
            _windowStateTimer.Stop();
            _windowStateTimer.Start();
        }
        if (args.DidPresenterChange && ViewModel.MinimizeToTray && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            SaveWindowState();
            sender.Hide();
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIconImage = CreateNativeTrayIcon();
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem("Open Forecast Center");
            open.Click += (_, _) => DispatcherQueue.TryEnqueue(RestoreFromTray);
            var refresh = new System.Windows.Forms.ToolStripMenuItem("Refresh weather");
            refresh.Click += (_, _) => DispatcherQueue.TryEnqueue(async () => await RefreshWeatherAndRadarAsync());
            var exit = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exit.Click += (_, _) => DispatcherQueue.TryEnqueue(() => Close());
            menu.Items.Add(open);
            menu.Items.Add(refresh);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exit);
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = _trayIconImage,
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => DispatcherQueue.TryEnqueue(RestoreFromTray);
            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ViewModel.Temperature) or nameof(ViewModel.LocationName))
                    UpdateTrayTooltip();
            };
            UpdateTrayTooltip();
        }
        catch { }
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon is null) return;
        var text = $"{ViewModel.Temperature} · {ViewModel.LocationName} — Forecast Center";
        _trayIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private System.Drawing.Icon CreateNativeTrayIcon()
    {
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var darkTaskbar = (background.R * .299) + (background.G * .587) + (background.B * .114) < 128;
        var foreground = darkTaskbar ? System.Drawing.Color.FromArgb(245, 255, 255, 255) : System.Drawing.Color.FromArgb(235, 24, 28, 34);
        using var bitmap = new System.Drawing.Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        using (var brush = new System.Drawing.SolidBrush(foreground))
        using (var cloud = new System.Drawing.Drawing2D.GraphicsPath())
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            cloud.StartFigure();
            cloud.AddBezier(3.5f, 13.25f, 1.85f, 13.25f, 1.15f, 12.15f, 1.45f, 10.75f);
            cloud.AddBezier(1.45f, 10.75f, 1.7f, 9.55f, 2.65f, 8.75f, 3.85f, 8.75f);
            cloud.AddBezier(3.85f, 8.75f, 4.15f, 6.05f, 6.15f, 4.25f, 8.55f, 4.65f);
            cloud.AddBezier(8.55f, 4.65f, 10.5f, 4.95f, 11.75f, 6.4f, 11.9f, 8.25f);
            cloud.AddBezier(11.9f, 8.25f, 13.55f, 8.2f, 14.65f, 9.25f, 14.55f, 10.75f);
            cloud.AddBezier(14.55f, 10.75f, 14.45f, 12.25f, 13.35f, 13.25f, 11.75f, 13.25f);
            cloud.CloseFigure();
            graphics.FillPath(brush, cloud);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    private void UpdateTrayIconAppearance()
    {
        if (_trayIcon is null) return;
        var previous = _trayIconImage;
        _trayIconImage = CreateNativeTrayIcon();
        _trayIcon.Icon = _trayIconImage;
        previous?.Dispose();
    }

    private void RestoreFromTray()
    {
        _appWindow.Show();
        if (_appWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
        Activate();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveWindowState();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }

    private void SaveWindowState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_windowStatePath)!);
            var maximized = _appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            File.WriteAllText(_windowStatePath, JsonSerializer.Serialize(new WindowState(_lastRestoredWindowSize.Width, _lastRestoredWindowSize.Height, maximized)));
        }
        catch { }
    }

    private sealed record WindowState(int Width = 1173, int Height = 980, bool Maximized = false);

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= MainWindow_Activated;
        _initialized = true;
        UnitsRadio.SelectedIndex = ViewModel.Metric ? 1 : 0;
        if (_themeRadio is not null)
            _themeRadio.SelectedIndex = ViewModel.Theme.ToLowerInvariant() switch { "light" => 1, "dark" => 2, _ => 0 };
        if (ViewModel.AutomaticLocation)
        {
            try
            {
                if (await _windowsLocationService.GetCurrentLocationAsync() is { } location) ViewModel.CurrentLocation = location;
                else throw new InvalidOperationException();
            }
            catch
            {
                await ViewModel.SetAutomaticLocationAsync(false);
            }
        }
        var initialWeatherLoad = RefreshWeatherAndRadarAsync(refreshRadar: false);
        // The skeleton is a launch transition, not a network-blocking screen.
        // Yield after a short cap and let slow data continue populating in place.
        await Task.WhenAny(initialWeatherLoad, Task.Delay(1100));
        await RevealDashboardAsync();
        await initialWeatherLoad;
        SyncTideStationPickers();
        RefreshLocationPicker();
        UpdateDefaultLocationButton();
        DispatcherQueue.TryEnqueue(() => { UpdatePagerButtons(HourlyScroller); UpdatePagerButtons(DailyScroller); UpdateHourlyTrendRange(); });
        await InitializeRadarAsync(DashboardRadarWebView);
        _weatherRefreshTimer.Start();
        _tideStatusTimer.Start();
        if (!ViewModel.NavigationTipDismissed)
            DispatcherQueue.TryEnqueue(() => NavigationTeachingTip.IsOpen = true);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWeatherAndRadarAsync();
    }

    private void CurrentWeatherCard_PointerEntered(object sender, PointerRoutedEventArgs e) => HeroRefreshButton.Opacity = 1;
    private void CurrentWeatherCard_PointerExited(object sender, PointerRoutedEventArgs e) => HeroRefreshButton.Opacity = 0;
    private void SetRadarAttributionOpacity(double opacity)
    {
        if (DashboardRadarCard.Child is Grid radarContents && radarContents.Children.LastOrDefault() is Border attribution)
        {
            attribution.Opacity = opacity;
            attribution.IsHitTestVisible = false;
        }
    }

    private void NormalizeCommandCardTints()
    {
        NearTermCard.Background = new SolidColorBrush(ColorHelper.FromArgb(14, 42, 142, 219));
        NearTermCard.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(36, 42, 142, 219));
        TemperatureTrendCard.Background = new SolidColorBrush(ColorHelper.FromArgb(14, 155, 139, 234));
        TemperatureTrendCard.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(36, 155, 139, 234));
        DaylightCard.Background = new SolidColorBrush(ColorHelper.FromArgb(14, 242, 184, 75));
        DaylightCard.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(36, 242, 184, 75));
    }

    private async void MainWindow_RefreshWhenStale(object sender, WindowActivatedEventArgs args)
    {
        if (!_initialized || args.WindowActivationState == WindowActivationState.Deactivated || ViewModel.IsBusy) return;
        if (DateTimeOffset.UtcNow - _lastWeatherRefresh >= TimeSpan.FromMinutes(ViewModel.RefreshMinutes))
            await RefreshWeatherAndRadarAsync();
    }

    private async Task RefreshWeatherAndRadarAsync(bool refreshRadar = true)
    {
        if (ViewModel.IsBusy) return;
        CloseActiveForecastToolTip();
        await ViewModel.LoadAsync();
        _lastWeatherRefresh = DateTimeOffset.UtcNow;
        UpdateForecastLayout(DashboardGrid.ActualWidth);
        UpdateHourlyTrendRange();
        if (refreshRadar) await RefreshRadarAsync();
    }

    private async Task RefreshRadarAsync()
    {
        var currentTemperatureJson = JsonSerializer.Serialize(ViewModel.Temperature);
        foreach (var webView in new[] { DashboardRadarWebView, RadarWebView })
        {
            if (webView.CoreWebView2 is null) continue;
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync($"window.setCurrentTemperature?.({currentTemperatureJson});");
            }
            catch { }
        }
        _ = UpdateNearbyRadarCitiesAsync(ViewModel.CurrentLocation.StorageKey);

        IReadOnlyList<NoaaObservedRadarFrame> observedFrames = [];
        try
        {
            using var observedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            observedFrames = await _observedRadarService.GetFramesAsync(
                ViewModel.CurrentLocation.Latitude,
                ViewModel.CurrentLocation.Longitude,
                observedTimeout.Token);
        }
        catch { }
        var observedJson = JsonSerializer.Serialize(observedFrames);
        foreach (var webView in new[] { DashboardRadarWebView, RadarWebView })
        {
            if (webView.CoreWebView2 is null) continue;
            try { await webView.CoreWebView2.ExecuteScriptAsync($"window.setObservedFrames?.({observedJson});"); }
            catch { }
        }

        IReadOnlyList<HrrrRadarFrame> forecastFrames = [];
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            forecastFrames = await _hrrrRadarService.GetFramesAsync(
                ViewModel.CurrentLocation.Latitude,
                ViewModel.CurrentLocation.Longitude,
                ViewModel.RadarSnowPossible,
                timeout.Token);
        }
        catch (Exception ex)
        {
            try
            {
                var folder = AppIdentity.DataRoot;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "hrrr-radar-status.txt"), $"{DateTimeOffset.Now:O}\nRefresh failed: {ex}");
            }
            catch { }
        }

        IReadOnlyList<DateTimeOffset> forecastTimes = [];
        if (forecastFrames.Count == 0)
        {
            try { forecastTimes = await _ndfdPrecipitationService.GetValidTimesAsync(); }
            catch { }
        }
        var framesJson = JsonSerializer.Serialize(forecastFrames);
        var forecastJson = JsonSerializer.Serialize(forecastTimes.Select(time => time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm")));
        foreach (var webView in new[] { DashboardRadarWebView, RadarWebView })
        {
            if (webView.CoreWebView2 is null) continue;
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync($"window.setHrrrFrames?.({framesJson});");
                await webView.CoreWebView2.ExecuteScriptAsync($"window.setForecastTimes?.({forecastJson});");
            }
            catch { }
        }
    }

    private async Task UpdateNearbyRadarCitiesAsync(string locationKey, WebView2? target = null)
    {
        try
        {
            await LoadRadarTemperatureCacheAsync();
            if (ViewModel.CurrentLocation.StorageKey != locationKey) return;
            var cacheKey = $"{locationKey}|{ViewModel.Metric}";
            if (_radarTemperatureCache.TryGetValue(cacheKey, out var cached))
                await PushNearbyRadarCitiesAsync(cached.Cities, target);
            if (cached is not null && DateTimeOffset.UtcNow - cached.SavedAt < TimeSpan.FromMinutes(10)) return;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            if (!_radarTemperatureRequests.TryGetValue(cacheKey, out var request))
                _radarTemperatureRequests[cacheKey] = request = ViewModel.GetNearbyTemperaturesAsync(timeout.Token);
            IReadOnlyList<NearbyTemperature> cities;
            try { cities = await request; }
            finally { _radarTemperatureRequests.Remove(cacheKey); }
            if (ViewModel.CurrentLocation.StorageKey != locationKey) return;
            _radarTemperatureCache[cacheKey] = new(DateTimeOffset.UtcNow, cities.ToList());
            await SaveRadarTemperatureCacheAsync();
            await PushNearbyRadarCitiesAsync(cities, target);
        }
        catch { }
    }

    private async Task PushNearbyRadarCitiesAsync(IReadOnlyList<NearbyTemperature> cities, WebView2? target)
    {
        var citiesJson = JsonSerializer.Serialize(cities);
        var webViews = target is null ? new[] { DashboardRadarWebView, RadarWebView } : new[] { target };
        foreach (var webView in webViews)
        {
            if (webView.CoreWebView2 is null) continue;
            try { await webView.CoreWebView2.ExecuteScriptAsync($"window.setNearbyCities?.({citiesJson});"); }
            catch { }
        }
    }

    private async Task LoadRadarTemperatureCacheAsync()
    {
        if (_radarTemperatureCacheLoaded) return;
        _radarTemperatureCacheLoaded = true;
        try
        {
            if (!File.Exists(_radarTemperatureCachePath)) return;
            var values = JsonSerializer.Deserialize<Dictionary<string, RadarTemperatureCache>>(await File.ReadAllTextAsync(_radarTemperatureCachePath));
            if (values is null) return;
            foreach (var value in values) _radarTemperatureCache[value.Key] = value.Value;
        }
        catch { }
    }

    private async Task SaveRadarTemperatureCacheAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_radarTemperatureCachePath)!);
            await File.WriteAllTextAsync(_radarTemperatureCachePath, JsonSerializer.Serialize(_radarTemperatureCache));
        }
        catch { }
    }

    private sealed record RadarTemperatureCache(DateTimeOffset SavedAt, List<NearbyTemperature> Cities);
    private void OpenRadar_Click(object sender, RoutedEventArgs e) => Nav.SelectedItem = RadarNavItem;
    private void OpenAlerts_Click(object sender, RoutedEventArgs e) => Nav.SelectedItem = AlertsNavItem;
    private void BackToWeather_Click(object sender, RoutedEventArgs e) => Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(item => (item.Tag as string) == "weather");
    private void SettingsDashboard_Click(object sender, RoutedEventArgs e)
    {
        ResetSettingsToGeneral();
        BackToWeather_Click(sender, e);
    }
    private async void SetDefaultLocation_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetDefaultLocationAsync();
        UpdateDefaultLocationButton();
    }

    private async void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        NavigationTeachingTip.IsOpen = false;
        _sidebarAutoHideTimer.Stop();
        Nav.IsPaneVisible = !Nav.IsPaneVisible;
        Nav.IsPaneOpen = Nav.IsPaneVisible;
        UpdateSidebarToggleIcon();
        UpdateNavigationContentCorner();
        await ViewModel.SetSidebarVisibleAsync(Nav.IsPaneVisible);
        if (Nav.IsPaneVisible && !ViewModel.SidebarPinned) _sidebarAutoHideTimer.Start();
    }

    private async void NavigationTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        await ViewModel.DismissNavigationTipAsync();
    }

    private void NavigationPane_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideNavigationPane = true;
        _sidebarAutoHideTimer.Stop();
    }

    private void NavigationPane_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInsideNavigationPane = false;
        if (!ViewModel.SidebarPinned && Nav.IsPaneVisible)
        {
            _sidebarAutoHideTimer.Stop();
            _sidebarAutoHideTimer.Start();
        }
    }

    private async void SidebarAutoHideTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel.SidebarPinned || _pointerInsideNavigationPane || !Nav.IsPaneVisible) return;
        await HideUnpinnedSidebarAsync();
    }

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (ViewModel.SidebarPinned || !Nav.IsPaneVisible) return;
        _sidebarAutoHideTimer.Stop();
        // Defer until NavigationView finishes committing the invoked selection.
        DispatcherQueue.TryEnqueue(async () => await HideUnpinnedSidebarAsync());
    }

    private async Task HideUnpinnedSidebarAsync()
    {
        if (ViewModel.SidebarPinned || !Nav.IsPaneVisible) return;
        Nav.IsPaneVisible = false;
        Nav.IsPaneOpen = false;
        UpdateSidebarToggleIcon();
        UpdateNavigationContentCorner();
        await ViewModel.SetSidebarVisibleAsync(false);
    }

    private async void PinSidebarButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingSidebarPin) return;
        _sidebarAutoHideTimer.Stop();
        await ViewModel.SetSidebarPinnedAsync(true);
        if (!Nav.IsPaneVisible)
        {
            Nav.IsPaneVisible = Nav.IsPaneOpen = true;
            await ViewModel.SetSidebarVisibleAsync(true);
            UpdateSidebarToggleIcon();
            UpdateNavigationContentCorner();
        }
        UpdateSidebarPinPresentation();
    }

    private async void PinSidebarButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_updatingSidebarPin) return;
        await ViewModel.SetSidebarPinnedAsync(false);
        UpdateSidebarPinPresentation();
        if (!_pointerInsideNavigationPane && Nav.IsPaneVisible) _sidebarAutoHideTimer.Start();
    }

    private void UpdateSidebarPinPresentation()
    {
        var pinned = PinSidebarButton.IsChecked == true;
        PinSidebarIcon.Glyph = "\uE718";
        PinSidebarIcon.Opacity = pinned ? 1 : 0.62;
        PinSidebarIcon.Foreground = pinned
            ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        ToolTipService.SetToolTip(PinSidebarButton, pinned ? "Allow navigation to auto-hide" : "Keep navigation open");
    }

    private static void AnimatePageEntrance(FrameworkElement page)
    {
        var visual = ElementCompositionPreview.GetElementVisual(page);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1f));

        visual.Opacity = 0;
        visual.Offset = new Vector3(0, 24, 0);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = TimeSpan.FromMilliseconds(750);

        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(1, Vector3.Zero, easing);
        rise.Duration = TimeSpan.FromMilliseconds(750);

        visual.StartAnimation(nameof(visual.Opacity), fade);
        visual.StartAnimation(nameof(visual.Offset), rise);
    }

    private async Task RevealDashboardAsync()
    {
        AnimatePageEntrance(WeatherPage);
        var skeletonVisual = ElementCompositionPreview.GetElementVisual(DashboardSkeleton);
        var fade = skeletonVisual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(360);
        skeletonVisual.StartAnimation(nameof(skeletonVisual.Opacity), fade);
        await Task.Delay(380);
        DashboardSkeleton.Visibility = Visibility.Collapsed;
        skeletonVisual.StopAnimation(nameof(skeletonVisual.Opacity));
        skeletonVisual.Opacity = 1;
    }

    private void UpdateSidebarToggleIcon()
    {
        SidebarToggleIcon.Glyph = Nav.IsPaneVisible ? "\uE89F" : "\uE8A0";
        PinSidebarButton.Visibility = Nav.IsPaneVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateNavigationContentCorner();
        _updatingSidebarPin = true;
        PinSidebarButton.IsChecked = ViewModel.SidebarPinned;
        _updatingSidebarPin = false;
        UpdateSidebarPinPresentation();
        if (FindNamedDescendant(Nav, "PaneContentGrid") is FrameworkElement paneContent)
        {
            paneContent.PointerEntered += NavigationPane_PointerEntered;
            paneContent.PointerExited += NavigationPane_PointerExited;
        }
        if (Nav.IsPaneVisible && !ViewModel.SidebarPinned) _sidebarAutoHideTimer.Start();
        if (Nav.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.MinHeight = 42;
            settingsItem.Margin = new Thickness(4, 2, 4, 4);
            settingsItem.CornerRadius = new CornerRadius(8);
            settingsItem.Icon = new FontIcon { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 16, Glyph = "\uE713" };
        }
        DispatcherQueue.TryEnqueue(PolishNavigationIndicators);
        var labels = Nav.MenuItems.OfType<NavigationViewItem>().Select(x => x.Content?.ToString() ?? "").Append("Settings");
        var widestLabel = labels.Select(label =>
        {
            var text = new TextBlock { Text = label, FontSize = 14 };
            text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return text.DesiredSize.Width;
        }).DefaultIfEmpty(0).Max();

        // Account for the icon, label gap, balanced padding, item margins, and the
        // NavigationView's internal content presenter inset so labels never clip.
        Nav.OpenPaneLength = Math.Ceiling(widestLabel + 20 + 12 + 52 + 8);
    }

    private void PolishNavigationIndicators()
    {
        var items = Nav.MenuItems.OfType<NavigationViewItem>().ToList();
        if (Nav.SettingsItem is NavigationViewItem settingsItem) items.Add(settingsItem);
        foreach (var item in items)
        {
            if (FindNamedDescendant(item, "SelectionIndicator") is FrameworkElement indicator)
            {
                indicator.Height = 22;
                indicator.VerticalAlignment = VerticalAlignment.Center;
                if (indicator is Border border) border.CornerRadius = new CornerRadius(2);
            }
        }
    }

    private void UpdateNavigationContentCorner()
    {
        var radius = Nav.IsPaneVisible ? new CornerRadius(8, 0, 0, 0) : new CornerRadius(0);
        Nav.Resources["NavigationViewContentGridCornerRadius"] = radius;
        if (FindNamedDescendant(Nav, "ContentGrid") is Grid contentGrid) contentGrid.CornerRadius = radius;
    }

    private static FrameworkElement? FindNamedDescendant(DependencyObject parent, string name)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && element.Name == name) return element;
            var match = FindNamedDescendant(child, name);
            if (match is not null) return match;
        }
        return null;
    }

    private static void ReplaceDescendantText(DependencyObject parent, string startsWith, string replacement)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock text && text.Text.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase)) text.Text = replacement;
            ReplaceDescendantText(child, startsWith, replacement);
        }
    }

    private void UpdateDefaultLocationButton() => DefaultLocationButton.Visibility = ViewModel.IsCurrentLocationDefault ? Visibility.Collapsed : Visibility.Visible;

    private async void SavedLocationPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingLocationPicker || SavedLocationPicker.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is string action && action == "add-location")
        {
            // Do not clear/rebuild a ComboBox while its SelectionChanged event
            // is still closing the flyout; WinUI can throw E_ELEMENTNOTFOUND.
            DispatcherQueue.TryEnqueue(OpenAddLocationSettings);
            return;
        }
        if (item.Tag is string tag && tag == "automatic")
        {
            try
            {
                var automatic = await _windowsLocationService.GetCurrentLocationAsync()
                    ?? throw new InvalidOperationException("Windows did not return a location.");
                await ViewModel.SetAutomaticLocationAsync(true);
                await ChangeLocationWithTransitionAsync(automatic, saveLocation: false);
                RefreshLocationPicker();
            }
            catch (Exception ex)
            {
                await ViewModel.SetAutomaticLocationAsync(false);
                ViewModel.Status = $"Automatic location is unavailable: {ex.Message}";
                RefreshLocationPicker();
            }
            return;
        }
        if (item.Tag is not LocationResult location) return;
        if (ViewModel.AutomaticLocation) await ViewModel.SetAutomaticLocationAsync(false);
        if (Math.Abs(location.Latitude - ViewModel.CurrentLocation.Latitude) < .001 && Math.Abs(location.Longitude - ViewModel.CurrentLocation.Longitude) < .001) { RefreshLocationPicker(); return; }
        await ChangeLocationWithTransitionAsync(location, saveLocation: false);
        RefreshLocationPicker();
    }

    private void RefreshLocationPicker()
    {
        _updatingLocationPicker = true;
        SavedLocationPicker.Items.Clear();
        var automatic = new ComboBoxItem { Content = ViewModel.AutomaticLocation ? $"Automatic — {ViewModel.CurrentLocation.PickerDisplayName}" : "Automatic location", Tag = "automatic" };
        SavedLocationPicker.Items.Add(automatic);
        ComboBoxItem? selected = ViewModel.AutomaticLocation ? automatic : null;
        foreach (var location in ViewModel.SavedLocations)
        {
            var item = new ComboBoxItem { Content = location.PickerDisplayName, Tag = location };
            SavedLocationPicker.Items.Add(item);
            if (!ViewModel.AutomaticLocation && Math.Abs(location.Latitude - ViewModel.CurrentLocation.Latitude) < .001 && Math.Abs(location.Longitude - ViewModel.CurrentLocation.Longitude) < .001) selected = item;
        }
        SavedLocationPicker.Items.Add(new ComboBoxItem
        {
            IsEnabled = false,
            Height = 1,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 5, 8, 5),
            Content = new Border { Height = 1, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] }
        });
        var addLocationContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        addLocationContent.Children.Add(new FontIcon { Glyph = "\uE710", FontSize = 14 });
        addLocationContent.Children.Add(new TextBlock { Text = "Add location…", VerticalAlignment = VerticalAlignment.Center });
        SavedLocationPicker.Items.Add(new ComboBoxItem { Content = addLocationContent, Tag = "add-location" });
        SavedLocationPicker.SelectedItem = selected;
        _updatingLocationPicker = false;
    }

    private void OpenAddLocationSettings()
    {
        Nav.SelectedItem = Nav.SettingsItem;
        var locationsItem = SettingsNav.MenuItems.OfType<NavigationViewItem>()
            .First(item => string.Equals(item.Tag as string, "locations", StringComparison.Ordinal));
        SettingsNav.SelectedItem = locationsItem;
        FocusLocationSearchWhenLoaded();
    }

    private void FocusLocationSearchWhenLoaded()
    {
        if (SearchBox.IsLoaded)
        {
            DispatcherQueue.TryEnqueue(TryFocusLocationSearch);
            return;
        }

        RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, _) =>
        {
            SearchBox.Loaded -= loadedHandler;
            DispatcherQueue.TryEnqueue(TryFocusLocationSearch);
        };
        SearchBox.Loaded += loadedHandler;
    }

    private void TryFocusLocationSearch()
    {
        try { SearchBox.Focus(FocusState.Programmatic); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    private async Task ChangeLocationWithTransitionAsync(LocationResult location, bool saveLocation)
    {
        CloseActiveForecastToolTip();
        ShowLoadingOverlay(DashboardRadarLoading);
        await AnimateDashboardOpacityAsync(.78f, 150);
        try
        {
            await ViewModel.SelectLocationAsync(location);
            SyncTideStationPickers();
            if (saveLocation) await ViewModel.SaveCurrentLocationAsync();
            UpdateDefaultLocationButton();
            await ReloadRadarLocationsAsync();
        }
        finally
        {
            await AnimateDashboardOpacityAsync(1f, 220);
        }
    }

    private async Task AnimateDashboardOpacityAsync(float target, int durationMilliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(DashboardGrid);
        if (!_uiSettings.AnimationsEnabled)
        {
            visual.Opacity = target;
            return;
        }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, visual.Opacity);
        animation.InsertKeyFrame(1, target);
        animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        visual.StartAnimation("Opacity", animation);
        await Task.Delay(durationMilliseconds);
        visual.StopAnimation("Opacity");
        visual.Opacity = target;
    }

    private async Task ReloadRadarLocationsAsync()
    {
        var source = await GetRadarSourceAsync();
        if (DashboardRadarWebView.CoreWebView2 is not null) DashboardRadarWebView.Source = source;
        if (_radarReady && RadarWebView.CoreWebView2 is not null) RadarWebView.Source = source;
    }
    private void ForecastScroll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        CloseActiveForecastToolTip();
        var scroller = tag.StartsWith("hourly", StringComparison.Ordinal) ? HourlyScroller : DailyScroller;
        var direction = tag.EndsWith("right", StringComparison.Ordinal) ? 1 : -1;
        var target = Math.Clamp(scroller.HorizontalOffset + (direction * scroller.ViewportWidth), 0, scroller.ScrollableWidth);
        scroller.ChangeView(target, null, null, false);
    }

    private void ForecastScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta;
        WeatherPage.ChangeView(null, Math.Max(0, WeatherPage.VerticalOffset - delta), null, true);
        e.Handled = true;
    }

    private void ForecastScroller_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scroller) DispatcherQueue.TryEnqueue(() => UpdatePagerButtons(scroller));
    }

    private void ForecastScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        CloseActiveForecastToolTip();
        if (sender is ScrollViewer scroller)
        {
            UpdatePagerButtons(scroller);
            if (ReferenceEquals(scroller, HourlyScroller)) UpdateHourlyTrendRange();
        }
    }

    private void UpdatePagerButtons(ScrollViewer scroller)
    {
        var left = ReferenceEquals(scroller, HourlyScroller) ? HourlyLeftButton : DailyLeftButton;
        var right = ReferenceEquals(scroller, HourlyScroller) ? HourlyRightButton : DailyRightButton;
        SetPagerButtonVisible(left, scroller.HorizontalOffset > 0.5);
        SetPagerButtonVisible(right, scroller.HorizontalOffset < scroller.ScrollableWidth - 0.5);
    }

    private static void SetPagerButtonVisible(Button button, bool visible)
    {
        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        button.IsHitTestVisible = visible;
        button.IsTabStop = visible;
    }
    private void DashboardGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _pendingDashboardWidth = e.NewSize.Width;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void UpdateBottomFeatureAspectRatio(double contentWidth)
    {
        // Keep the full-width tide summary intentionally compact.
        var tideHeight = contentWidth < 700 ? 220d : 190d;
        if (Math.Abs(DashboardDetailsCard.Height - tideHeight) > 0.5)
            DashboardDetailsCard.Height = tideHeight;
    }

    private void UpdateForecastLayout(double dashboardWidth)
    {
        // DashboardGrid's reported width includes its 28px padding on each side.
        var contentWidth = Math.Max(0, dashboardWidth - 56);
        UpdateDashboardResponsiveLayout(contentWidth);
        UpdateBottomFeatureAspectRatio(contentWidth);
        var carouselWidth = Math.Max(120, contentWidth);
        const double preferredCardWidth = 150;
        const double gap = 8;
        var visibleCards = Math.Max(1, (int)Math.Floor((carouselWidth + gap) / (preferredCardWidth + gap)));
        _visibleHourlyCards = visibleCards;
        var cardWidth = (carouselWidth - ((visibleCards - 1) * gap)) / visibleCards;
        HourlyLayout.MinItemWidth = cardWidth;
        DailyLayout.MinItemWidth = cardWidth;
        HourlySignalChart.ItemWidth = cardWidth;
        DispatcherQueue.TryEnqueue(() => { UpdatePagerButtons(HourlyScroller); UpdatePagerButtons(DailyScroller); UpdateHourlyTrendRange(); });
    }

    private void UpdateHourlyTrendRange()
    {
        if (ViewModel.HourlyTrend.Count == 0 || HourlyLayout.MinItemWidth <= 0) return;
        const double gap = 8;
        var start = (int)Math.Round(HourlyScroller.HorizontalOffset / (HourlyLayout.MinItemWidth + gap));
        start = Math.Clamp(start, 0, ViewModel.HourlyTrend.Count - 1);
        var count = Math.Min(_visibleHourlyCards, ViewModel.HourlyTrend.Count - start);
        HourlyTrendCard.Width = (count * HourlyLayout.MinItemWidth) + ((count - 1) * gap);
        HourlySignalChart.StartIndex = start;
        HourlySignalChart.VisibleCount = count;

        var first = ViewModel.HourlyTrend[start].Time;
        var last = ViewModel.HourlyTrend[start + count - 1].Time;
        HourlyTrendRangeText.Text = first.Date == last.Date
            ? $"{first:h tt} – {last:h tt}"
            : $"{first:ddd h tt} – {last:ddd h tt}";
    }

    private void UpdateDashboardResponsiveLayout(double contentWidth)
    {
        var narrow = contentWidth < 760;
        var compact = contentWidth < 1050;
        var commandColumnCount = narrow ? 1 : compact ? 2 : 3;
        var commandRowCount = (int)Math.Ceiling(6d / commandColumnCount);
        while (CommandCenterGrid.RowDefinitions.Count < commandRowCount)
            CommandCenterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (CommandCenterGrid.RowDefinitions.Count > commandRowCount)
            CommandCenterGrid.RowDefinitions.RemoveAt(CommandCenterGrid.RowDefinitions.Count - 1);
        CommandColumn1.Width = new GridLength(1, GridUnitType.Star);
        CommandColumn2.Width = commandColumnCount >= 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        CommandColumn3.Width = commandColumnCount >= 3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        FrameworkElement[] commandCards = ViewModel.NextHourVisibility == Visibility.Visible
            ? [NextHourCard, NearTermCard, DaylightCard, ComfortCard, UvCard, AirQualityCard]
            : [NearTermCard, TemperatureTrendCard, DaylightCard, ComfortCard, UvCard, AirQualityCard];
        for (var index = 0; index < commandCards.Length; index++)
        {
            commandCards[index].Height = 116;
            Grid.SetColumn(commandCards[index], index % commandColumnCount);
            Grid.SetRow(commandCards[index], index / commandColumnCount);
        }

        HeroPrimaryColumn.Width = new GridLength(1.1, GridUnitType.Star);
        HeroSecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        HeroSecondRow.Height = narrow ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(DashboardRadarSection, narrow ? 0 : 1);
        Grid.SetRow(DashboardRadarSection, narrow ? 1 : 0);

        BottomFeatureColumn1.Width = new GridLength(1, GridUnitType.Star);
        BottomFeatureColumn2.Width = new GridLength(0);
        Grid.SetColumn(DashboardDetailsSection, 0);
        Grid.SetColumnSpan(DashboardDetailsSection, 2);
        Grid.SetRow(DashboardDetailsSection, 0);
    }

    private async void DailyForecast_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fullDate }) return;
        var day = ViewModel.Days.FirstOrDefault(item => item.FullDate == fullDate);
        if (day is null) return;
        var details = new StackPanel { Spacing = 10, MinWidth = 340 };
        details.Children.Add(new TextBlock { Text = $"{day.Glyph}  {day.Condition}", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        details.Children.Add(new TextBlock { Text = day.Summary, TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock { Text = $"High {day.High}    Low {day.Low}" });
        details.Children.Add(new TextBlock { Text = $"Precipitation chance: {day.Precipitation}" });
        details.Children.Add(new TextBlock { Text = $"Sunrise {day.Sunrise}    Sunset {day.Sunset}", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        await new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = day.FullDate, Content = details, CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close }.ShowAsync();
    }

    private void DailyForecastCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fullDate } button) return;
        var day = ViewModel.Days.FirstOrDefault(item => item.FullDate == fullDate);
        if (day is null) return;

        var content = new StackPanel { Spacing = 7, MaxWidth = 288, Padding = new Thickness(4) };
        content.Children.Add(new TextBlock { Text = day.FullDate, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"{day.Glyph}  {day.Condition}", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = day.Summary, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        content.Children.Add(new TextBlock { Text = $"High {day.High}    Low {day.Low}    Precipitation {day.Precipitation}", TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        content.Children.Add(new TextBlock { Text = $"Sunrise {day.Sunrise}    Sunset {day.Sunset}", TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        var palette = ForecastToolTipPalette();
        foreach (var textBlock in content.Children.OfType<TextBlock>()) textBlock.Foreground = palette.Foreground;

        var toolTip = new ToolTip
        {
            Content = content,
            MaxWidth = 320,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Foreground = palette.Foreground,
            Background = palette.Background,
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(1)
        };
        ToolTipService.SetToolTip(button, toolTip);
        ToolTipService.SetPlacement(button, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Bottom);
        ToolTipService.SetPlacementTarget(button, button);
        OpenForecastToolTip(button, toolTip);
    }

    private void DailyForecastCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card) CloseForecastToolTip(card);
    }

    private void HourlyForecastCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border { Tag: DateTime timestamp } card) return;
        var hour = ViewModel.Hours.FirstOrDefault(item => item.Timestamp == timestamp);
        if (hour is null) return;

        var content = new StackPanel { Spacing = 6, MaxWidth = 220, Padding = new Thickness(2) };
        content.Children.Add(new TextBlock { Text = hour.Time, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"{hour.Glyph}  {hour.Condition}", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"Temperature {hour.Temperature}    Dew point {hour.DewPoint}", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = $"Precipitation chance: {hour.Precipitation}", TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        var palette = ForecastToolTipPalette();
        foreach (var textBlock in content.Children.OfType<TextBlock>()) textBlock.Foreground = palette.Foreground;

        var toolTip = new ToolTip
        {
            Content = content,
            MaxWidth = 252,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Foreground = palette.Foreground,
            Background = palette.Background,
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(1)
        };
        ToolTipService.SetToolTip(card, toolTip);
        OpenForecastToolTip(card, toolTip);
    }

    private (SolidColorBrush Background, SolidColorBrush Border, SolidColorBrush Foreground) ForecastToolTipPalette()
    {
        if (RootGrid.ActualTheme == ElementTheme.Dark)
            return (
                new SolidColorBrush(ColorHelper.FromArgb(238, 31, 35, 40)),
                new SolidColorBrush(ColorHelper.FromArgb(90, 255, 255, 255)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 255, 255, 255)));

        return (
            new SolidColorBrush(ColorHelper.FromArgb(246, 250, 252, 254)),
            new SolidColorBrush(ColorHelper.FromArgb(55, 25, 35, 45)),
            new SolidColorBrush(ColorHelper.FromArgb(255, 24, 27, 31)));
    }

    private void HourlyForecastCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card) CloseForecastToolTip(card);
    }

    private void OpenForecastToolTip(FrameworkElement owner, ToolTip toolTip)
    {
        CloseActiveForecastToolTip();
        _activeForecastToolTip = toolTip;
        owner.Unloaded += ForecastToolTipOwner_Unloaded;
        toolTip.IsOpen = true;
    }

    private void CloseForecastToolTip(FrameworkElement owner)
    {
        owner.Unloaded -= ForecastToolTipOwner_Unloaded;
        if (ToolTipService.GetToolTip(owner) is ToolTip toolTip) toolTip.IsOpen = false;
        if (ReferenceEquals(_activeForecastToolTip, ToolTipService.GetToolTip(owner))) _activeForecastToolTip = null;
    }

    private void ForecastToolTipOwner_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement owner) CloseForecastToolTip(owner);
    }

    private void CloseActiveForecastToolTip()
    {
        if (_activeForecastToolTip is not null) _activeForecastToolTip.IsOpen = false;
        _activeForecastToolTip = null;
    }
    private async void Search_Click(object sender, RoutedEventArgs e) => await ViewModel.SearchCommand.ExecuteAsync(null);
    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) await ViewModel.SearchCommand.ExecuteAsync(null); }
    private async void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ListView)sender).SelectedItem is not LocationResult location) return;
        await ChangeLocationWithTransitionAsync(location, saveLocation: true);
        RefreshLocationPicker();
    }
    private async void RemoveSavedLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string storageKey }) { await ViewModel.RemoveSavedLocationAsync(storageKey); RefreshLocationPicker(); }
    }
    private async void UnitsRadio_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_initialized && UnitsRadio.SelectedIndex >= 0 && (UnitsRadio.SelectedIndex == 1) != ViewModel.Metric) await ViewModel.SetMetricAsync(UnitsRadio.SelectedIndex == 1); }

    private void InitializeThemeSettings()
    {
        _themeRadio = new RadioButtons();
        _themeRadio.MaxColumns = 3;
        _themeRadio.Items.Add("System");
        _themeRadio.Items.Add("Light");
        _themeRadio.Items.Add("Dark");
        _themeRadio.SelectionChanged += ThemeRadio_SelectionChanged;
        ThemeSettingsHost.Children.Add(_themeRadio);
        _minimizeToTrayCheckBox = new CheckBox { Content = "Minimize to system tray", IsChecked = ViewModel.MinimizeToTray };
        _minimizeToTrayCheckBox.Checked += MinimizeToTrayCheckBox_Changed;
        _minimizeToTrayCheckBox.Unchecked += MinimizeToTrayCheckBox_Changed;

        _windUnitPicker = CreateTaggedPicker("Wind speed", [("Automatic", "Auto"), ("Miles per hour", "mph"), ("Kilometers per hour", "km/h"), ("Knots", "kn")], ViewModel.WindUnit);
        _pressureUnitPicker = CreateTaggedPicker("Pressure", [("Automatic", "Auto"), ("Hectopascals", "hPa"), ("Inches of mercury", "inHg"), ("Millimeters of mercury", "mmHg")], ViewModel.PressureUnit);
        _windUnitPicker.SelectionChanged += MeasurementPicker_SelectionChanged;
        _pressureUnitPicker.SelectionChanged += MeasurementPicker_SelectionChanged;
        MeasurementSettingsHost.Children.Add(_windUnitPicker);
        MeasurementSettingsHost.Children.Add(_pressureUnitPicker);
        _refreshFrequencyPicker = CreateTaggedPicker("Refresh frequency", [("Every 5 minutes", "5"), ("Every 10 minutes", "10"), ("Every 15 minutes", "15"), ("Every 30 minutes", "30"), ("Every 60 minutes", "60")], ViewModel.RefreshMinutes.ToString());
        _refreshFrequencyPicker.SelectionChanged += RefreshFrequencyPicker_SelectionChanged;
        _startWithWindowsCheckBox = new CheckBox { Content = "Start Forecast Center with Windows", IsChecked = ViewModel.StartWithWindows };
        _startWithWindowsCheckBox.Checked += StartWithWindowsCheckBox_Changed;
        _startWithWindowsCheckBox.Unchecked += StartWithWindowsCheckBox_Changed;
        AppBehaviorSettingsHost.Children.Add(_refreshFrequencyPicker);
        AppBehaviorSettingsHost.Children.Add(_startWithWindowsCheckBox);
        AppBehaviorSettingsHost.Children.Add(_minimizeToTrayCheckBox);
        _globalTideStationCheckBox = new CheckBox { Content = "Use one tide station for every weather location", IsChecked = ViewModel.UseGlobalTideStation };
        _globalTideStationCheckBox.Checked += GlobalTideStationCheckBox_Changed;
        _globalTideStationCheckBox.Unchecked += GlobalTideStationCheckBox_Changed;
        _tideStationPicker = new ComboBox { ItemsSource = ViewModel.TideStationChoices, DisplayMemberPath = nameof(TideStationChoice.DisplayName), HorizontalAlignment = HorizontalAlignment.Stretch };
        _tideStationPicker.SelectedItem = ViewModel.TideStationChoices.FirstOrDefault(choice => choice.Id == ViewModel.TideStationId) ?? ViewModel.TideStationChoices[0];
        _tideStationPicker.SelectionChanged += TideStationPicker_SelectionChanged;
        TideSettingsHost.Children.Add(_globalTideStationCheckBox);
        TideSettingsHost.Children.Add(_tideStationPicker);
        AddDownloadedDataSettings(DownloadedDataSettingsHost);
        AddAboutSettings(AboutSettingsPanel);
    }

    private static ComboBox CreateTaggedPicker(string header, IReadOnlyList<(string Label, string Value)> items, string selectedValue)
    {
        var picker = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, value) in items) picker.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, selectedValue, StringComparison.OrdinalIgnoreCase)) ?? picker.Items[0];
        return picker;
    }

    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string ?? "general";
        GeneralSettingsPage.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        LocationSettingsPage.Visibility = tag == "locations" ? Visibility.Visible : Visibility.Collapsed;
        SourcesSettingsPage.Visibility = tag == "sources" ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPage.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetSettingsToGeneral()
    {
        var generalItem = SettingsNav.MenuItems.OfType<NavigationViewItem>()
            .First(item => string.Equals(item.Tag as string, "general", StringComparison.Ordinal));
        SettingsNav.SelectedItem = generalItem;
        GeneralSettingsPage.Visibility = Visibility.Visible;
        LocationSettingsPage.Visibility = Visibility.Collapsed;
        SourcesSettingsPage.Visibility = Visibility.Collapsed;
        AboutSettingsPage.Visibility = Visibility.Collapsed;
    }

    private static void AddAboutSettings(StackPanel settingsPanel)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var displayVersion = version is null ? "Unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
        var releaseDateText = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ReleaseDate")?.Value;
        var released = DateTime.TryParse(releaseDateText, out var releaseDate)
            ? releaseDate.ToString("MMMM d, yyyy")
            : "Unknown";
        var card = new Border
        {
            Style = (Style)Application.Current.Resources["WeatherCardStyle"],
            Padding = new Thickness(18),
            Child = new Grid
            {
                ColumnSpacing = 16,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(56) },
                    new ColumnDefinition()
                },
                Children =
                {
                    CreateAboutIcon(),
                    CreateAboutText(displayVersion, released)
                }
            }
        };
        settingsPanel.Children.Add(card);
    }

    private static FrameworkElement CreateAboutIcon()
    {
        var icon = new FontIcon { Glyph = "\uE706", FontSize = 24, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 242, 184, 75)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Grid { Width = 52, Height = 52, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 242, 184, 75)), CornerRadius = new CornerRadius(12), Children = { icon } };
    }

    private static FrameworkElement CreateAboutText(string version, string released)
    {
        var panel = new StackPanel { Spacing = 5 };
        Grid.SetColumn(panel, 1);
        panel.Children.Add(new TextBlock { Text = "Forecast Center", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Version {version} · Released {released}", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        panel.Children.Add(new TextBlock { Text = "A private, ad-free Windows weather dashboard.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        return panel;
    }

    private void AddDownloadedDataSettings(StackPanel settingsPanel)
    {
        _downloadedDataSettingsSection = DownloadedDataCard;
        _weatherCacheStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        _radarCacheStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        _tideCacheStatusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var refreshTides = new Button { Content = "Refresh NOAA station list", HorizontalAlignment = HorizontalAlignment.Left };
        refreshTides.Click += RefreshTideCatalog_Click;
        settingsPanel.Children.Add(CreateDownloadedDataRow("WEATHER & ENVIRONMENT", _weatherCacheStatusText));
        settingsPanel.Children.Add(CreateDownloadedDataRow("RADAR CITY TEMPERATURES", _radarCacheStatusText));
        settingsPanel.Children.Add(CreateDownloadedDataRow("NOAA TIDE STATIONS", _tideCacheStatusText, refreshTides));
        RefreshDownloadedDataStatus();
    }

    private static FrameworkElement CreateDownloadedDataRow(string title, TextBlock status, Button? action = null)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 60 });
        panel.Children.Add(status);
        if (action is not null) panel.Children.Add(action);
        return panel;
    }

    private void RefreshDownloadedDataStatus()
    {
        var dataRoot = AppIdentity.DataRoot;
        var cacheRoot = Path.Combine(dataRoot, "cache");
        if (_weatherCacheStatusText is not null)
        {
            var files = Directory.Exists(cacheRoot)
                ? Directory.EnumerateFiles(cacheRoot, "*.json", SearchOption.AllDirectories).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tides{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            _weatherCacheStatusText.Text = FormatLocalDataStatus(files);
        }
        if (_radarCacheStatusText is not null)
            _radarCacheStatusText.Text = FormatLocalDataStatus(File.Exists(_radarTemperatureCachePath) ? [_radarTemperatureCachePath] : []);
        if (_tideCacheStatusText is not null) _tideCacheStatusText.Text = ViewModel.TideCatalogStatus;
    }

    private static string FormatLocalDataStatus(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return "No local data saved yet";
        var bytes = files.Sum(path => new FileInfo(path).Length);
        var newest = files.Max(path => File.GetLastWriteTime(path));
        var size = bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024d):0} KB" : $"{bytes / 1024d / 1024d:0.0} MB";
        return $"{files.Count:N0} saved {(files.Count == 1 ? "file" : "files")} · {size} · Updated {newest:MMM d, h:mm tt}";
    }

    private async void RefreshTideCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false;
        button.Content = "Refreshing…";
        try
        {
            await ViewModel.RefreshTideStationCatalogAsync();
            SyncTideStationPickers();
        }
        catch { }
        finally
        {
            button.Content = "Refresh NOAA station list";
            button.IsEnabled = true;
            RefreshDownloadedDataStatus();
        }
    }

    private void OpenDownloadedDataSettings_Click(object sender, RoutedEventArgs e)
    {
        Nav.SelectedItem = Nav.SettingsItem;
        DispatcherQueue.TryEnqueue(() => _downloadedDataSettingsSection?.StartBringIntoView());
    }

    private async void MinimizeToTrayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _minimizeToTrayCheckBox?.IsChecked is not bool enabled) return;
        await ViewModel.SetMinimizeToTrayAsync(enabled);
    }

    private async void MeasurementPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _windUnitPicker?.SelectedItem is not ComboBoxItem { Tag: string wind } || _pressureUnitPicker?.SelectedItem is not ComboBoxItem { Tag: string pressure }) return;
        await ViewModel.SetMeasurementPreferencesAsync(wind, pressure);
    }

    private async void RefreshFrequencyPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _refreshFrequencyPicker?.SelectedItem is not ComboBoxItem { Tag: string value } || !int.TryParse(value, out var minutes)) return;
        await ViewModel.SetRefreshMinutesAsync(minutes);
        _weatherRefreshTimer.Interval = TimeSpan.FromMinutes(ViewModel.RefreshMinutes);
    }

    private async void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _startWithWindowsCheckBox?.IsChecked is not bool enabled) return;
        if (!SetStartupRegistration(enabled)) { _startWithWindowsCheckBox.IsChecked = false; ViewModel.Status = "Forecast Center could not update the Windows startup setting."; return; }
        await ViewModel.SetStartWithWindowsAsync(enabled);
    }

    private static bool SetStartupRegistration(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return false;
            if (enabled) key.SetValue(AppIdentity.StartupValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(AppIdentity.StartupValueName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }

    private async void GlobalTideStationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _updatingTideStationSelection || _globalTideStationCheckBox?.IsChecked is not bool enabled) return;
        _updatingTideStationSelection = true;
        try
        {
            await ViewModel.SetUseGlobalTideStationAsync(enabled);
            SyncTideStationPickersCore();
        }
        finally { _updatingTideStationSelection = false; }
    }

    private void SyncTideStationPickers()
    {
        _updatingTideStationSelection = true;
        try { SyncTideStationPickersCore(); }
        finally { _updatingTideStationSelection = false; }
        // Reapply after ItemsSource has processed a catalog replacement. WinUI
        // can otherwise clear the selection while leaving the loaded tide data intact.
        DispatcherQueue.TryEnqueue(() =>
        {
            _updatingTideStationSelection = true;
            try { SyncTideStationPickersCore(); }
            finally { _updatingTideStationSelection = false; }
        });
    }

    private void SyncTideStationPickersCore()
    {
        var index = ViewModel.TideStationChoices.ToList().FindIndex(item => item.Id == ViewModel.TideStationId);
        if (index < 0) index = 0;
        DashboardTideStationPicker.SelectedIndex = index;
        if (_tideStationPicker is not null) _tideStationPicker.SelectedIndex = index;
        if (_globalTideStationCheckBox is not null) _globalTideStationCheckBox.IsChecked = ViewModel.UseGlobalTideStation;
    }

    private async void TideStationPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingTideStationSelection || sender is not ComboBox { SelectedItem: TideStationChoice choice }) return;
        _updatingTideStationSelection = true;
        try
        {
            DashboardTideStationPicker.SelectedItem = choice;
            if (_tideStationPicker is not null) _tideStationPicker.SelectedItem = choice;
            await ViewModel.SetTideStationAsync(choice.Id);
        }
        finally { _updatingTideStationSelection = false; }
    }

    private async void ThemeRadio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _themeRadio is null || _themeRadio.SelectedIndex < 0) return;
        var theme = _themeRadio.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        ApplyTheme(theme);
        await ViewModel.SetThemeAsync(theme);
    }

    private void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme.ToLowerInvariant() switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        DispatcherQueue.TryEnqueue(async () =>
        {
            ApplyNativeTitleBarTheme(WindowNative.GetWindowHandle(this));
            await ApplyRadarMapThemeAsync(ResolveMapTheme(theme));
        });
    }

    private string ResolveMapTheme(string theme)
    {
        if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase)) return "dark";
        if (theme.Equals("Light", StringComparison.OrdinalIgnoreCase)) return "light";
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 384 ? "dark" : "light";
    }

    private async Task ApplyRadarMapThemeAsync(string mapTheme)
    {
        foreach (var webView in new[] { DashboardRadarWebView, RadarWebView })
        {
            if (webView.CoreWebView2 is null) continue;
            try { await webView.CoreWebView2.ExecuteScriptAsync($"window.setMapTheme?.('{mapTheme}');"); }
            catch { }
        }
    }

    private async void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "weather";
        WeatherPage.Visibility = RadarPage.Visibility = AlertsPage.Visibility = SettingsPage.Visibility = Visibility.Collapsed;
        if (args.IsSettingsSelected)
        {
            ResetSettingsToGeneral();
            SettingsPage.Visibility = Visibility.Visible;
            AnimatePageEntrance(SettingsPage);
            return;
        }
        if (tag == "radar")
        {
            RadarPage.Visibility = Visibility.Visible;
            AnimatePageEntrance(RadarPage);
            if (!_radarReady)
            {
                _radarReady = true;
                await InitializeRadarAsync(RadarWebView);
            }
        }
        else if (tag == "alerts")
        {
            AlertsPage.Visibility = Visibility.Visible;
            AnimatePageEntrance(AlertsPage);
        }
        else
        {
            RefreshLocationPicker();
            WeatherPage.Visibility = Visibility.Visible;
            AnimatePageEntrance(WeatherPage);
        }
    }

    private static void StartLoadingShimmer(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, -260f);
        animation.InsertKeyFrame(1, 1200f);
        animation.Duration = TimeSpan.FromSeconds(2.2);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Offset.X", animation);
    }

    private static void ShowLoadingOverlay(FrameworkElement overlay)
    {
        var visual = ElementCompositionPreview.GetElementVisual(overlay);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        overlay.Opacity = 1;
        overlay.Visibility = Visibility.Visible;
    }

    private async void HideLoadingOverlay(FrameworkElement overlay)
    {
        if (overlay.Visibility != Visibility.Visible) return;
        var visual = ElementCompositionPreview.GetElementVisual(overlay);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 1f);
        animation.InsertKeyFrame(1, 0f);
        animation.Duration = TimeSpan.FromMilliseconds(260);
        visual.StartAnimation("Opacity", animation);
        await Task.Delay(280);
        overlay.Visibility = Visibility.Collapsed;
        overlay.Opacity = 1;
    }

    private async Task InitializeRadarAsync(WebView2 webView)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets");
        _webViewEnvironmentTask ??= CreateWebViewEnvironmentAsync();
        await webView.EnsureCoreWebView2Async(await _webViewEnvironmentTask);
        webView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        webView.CoreWebView2.WebMessageReceived += RadarWebMessageReceived;
        webView.CoreWebView2.NavigationCompleted += (sender, e) =>
        {
            if (ReferenceEquals(webView, DashboardRadarWebView) && e.IsSuccess)
                HideLoadingOverlay(DashboardRadarLoading);
            if (e.IsSuccess) _ = RefreshRadarAsync();
        };
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping("forecastcenter.local", folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        webView.Source = await GetRadarSourceAsync();
    }

    private static async Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> CreateWebViewEnvironmentAsync()
    {
        return await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
            "",
            Path.Combine(AppIdentity.DataRoot, "WebView2"),
            new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions());
    }

    private Task<Uri> GetRadarSourceAsync()
    {
        var mapTheme = ResolveMapTheme(ViewModel.Theme);
        var source = new Uri($"https://forecastcenter.local/radar.html?lat={ViewModel.CurrentLocation.Latitude}&lon={ViewModel.CurrentLocation.Longitude}&theme={mapTheme}&ct={Uri.EscapeDataString(ViewModel.Temperature)}&snow={(ViewModel.RadarSnowPossible ? 1 : 0)}&ft=&cities=%5B%5D");
        return Task.FromResult(source);
    }

    private void RadarWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (WeatherPage.Visibility != Visibility.Visible) return;
        try
        {
            using var message = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (message.RootElement.GetProperty("type").GetString() != "pageWheel") return;
            var delta = message.RootElement.GetProperty("delta").GetDouble();
            WeatherPage.ChangeView(null, Math.Clamp(WeatherPage.VerticalOffset + delta, 0, WeatherPage.ScrollableHeight), null, true);
        }
        catch { }
    }

    private void ApplyNativeTitleBarTheme(nint windowHandle)
    {
        var isDark = RootGrid.ActualTheme == ElementTheme.Dark;
        var useDarkCaption = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(windowHandle, 20, ref useDarkCaption, sizeof(int)) != 0)
            DwmSetWindowAttribute(windowHandle, 19, ref useDarkCaption, sizeof(int));

        // Extended-content title bars do not always refresh their caption
        // glyph color when RequestedTheme changes. Set the glyphs explicitly
        // while preserving the system's native hover and close-button states.
        if (_appWindow is not null && AppWindowTitleBar.IsCustomizationSupported())
        {
            _appWindow.TitleBar.ButtonForegroundColor = isDark
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 26, 29, 33);
            _appWindow.TitleBar.ButtonInactiveForegroundColor = isDark
                ? Windows.UI.Color.FromArgb(150, 255, 255, 255)
                : Windows.UI.Color.FromArgb(145, 26, 29, 33);
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
