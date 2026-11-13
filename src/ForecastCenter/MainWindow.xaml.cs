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
    private readonly DispatcherQueueTimer _radarRevealFallbackTimer;
    private readonly Dictionary<FrameworkElement, int> _overlayVersions = [];
    private string? _radarConditionsKey;
    private bool _settingsUiBuilt;
    private string? _lastUpdateStatus;
    private DateTimeOffset _lastWeatherRefresh = DateTimeOffset.MinValue;
    private double _pendingDashboardWidth;
    private double _lastDashboardLayoutWidth = double.NaN;
    private int _visibleHourlyCards = 1;
    private int _forecastBaseCardWidth = 120;
    private int _forecastExtraPixelCards;
    private int _forecastPageWidth = 120;
    private ComboBox? _themePicker;
    private ComboBox? _unitsPicker;
    private int _settingsTabIndex;
    private bool _suppressSettingsTabAnimation;
    private ComboBox? _tideStationPicker;
    private ToggleSwitch? _minimizeToTrayToggle;
    private bool _updatingLocationPicker;
    private ToggleSwitch? _startWithWindowsToggle;
    private ComboBox? _windUnitPicker;
    private ComboBox? _pressureUnitPicker;
    private ComboBox? _refreshFrequencyPicker;
    private readonly WindowsLocationService _windowsLocationService = new();
    private ToggleSwitch? _globalTideStationToggle;
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
    private readonly string _radarStartupLogPath = Path.Combine(AppIdentity.DataRoot, "radar-startup.log");
    private Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? _webViewEnvironmentTask;
    private ToolTip? _activeForecastToolTip;
    private FrameworkElement? _activeForecastToolTipOwner;
    private bool _forecastPagerUpdateQueued;
    private readonly UpdateService _updateService = new();
    private TextBlock? _updateStatusText;
    private Button? _checkForUpdatesButton;
    private bool _updateDialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TideStationName))
                DispatcherQueue.TryEnqueue(SyncTideStationPickers);
            else if (args.PropertyName == nameof(ViewModel.TideVisibility))
                DispatcherQueue.TryEnqueue(() => UpdateForecastLayout(DashboardGrid.ActualWidth));
            else if (args.PropertyName == nameof(ViewModel.TideStationChoices))
            {
                if (_tideStationPicker is not null) _tideStationPicker.ItemsSource = ViewModel.TideStationChoices;
                SyncTideStationPickers();
            }
        };
        StartLoadingShimmer(RadarLoadingShimmer);
        StartLoadingShimmer(DashboardSkeletonShimmer);
        ElementCompositionPreview.GetElementVisual(WeatherPage).Opacity = 0;
        // Keep startup focus off the location picker without removing its normal
        // keyboard focus visual when the user tabs to it later.
        WeatherPage.Loaded += (_, _) =>
        {
            WeatherPage.Focus(FocusState.Programmatic);
        };
        // Settings controls and the tray icon are built after first paint
        // (EnsureSettingsUi / InitializeTrayIcon), keeping them off the launch path.
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
        _radarRevealFallbackTimer = DispatcherQueue.CreateTimer();
        _radarRevealFallbackTimer.Interval = TimeSpan.FromSeconds(5);
        _radarRevealFallbackTimer.IsRepeating = false;
        _radarRevealFallbackTimer.Tick += (_, _) => HideLoadingOverlay(DashboardRadarLoading);
        Title = "Forecast Center";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        InitializeBackNavigation();
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
        }
        RestoreWindowState();
        appWindow.Changed += AppWindow_Changed;
        appWindow.Closing += (_, _) => SaveWindowState();
        Closed += MainWindow_Closed;
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
        WindowState? saved = null;
        try
        {
            if (File.Exists(_windowStatePath))
                saved = JsonSerializer.Deserialize<WindowState>(File.ReadAllText(_windowStatePath));
        }
        catch { }

        // AppWindow works in physical pixels but the layout is designed in
        // effective pixels, so sizes the app picks must follow the display scale.
        var scale = Math.Max(1d, GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96d);
        int ToPhysical(int effectivePixels) => (int)Math.Round(effectivePixels * scale);

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var firstLaunch = new WindowState();
        var preferredWidth = saved?.Width ?? ToPhysical(firstLaunch.Width);
        var preferredHeight = saved?.Height ?? ToPhysical(firstLaunch.Height);
        var maximumWidth = Math.Max(1, workArea.Width - 64);
        var maximumHeight = Math.Max(1, workArea.Height - 48);
        var minimumWidth = Math.Min(ToPhysical(720), maximumWidth);
        var minimumHeight = Math.Min(ToPhysical(540), maximumHeight);
        var width = Math.Clamp(preferredWidth, minimumWidth, maximumWidth);
        var height = Math.Clamp(preferredHeight, minimumHeight, maximumHeight);
        _lastRestoredWindowSize = new Windows.Graphics.SizeInt32(width, height);

        // Center the preferred first-launch size. Returning users keep the
        // normal Windows placement unless their saved size no longer fits the
        // current monitor, in which case center the safely constrained window.
        if (saved is null || width != preferredWidth || height != preferredHeight)
        {
            var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
        }
        else
        {
            _appWindow.Resize(_lastRestoredWindowSize);
        }
        if (saved?.Maximized == true && _appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
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

        // Windows geolocation can take many seconds. Paint from the last known
        // position now and move to the fresh one when it arrives.
        var automaticLocationTask = ViewModel.AutomaticLocation ? ResolveAutomaticLocationAsync() : Task.FromResult<LocationResult?>(null);

        // Saved data paints the dashboard in a few milliseconds on most launches.
        var hasCachedForecast = await ViewModel.LoadCachedAsync();

        // Start the slow, independent pieces together: WebView2 start-up and the
        // network refresh.
        var radarTask = InitializeDashboardRadarAsync();
        var initialWeatherLoad = RefreshWeatherAndRadarAsync(refreshRadar: false);
        // Set only now: the stale-refresh handler runs on this same first activation
        // and must see the initial load as in progress rather than start its own.
        _initialized = true;
        UpdateForecastLayout(DashboardGrid.ActualWidth);
        RefreshLocationPicker();
        UpdateDefaultLocationButton();

        // The skeleton is only a fallback for launches with nothing saved. Yield after
        // a short cap and let slow data continue populating in place.
        if (!hasCachedForecast) await Task.WhenAny(initialWeatherLoad, Task.Delay(1100));
        await RevealDashboardAsync();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, InitializeTrayIcon);
        _ = CheckForUpdatesAfterLaunchAsync();

        await initialWeatherLoad;
        SyncTideStationPickers();
        RefreshLocationPicker();
        UpdateDefaultLocationButton();
        DispatcherQueue.TryEnqueue(() => { UpdatePagerButtons(HourlyScroller); UpdatePagerButtons(DailyScroller); UpdateHourlyTrendRange(); });
        _weatherRefreshTimer.Start();
        _tideStatusTimer.Start();
        await SyncRadarWithCurrentConditionsAsync();
        await ApplyAutomaticLocationAsync(await automaticLocationTask);
        await radarTask;
    }

    private async Task<LocationResult?> ResolveAutomaticLocationAsync()
    {
        try { return await _windowsLocationService.GetCurrentLocationAsync(); }
        catch { return null; }
    }

    private async Task ApplyAutomaticLocationAsync(LocationResult? location)
    {
        if (!ViewModel.AutomaticLocation) return;
        if (location is null)
        {
            await ViewModel.SetAutomaticLocationAsync(false);
            RefreshLocationPicker();
            return;
        }
        await ViewModel.RememberAutomaticLocationAsync(location);
        // Ignore GPS jitter; only move when the position meaningfully changed.
        if (Math.Abs(location.Latitude - ViewModel.CurrentLocation.Latitude) < .02 && Math.Abs(location.Longitude - ViewModel.CurrentLocation.Longitude) < .02) return;
        await ChangeLocationWithTransitionAsync(location, saveLocation: false);
        RefreshLocationPicker();
    }

    private async Task CheckForUpdatesAfterLaunchAsync()
    {
        // The Microsoft Store updates packaged installs itself.
        if (AppIdentity.IsPackaged) return;
        // Let the launch transition and first data settle before a possible dialog.
        await Task.Delay(TimeSpan.FromSeconds(4));
        await CheckForUpdatesAsync(force: false, showCurrentStatus: false);
    }

    private string RadarConditionsKey() => $"{ViewModel.Temperature}|{ViewModel.RadarSnowPossible}|{ViewModel.CurrentLocation.StorageKey}";

    /// <summary>
    /// The radar starts loading before fresh weather arrives. Afterwards, push the new
    /// temperature, and reload only if snow possibility (which changes the legend) flipped.
    /// </summary>
    private async Task SyncRadarWithCurrentConditionsAsync()
    {
        if (DashboardRadarWebView.CoreWebView2 is null || _radarConditionsKey is null || _radarConditionsKey == RadarConditionsKey()) return;
        var previousSnow = _radarConditionsKey.Split('|')[1];
        _radarConditionsKey = RadarConditionsKey();
        if (previousSnow != ViewModel.RadarSnowPossible.ToString())
        {
            await ReloadRadarLocationsAsync();
            return;
        }
        var currentTemperatureJson = JsonSerializer.Serialize(ViewModel.Temperature);
        foreach (var webView in RadarWebViews().ToList())
        {
            try { await webView.CoreWebView2.ExecuteScriptAsync($"window.setCurrentTemperature?.({currentTemperatureJson});"); }
            catch { }
        }
    }

    /// <summary>The radar views that have finished initializing (the full-page radar starts on first open).</summary>
    private IEnumerable<WebView2> RadarWebViews()
    {
        if (DashboardRadarWebView.CoreWebView2 is not null) yield return DashboardRadarWebView;
        if (RadarWebView.CoreWebView2 is not null) yield return RadarWebView;
    }

    private async Task InitializeDashboardRadarAsync()
    {
        try
        {
            LogRadarStartup("Dashboard radar initialization requested.");
            var initialization = InitializeRadarAsync(DashboardRadarWebView);
            if (await Task.WhenAny(initialization, Task.Delay(TimeSpan.FromSeconds(12))) != initialization)
            {
                SetDashboardRadarLoadingText(
                    "Radar is taking longer than expected",
                    "The dashboard will keep trying in the background");
                await initialization;
                return;
            }

            await initialization;
            await Task.Delay(TimeSpan.FromSeconds(12));
            if (DashboardRadarLoading.Visibility == Visibility.Visible)
            {
                SetDashboardRadarLoadingText(
                    "Radar is temporarily unavailable",
                    "Check the connection or open Radar to try again");
            }
        }
        catch (Exception ex)
        {
            LogRadarStartup("Dashboard radar initialization failed.", ex);
            var runtimeMissing = ex.Message.Contains("WebView2 Runtime", StringComparison.OrdinalIgnoreCase) ||
                ex is FileNotFoundException;
            SetDashboardRadarLoadingText(
                runtimeMissing
                    ? "WebView2 Runtime is required"
                    : "Radar is temporarily unavailable",
                runtimeMissing
                    ? "Install Microsoft Edge WebView2 Runtime, then reopen Forecast Center"
                    : "Check the connection or open Radar to try again");
        }
    }

    private void SetDashboardRadarLoadingText(string title, string subtitle)
    {
        if (FindDescendantByName<TextBlock>(DashboardRadarLoading, "DashboardRadarLoadingTitle") is { } titleBlock)
            titleBlock.Text = title;
        if (FindDescendantByName<TextBlock>(DashboardRadarLoading, "DashboardRadarLoadingSubtitle") is { } subtitleBlock)
            subtitleBlock.Text = subtitle;
    }

    private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && element.Name == name) return element;
            if (FindDescendantByName<T>(child, name) is { } descendant) return descendant;
        }
        return null;
    }

    private void LogRadarStartup(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(AppIdentity.DataRoot);
            var detail = exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}";
            File.AppendAllText(_radarStartupLogPath, $"{DateTimeOffset.Now:O} {detail}{Environment.NewLine}");
        }
        catch { }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWeatherAndRadarAsync();
    }

    private void CurrentWeatherCard_PointerEntered(object sender, PointerRoutedEventArgs e) => HeroRefreshButton.Opacity = 1;
    private void CurrentWeatherCard_PointerExited(object sender, PointerRoutedEventArgs e) => HeroRefreshButton.Opacity = 0;

    private void HeroForecast_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not TextBlock { IsTextTrimmed: true } summary || string.IsNullOrWhiteSpace(ViewModel.ForecastSummary)) return;
        var palette = ForecastToolTipPalette();
        var toolTip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = ViewModel.ForecastSummary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Foreground = palette.Foreground
            },
            MaxWidth = 420,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Foreground = palette.Foreground,
            Background = palette.Background,
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(summary, toolTip);
        ToolTipService.SetPlacement(summary, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Bottom);
        OpenForecastToolTip(summary, toolTip);
    }

    private void HeroForecast_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement summary) CloseForecastToolTip(summary);
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
        _radarConditionsKey = RadarConditionsKey();
        var currentTemperatureJson = JsonSerializer.Serialize(ViewModel.Temperature);
        foreach (var webView in RadarWebViews().ToList())
        {
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
        var observedScript = await Task.Run(() => $"window.setObservedFrames?.({JsonSerializer.Serialize(observedFrames)});");
        foreach (var webView in RadarWebViews().ToList())
        {
            try { await webView.CoreWebView2.ExecuteScriptAsync(observedScript); }
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
        // The frames are base64 PNGs (often a few MB); build the scripts off the UI thread.
        var (framesScript, forecastScript) = await Task.Run(() => (
            $"window.setHrrrFrames?.({JsonSerializer.Serialize(forecastFrames)});",
            $"window.setForecastTimes?.({JsonSerializer.Serialize(forecastTimes.Select(time => time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm")))});"));
        foreach (var webView in RadarWebViews().ToList())
        {
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(framesScript);
                await webView.CoreWebView2.ExecuteScriptAsync(forecastScript);
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
        var webViews = target is null ? RadarWebViews().ToList() : [target];
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
    private async void OpenRadar_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(RadarPage);
        if (!_radarReady)
        {
            _radarReady = true;
            await InitializeRadarAsync(RadarWebView);
        }
    }
    private void OpenAlerts_Click(object sender, RoutedEventArgs e) => ShowAlertsPage();

    private async void OpenLocationSettings_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-location"));
    private void OpenAlerts_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ShowAlertsPage();
    }
    private void ShowAlertsPage() => ShowPage(AlertsPage);
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage("general");
    private void ShowSettingsPage(string section)
    {
        EnsureSettingsUi();
        ShowPage(SettingsPage);
        var item = SettingsNav.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, section, StringComparison.Ordinal));
        // Opening Settings already plays the rise; starting it again for the tab would restart it mid-motion.
        _suppressSettingsTabAnimation = true;
        try { if (item is not null) SettingsNav.SelectedItem = item; }
        finally { _suppressSettingsTabAnimation = false; }
    }
    private void ShowPage(FrameworkElement page)
    {
        foreach (var other in new FrameworkElement[] { WeatherPage, RadarPage, AlertsPage, SettingsPage })
            if (!ReferenceEquals(other, page)) other.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        var onDashboard = ReferenceEquals(page, WeatherPage);
        TitleBarBackButton.Visibility = onDashboard ? Visibility.Collapsed : Visibility.Visible;
        // The drag region swallows clicks, so pull it clear of the back button while it shows.
        TitleBarDragRegion.Margin = new Thickness(onDashboard ? 0 : 56, 0, 0, 0);
        AnimatePageEntrance(page);
    }

    private bool IsOnDashboard => WeatherPage.Visibility == Visibility.Visible;

    private void GoBackToDashboard()
    {
        if (IsOnDashboard) return;
        if (SettingsPage.Visibility == Visibility.Visible) ResetSettingsToGeneral();
        RefreshLocationPicker();
        ShowPage(WeatherPage);
    }

    private void TitleBarBack_Click(object sender, RoutedEventArgs e) => GoBackToDashboard();

    /// <summary>Alt+Left, Escape and the mouse back button return to the dashboard, as in other Windows apps.</summary>
    private void InitializeBackNavigation()
    {
        void AddAccelerator(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) =>
            {
                if (!CanNavigateBack()) return;
                // Escape in the location search box belongs to the text box, not the page.
                if (key == Windows.System.VirtualKey.Escape && FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox) return;
                args.Handled = true;
                GoBackToDashboard();
            };
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }
        AddAccelerator(Windows.System.VirtualKey.Left, Windows.System.VirtualKeyModifiers.Menu);
        AddAccelerator(Windows.System.VirtualKey.GoBack, Windows.System.VirtualKeyModifiers.None);
        AddAccelerator(Windows.System.VirtualKey.Escape, Windows.System.VirtualKeyModifiers.None);
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(RootGrid).Properties.IsXButton1Pressed || !CanNavigateBack()) return;
            e.Handled = true;
            GoBackToDashboard();
        }), handledEventsToo: true);
    }

    /// <summary>
    /// Back shortcuts only act on the page itself: an open dialog, dropdown or flyout gets the
    /// key (Escape closes it) and the page underneath stays put.
    /// </summary>
    private bool CanNavigateBack() =>
        !IsOnDashboard
        && RootGrid.XamlRoot is { } root
        && !VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is not ToolTip);

    /// <summary>Settings controls are built on first open, keeping them off the launch path.</summary>
    private void EnsureSettingsUi()
    {
        if (_settingsUiBuilt) return;
        _settingsUiBuilt = true;
        InitializeThemeSettings();
    }
    private async void SetDefaultLocation_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetDefaultLocationAsync();
        UpdateDefaultLocationButton();
    }

    private void AnimatePageEntrance(FrameworkElement page)
    {
        var visual = ElementCompositionPreview.GetElementVisual(page);
        visual.StopAnimation(nameof(visual.Opacity));
        visual.StopAnimation(nameof(visual.Offset));
        if (!_uiSettings.AnimationsEnabled)
        {
            visual.Opacity = 1;
            visual.Offset = Vector3.Zero;
            if (ReferenceEquals(page, SettingsPage)) RiseInSettingsContent();
            return;
        }
        if (ReferenceEquals(page, SettingsPage))
        {
            // Settings opens like a Windows Settings page: the title and tabs fade in place
            // and only the content rises from below.
            visual.Offset = Vector3.Zero;
            visual.Opacity = 0;
            var quickFade = visual.Compositor.CreateScalarKeyFrameAnimation();
            quickFade.InsertKeyFrame(1, 1, visual.Compositor.CreateLinearEasingFunction());
            quickFade.Duration = TimeSpan.FromMilliseconds(150);
            visual.StartAnimation(nameof(visual.Opacity), quickFade);
            RiseInSettingsContent();
            return;
        }

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        visual.Opacity = 0;
        visual.Offset = new Vector3(0, 12, 0);

        // Short and decelerating: page switches should feel immediate, not staged.
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = TimeSpan.FromMilliseconds(220);

        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(1, Vector3.Zero, easing);
        rise.Duration = TimeSpan.FromMilliseconds(300);

        visual.StartAnimation(nameof(visual.Opacity), fade);
        visual.StartAnimation(nameof(visual.Offset), rise);
    }

    private async Task RevealDashboardAsync()
    {
        AnimatePageEntrance(WeatherPage);
        var skeletonVisual = ElementCompositionPreview.GetElementVisual(DashboardSkeleton);
        if (_uiSettings.AnimationsEnabled)
        {
            var fade = skeletonVisual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1, 0);
            fade.Duration = TimeSpan.FromMilliseconds(220);
            skeletonVisual.StartAnimation(nameof(skeletonVisual.Opacity), fade);
            await Task.Delay(240);
        }
        DashboardSkeleton.Visibility = Visibility.Collapsed;
        skeletonVisual.StopAnimation(nameof(skeletonVisual.Opacity));
        skeletonVisual.Opacity = 1;
        StopLoadingShimmer(DashboardSkeletonShimmer);
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
                ViewModel.Status = ex is LocationPermissionException ? ex.Message : $"Automatic location is unavailable: {ex.Message}";
                ViewModel.LocationSettingsActionVisible = ex is LocationPermissionException;
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
        ShowSettingsPage("locations");
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
        if (DashboardRadarWebView.CoreWebView2 is not null) DashboardRadarWebView.Source = await GetRadarSourceAsync(true);
        if (_radarReady && RadarWebView.CoreWebView2 is not null) RadarWebView.Source = await GetRadarSourceAsync(false);
    }
    private void ForecastScroll_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
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
        QueueForecastPagerUpdate();
    }

    private void QueueForecastPagerUpdate()
    {
        if (_forecastPagerUpdateQueued) return;
        _forecastPagerUpdateQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _forecastPagerUpdateQueued = false;
            UpdatePagerButtons(HourlyScroller);
            UpdatePagerButtons(DailyScroller);
            UpdateHourlyTrendRange();
        });
    }

    private void UpdatePagerButtons(ScrollViewer scroller)
    {
        var left = ReferenceEquals(scroller, HourlyScroller) ? HourlyLeftButton : DailyLeftButton;
        var right = ReferenceEquals(scroller, HourlyScroller) ? HourlyRightButton : DailyRightButton;
        SetPagerButtonVisible(left, scroller.HorizontalOffset > 0.5);
        SetPagerButtonVisible(right, scroller.HorizontalOffset < scroller.ScrollableWidth - 0.5);
    }

    private static void SetPagerButtonVisible(FrameworkElement button, bool visible)
    {
        button.Visibility = Visibility.Visible;
        button.Opacity = visible ? 1 : 0;
        button.IsHitTestVisible = visible;
    }
    private void DashboardGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5) return;
        _pendingDashboardWidth = e.NewSize.Width;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void ForecastCarouselHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement host || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        var visual = ElementCompositionPreview.GetElementVisual(host);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        geometry.CornerRadius = new Vector2(12, 12);
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);

        var scroller = ReferenceEquals(host, HourlyCarouselHost) ? HourlyScroller : DailyScroller;
        var scrollerVisual = ElementCompositionPreview.GetElementVisual(scroller);
        var innerGeometry = scrollerVisual.Compositor.CreateRoundedRectangleGeometry();
        var rasterizationScale = host.XamlRoot?.RasterizationScale ?? 1d;
        var physicalPixel = 1d / Math.Max(1d, rasterizationScale);
        innerGeometry.Offset = new Vector2((float)physicalPixel, 0);
        innerGeometry.Size = new Vector2(
            (float)Math.Max(1, e.NewSize.Width - (physicalPixel * 2)),
            (float)e.NewSize.Height);
        innerGeometry.CornerRadius = new Vector2(12, 12);
        scrollerVisual.Clip = scrollerVisual.Compositor.CreateGeometricClip(innerGeometry);
    }

    private void UpdateBottomFeatureAspectRatio(double contentWidth)
    {
        const double cardHeight = 270;
        DashboardDetailsCard.Height = cardHeight;
        DashboardMoonCard.Height = cardHeight;

        var tidesVisible = ViewModel.TideVisibility == Visibility.Visible;
        var stackCards = tidesVisible && contentWidth < 920;
        DashboardDetailColumn1.Width = new GridLength(1, GridUnitType.Star);
        DashboardDetailColumn2.Width = stackCards || !tidesVisible ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        DashboardDetailSecondRow.Height = stackCards ? GridLength.Auto : new GridLength(0);

        Grid.SetColumn(DashboardDetailsCard, 0);
        Grid.SetRow(DashboardDetailsCard, 0);
        Grid.SetColumnSpan(DashboardDetailsCard, stackCards ? 2 : 1);
        Grid.SetColumn(DashboardMoonCard, stackCards ? 0 : tidesVisible ? 1 : 0);
        Grid.SetRow(DashboardMoonCard, stackCards ? 1 : 0);
        Grid.SetColumnSpan(DashboardMoonCard, stackCards || !tidesVisible ? 2 : 1);
    }

    private void UpdateForecastLayout(double dashboardWidth)
    {
        if (!double.IsFinite(dashboardWidth) || dashboardWidth <= 0) return;
        // DashboardGrid's reported width includes its 28px padding on each side.
        var contentWidth = Math.Max(0, dashboardWidth - 56);
        UpdateDashboardResponsiveLayout(contentWidth);
        UpdateBottomFeatureAspectRatio(contentWidth);
        if (double.IsFinite(_lastDashboardLayoutWidth) &&
            Math.Abs(dashboardWidth - _lastDashboardLayoutWidth) < 0.5)
            return;
        _lastDashboardLayoutWidth = dashboardWidth;
        var carouselWidth = Math.Max(120, contentWidth);
        const double preferredCardWidth = 150;
        const double gap = 8;
        var visibleCards = Math.Max(1, (int)Math.Floor((carouselWidth + gap) / (preferredCardWidth + gap)));
        _visibleHourlyCards = visibleCards;
        var roundedCarouselWidth = Math.Max(120, (int)Math.Round(carouselWidth));
        _forecastPageWidth = roundedCarouselWidth;
        _forecastBaseCardWidth = Math.Max(1, (roundedCarouselWidth - ((visibleCards - 1) * (int)gap)) / visibleCards);
        _forecastExtraPixelCards = roundedCarouselWidth -
            ((_forecastBaseCardWidth * visibleCards) + ((visibleCards - 1) * (int)gap));
        UpdateForecastItemWidths();
        var chartItemWidth = (roundedCarouselWidth - ((visibleCards - 1) * gap)) / visibleCards;
        if (Math.Abs(HourlySignalChart.ItemWidth - chartItemWidth) >= 0.5)
            HourlySignalChart.ItemWidth = chartItemWidth;
        DispatcherQueue.TryEnqueue(() => { UpdatePagerButtons(HourlyScroller); UpdatePagerButtons(DailyScroller); UpdateHourlyTrendRange(); });
    }

    private void ForecastRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
            element.Width = ForecastCardWidth(args.Index);
    }

    private double ForecastCardWidth(int index)
        => _forecastBaseCardWidth + ((index % Math.Max(1, _visibleHourlyCards)) < _forecastExtraPixelCards ? 1 : 0);

    private void UpdateForecastItemWidths()
    {
        UpdateForecastItemWidths(HourlyRepeater, ViewModel.Hours.Count);
        UpdateForecastItemWidths(DailyRepeater, ViewModel.Days.Count);
    }

    private void UpdateForecastItemWidths(ItemsRepeater repeater, int count)
    {
        for (var index = 0; index < count; index++)
            if (repeater.TryGetElement(index) is FrameworkElement element)
                element.Width = ForecastCardWidth(index);
    }

    private void UpdateHourlyTrendRange()
    {
        if (ViewModel.HourlyTrend.Count == 0 || _forecastBaseCardWidth <= 0) return;
        const double gap = 8;
        var cardPitch = Math.Max(1, HourlySignalChart.ItemWidth + gap);
        var start = (int)Math.Round(HourlyScroller.HorizontalOffset / cardPitch);
        start = Math.Clamp(start, 0, ViewModel.HourlyTrend.Count - 1);
        var count = Math.Min(_visibleHourlyCards, ViewModel.HourlyTrend.Count - start);
        HourlyTrendCard.Width = _forecastPageWidth;
        HourlyTrendCard.Opacity = 1;
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
        UpdateSkeletonLayout(narrow, commandColumnCount);

        BottomFeatureColumn1.Width = new GridLength(1, GridUnitType.Star);
        BottomFeatureColumn2.Width = new GridLength(0);
        Grid.SetColumn(DashboardDetailsSection, 0);
        Grid.SetColumnSpan(DashboardDetailsSection, 2);
        Grid.SetRow(DashboardDetailsSection, 0);
    }

    /// <summary>Mirrors the dashboard's responsive grid so the reveal doesn't visibly jump.</summary>
    private void UpdateSkeletonLayout(bool narrow, int commandColumnCount)
    {
        if (DashboardSkeleton.Visibility != Visibility.Visible) return;
        SkeletonHeroSecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SkeletonHeroSecondRow.Height = narrow ? new GridLength(450) : new GridLength(0);
        Grid.SetColumn(SkeletonRadarCard, narrow ? 0 : 1);
        Grid.SetRow(SkeletonRadarCard, narrow ? 1 : 0);
        SkeletonCommandColumn2.Width = commandColumnCount >= 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SkeletonCommandColumn3.Width = commandColumnCount >= 3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var cards = SkeletonCommandGrid.Children.OfType<FrameworkElement>().ToList();
        var rows = (int)Math.Ceiling(cards.Count / (double)commandColumnCount);
        while (SkeletonCommandGrid.RowDefinitions.Count < rows) SkeletonCommandGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(116) });
        while (SkeletonCommandGrid.RowDefinitions.Count > rows) SkeletonCommandGrid.RowDefinitions.RemoveAt(SkeletonCommandGrid.RowDefinitions.Count - 1);
        for (var index = 0; index < cards.Count; index++)
        {
            Grid.SetColumn(cards[index], index % commandColumnCount);
            Grid.SetRow(cards[index], index / commandColumnCount);
        }
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
        details.Children.Add(new TextBlock { Text = $"Sunrise {day.Sunrise}    Sunset {day.Sunset}", Opacity = 0.72 });
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
        content.Children.Add(new TextBlock { Text = $"High {day.High}    Low {day.Low}    Precipitation {day.Precipitation}", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = $"Sunrise {day.Sunrise}    Sunset {day.Sunset}", TextWrapping = TextWrapping.Wrap });
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
            BorderThickness = new Thickness(0)
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
        SetHourlyCardHover(card, true);

        var content = new StackPanel { Spacing = 6, MaxWidth = 220, Padding = new Thickness(2) };
        content.Children.Add(new TextBlock { Text = hour.Time, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"{hour.Glyph}  {hour.Condition}", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = $"Temperature {hour.Temperature}    Dew point {hour.DewPoint}", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = $"Precipitation chance: {hour.Precipitation}", TextWrapping = TextWrapping.Wrap });
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
            BorderThickness = new Thickness(0)
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
        if (sender is Border card)
        {
            SetHourlyCardHover(card, false);
            CloseForecastToolTip(card);
        }
    }

    private static void SetHourlyCardHover(Border card, bool hovered)
    {
        const string backgroundKey = "HourlyCardOriginalBackground";
        if (hovered)
        {
            if (!card.Resources.ContainsKey(backgroundKey)) card.Resources[backgroundKey] = card.Background;
            if (Application.Current.Resources["ControlFillColorSecondaryBrush"] is Brush hoverBackground)
                card.Background = hoverBackground;
            return;
        }

        if (card.Resources.ContainsKey(backgroundKey) && card.Resources[backgroundKey] is Brush originalBackground)
            card.Background = originalBackground;
        card.Resources.Remove(backgroundKey);
    }

    private void OpenForecastToolTip(FrameworkElement owner, ToolTip toolTip)
    {
        CloseActiveForecastToolTip();
        _activeForecastToolTipOwner = owner;
        _activeForecastToolTip = toolTip;
        owner.Unloaded += ForecastToolTipOwner_Unloaded;
        toolTip.IsOpen = true;
    }

    private void CloseForecastToolTip(FrameworkElement owner)
    {
        if (ReferenceEquals(owner, _activeForecastToolTipOwner))
        {
            CloseActiveForecastToolTip();
            return;
        }

        owner.Unloaded -= ForecastToolTipOwner_Unloaded;
        if (ToolTipService.GetToolTip(owner) is ToolTip toolTip) toolTip.IsOpen = false;
        ToolTipService.SetToolTip(owner, null);
    }

    private void ForecastToolTipOwner_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement owner) CloseForecastToolTip(owner);
    }

    private void CloseActiveForecastToolTip()
    {
        var owner = _activeForecastToolTipOwner;
        var toolTip = _activeForecastToolTip;
        _activeForecastToolTipOwner = null;
        _activeForecastToolTip = null;

        if (owner is not null) owner.Unloaded -= ForecastToolTipOwner_Unloaded;
        if (toolTip is not null) toolTip.IsOpen = false;
        if (owner is not null && ReferenceEquals(ToolTipService.GetToolTip(owner), toolTip))
            ToolTipService.SetToolTip(owner, null);
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
    private async void UnitsPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _unitsPicker?.SelectedItem is not ComboBoxItem { Tag: string unit }) return;
        var metric = unit == "C";
        if (metric != ViewModel.Metric) await ViewModel.SetMetricAsync(metric);
    }

    private void InitializeThemeSettings()
    {
        // General
        _themePicker = CreateTaggedPicker([("Use system setting", "System"), ("Light", "Light"), ("Dark", "Dark")], ViewModel.Theme);
        _themePicker.SelectionChanged += ThemePicker_SelectionChanged;
        _unitsPicker = CreateTaggedPicker([("Fahrenheit (°F)", "F"), ("Celsius (°C)", "C")], ViewModel.Metric ? "C" : "F");
        _unitsPicker.SelectionChanged += UnitsPicker_SelectionChanged;
        _windUnitPicker = CreateTaggedPicker([("Automatic", "Auto"), ("Miles per hour", "mph"), ("Kilometers per hour", "km/h"), ("Knots", "kn")], ViewModel.WindUnit);
        _pressureUnitPicker = CreateTaggedPicker([("Automatic", "Auto"), ("Hectopascals", "hPa"), ("Inches of mercury", "inHg"), ("Millimeters of mercury", "mmHg")], ViewModel.PressureUnit);
        _windUnitPicker.SelectionChanged += MeasurementPicker_SelectionChanged;
        _pressureUnitPicker.SelectionChanged += MeasurementPicker_SelectionChanged;
        _refreshFrequencyPicker = CreateTaggedPicker([("Every 5 minutes", "5"), ("Every 10 minutes", "10"), ("Every 15 minutes", "15"), ("Every 30 minutes", "30"), ("Every 60 minutes", "60")], ViewModel.RefreshMinutes.ToString());
        _refreshFrequencyPicker.SelectionChanged += RefreshFrequencyPicker_SelectionChanged;
        _startWithWindowsToggle = CreateSettingsToggle(ViewModel.StartWithWindows);
        _startWithWindowsToggle.Toggled += StartWithWindowsToggle_Toggled;
        _ = SyncPackagedStartupToggleAsync();
        _minimizeToTrayToggle = CreateSettingsToggle(ViewModel.MinimizeToTray);
        _minimizeToTrayToggle.Toggled += MinimizeToTrayToggle_Toggled;

        var blue = SettingsIconBrush(0x4A, 0xAD, 0xE8);
        var amber = SettingsIconBrush(0xF2, 0xB8, 0x4B);
        var purple = SettingsIconBrush(0xA9, 0x9A, 0xF0);
        var teal = (Brush)Application.Current.Resources["TideAccentBrush"];

        AddSettingsSection(GeneralSettingsPanel, "Appearance",
            SettingsRow("\uE771", blue, "App theme", "Follow Windows, or always use light or dark colors.", _themePicker));
        AddSettingsSection(GeneralSettingsPanel, "Units",
            SettingsRow("\uE9CA", amber, "Temperature", "Used throughout forecasts and maps.", _unitsPicker),
            SettingsRow("ms-appx:///Assets/metrics/wind.svg", amber, "Wind speed", "Automatic follows your temperature units.", _windUnitPicker),
            SettingsRow("ms-appx:///Assets/metrics/pressure.svg", amber, "Pressure", "Automatic follows your temperature units.", _pressureUnitPicker));
        AddSettingsSection(GeneralSettingsPanel, "App behavior",
            SettingsRow("\uE72C", purple, "Refresh frequency", "How often new weather is downloaded.", _refreshFrequencyPicker),
            SettingsRow("\uE7E8", purple, "Start with Windows", "Open Forecast Center when you sign in.", _startWithWindowsToggle),
            SettingsRow("\uE73F", purple, "Minimize to system tray", "Keep Forecast Center running in the notification area.", _minimizeToTrayToggle));

        // Locations: tides (the rest of the tab is bound in XAML)
        _globalTideStationToggle = CreateSettingsToggle(ViewModel.UseGlobalTideStation);
        _globalTideStationToggle.Toggled += GlobalTideStationToggle_Toggled;
        _tideStationPicker = new ComboBox { ItemsSource = ViewModel.TideStationChoices, DisplayMemberPath = nameof(TideStationChoice.DisplayName), Width = 280, VerticalAlignment = VerticalAlignment.Center };
        _tideStationPicker.SelectedItem = ViewModel.TideStationChoices.FirstOrDefault(choice => choice.Id == ViewModel.TideStationId) ?? ViewModel.TideStationChoices[0];
        _tideStationPicker.SelectionChanged += TideStationPicker_SelectionChanged;
        TideSettingsHost.Children.Add(SettingsRow("ms-appx:///Assets/metrics/tides.svg", teal, "Tide station", "Automatic picks the nearest NOAA station.", _tideStationPicker));
        TideSettingsHost.Children.Add(SettingsRow("\uE8EB", teal, "Use one station everywhere", "Keep this tide station for every weather location.", _globalTideStationToggle));

        // Sources & data
        AddDownloadedDataSettings(SourcesSettingsPanel, blue);
        AddSettingsSection(SourcesSettingsPanel, "Attribution",
            SettingsRow("\uE946", teal, "Data sources", "Forecast, environment, and geocoding: Open-Meteo (CC BY 4.0)\nObserved and future radar, U.S. alerts, and tides: NOAA/NWS\nMap: © OpenStreetMap contributors, OpenMapTiles, and OpenFreeMap\nNearby city labels: GeoNames (CC BY 4.0)"));

        AddAboutSettings(AboutSettingsPanel, amber);
    }

    private static SolidColorBrush SettingsIconBrush(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));

    private Style SettingsStyle(string key) => (Style)SettingsPage.Resources[key];

    private void AddSettingsSection(Panel panel, string header, params UIElement[] rows)
    {
        var title = new TextBlock { Text = header, Style = SettingsStyle("SettingsSectionHeaderStyle") };
        panel.Children.Add(title);
        foreach (var row in rows) panel.Children.Add(row);
    }

    /// <summary>A Windows 11 style settings row: icon, title and description, with the control on the right.</summary>
    private Border SettingsRow(string glyph, Brush iconBrush, string title, object? description, FrameworkElement? control = null)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // App artwork (ms-appx paths) matches the dashboard; everything else is a Segoe Fluent glyph.
        // A fixed slot keeps every title lined up whatever the icon.
        FrameworkElement icon = glyph.StartsWith("ms-appx:", StringComparison.Ordinal)
            ? new Image { Source = new SvgImageSource(new Uri(glyph)), Width = 20, Height = 20 }
            : new FontIcon { Glyph = glyph, FontSize = 18, Foreground = iconBrush };
        grid.Children.Add(new Grid { Width = 20, VerticalAlignment = VerticalAlignment.Center, Children = { icon } });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (description is TextBlock live)
        {
            live.Style = SettingsStyle("SettingsRowDescriptionStyle");
            text.Children.Add(live);
        }
        else if (description is string words)
            text.Children.Add(new TextBlock { Text = words, Style = SettingsStyle("SettingsRowDescriptionStyle") });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (control is not null)
        {
            // The row's title is the control's label for Narrator, as a Header used to be.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, title);
            if (description is string help) Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(control, help);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
            grid.Children.Add(control);
        }
        return new Border { Style = SettingsStyle("SettingsRowStyle"), Child = grid };
    }

    private static ToggleSwitch CreateSettingsToggle(bool isOn) =>
        // Text-free switch, the way Windows Settings shows on/off rows.
        new() { IsOn = isOn, OnContent = "", OffContent = "", MinWidth = 0 };

    private static ComboBox CreateTaggedPicker(IReadOnlyList<(string Label, string Value)> items, string selectedValue)
    {
        var picker = new ComboBox { Width = 200 };
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

        var index = SettingsNav.MenuItems.IndexOf(args.SelectedItemContainer);
        var previous = _settingsTabIndex;
        _settingsTabIndex = index;
        if (!_suppressSettingsTabAnimation && previous >= 0 && index >= 0 && index != previous && SettingsPage.Visibility == Visibility.Visible)
            RiseInSettingsContent();
    }

    /// <summary>
    /// The Windows 11 Settings page transition: content rises from below into place while the
    /// title and tabs stay put.
    /// </summary>
    private void RiseInSettingsContent()
    {
        ElementCompositionPreview.SetIsTranslationEnabled(SettingsContentHost, true);
        var visual = ElementCompositionPreview.GetElementVisual(SettingsContentHost);
        visual.StopAnimation(nameof(visual.Opacity));
        visual.StopAnimation("Translation");
        if (!_uiSettings.AnimationsEnabled)
        {
            visual.Opacity = 1;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        // A long travel with a hard deceleration: most of the distance is covered in the first
        // few frames, then the content settles gently.
        var rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 140, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, easing);
        rise.Duration = TimeSpan.FromMilliseconds(500);
        // Quick fade so the content is solid well before it lands.
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, compositor.CreateLinearEasingFunction());
        fade.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation("Translation", rise);
        visual.StartAnimation(nameof(visual.Opacity), fade);
    }

    private void ResetSettingsToGeneral()
    {
        var generalItem = SettingsNav.MenuItems.OfType<NavigationViewItem>()
            .First(item => string.Equals(item.Tag as string, "general", StringComparison.Ordinal));
        _suppressSettingsTabAnimation = true;
        try { SettingsNav.SelectedItem = generalItem; }
        finally { _suppressSettingsTabAnimation = false; }
        GeneralSettingsPage.Visibility = Visibility.Visible;
        LocationSettingsPage.Visibility = Visibility.Collapsed;
        SourcesSettingsPage.Visibility = Visibility.Collapsed;
        AboutSettingsPage.Visibility = Visibility.Collapsed;
    }

    private void AddAboutSettings(StackPanel settingsPanel, Brush iconBrush)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var displayVersion = version is null ? "Unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
        var releaseDateText = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ReleaseDate")?.Value;
        var released = DateTime.TryParse(releaseDateText, out var releaseDate)
            ? releaseDate.ToString("MMMM d, yyyy")
            : "Unknown";

        var supportButton = new Button { Content = "Support on Ko-fi" };
        supportButton.Click += async (_, _) =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://ko-fi.com/wrenchware"));
        var rows = new List<UIElement>
        {
            SettingsRow("\uE706", iconBrush, $"Version {displayVersion}", $"Released {released} · A free, ad-free Windows weather dashboard."),
            SettingsRow("\uEB51", iconBrush, "Support Forecast Center", "Forecast Center is free and ad-free. If it is useful to you, an optional donation can help support continued development. Donations do not unlock features or benefits.", supportButton)
        };

        // Store installs are updated by the Microsoft Store, which does not allow
        // apps to direct users to download updates elsewhere.
        if (!AppIdentity.IsPackaged)
        {
            _updateStatusText = new TextBlock { Text = _lastUpdateStatus ?? "Updates are checked automatically once a day." };
            _checkForUpdatesButton = new Button { Content = "Check for updates" };
            _checkForUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(force: true, showCurrentStatus: true);
            rows.Add(SettingsRow("\uE895", iconBrush, "App updates", _updateStatusText, _checkForUpdatesButton));
        }
        AddSettingsSection(settingsPanel, "Forecast Center", [.. rows]);
    }

    private async Task CheckForUpdatesAsync(bool force, bool showCurrentStatus)
    {
        if (_checkForUpdatesButton is not null) _checkForUpdatesButton.IsEnabled = false;
        if (showCurrentStatus && _updateStatusText is not null) _updateStatusText.Text = "Checking for updates…";
        try
        {
            var status = await _updateService.CheckAsync(force);
            // Settings is built on first open, so remember the result for it.
            _lastUpdateStatus = status.UpdateAvailable
                ? $"Version {status.LatestVersion} is available."
                : $"Forecast Center {status.InstalledVersion} is up to date.";
            if (_updateStatusText is not null) _updateStatusText.Text = _lastUpdateStatus;
            if (status.UpdateAvailable) await ShowUpdateDialogAsync(status);
        }
        catch
        {
            if (showCurrentStatus && _updateStatusText is not null)
                _updateStatusText.Text = "Couldn't check for updates. Try again when you're online.";
        }
        finally
        {
            if (_checkForUpdatesButton is not null) _checkForUpdatesButton.IsEnabled = true;
        }
    }

    private async Task ShowUpdateDialogAsync(UpdateStatus status)
    {
        if (_updateDialogOpen || RootGrid.XamlRoot is null) return;
        _updateDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = $"Forecast Center {status.LatestVersion} is available",
                Content = new TextBlock
                {
                    Text = $"You currently have version {status.InstalledVersion}. Download the latest installer from GitHub.",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                },
                PrimaryButtonText = "View update",
                SecondaryButtonText = "Remind me tomorrow",
                CloseButtonText = "Skip this version",
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && status.ReleaseUri is not null)
                await Windows.System.Launcher.LaunchUriAsync(status.ReleaseUri);
            else if (result == ContentDialogResult.Secondary)
                await _updateService.RemindLaterAsync();
            else if (status.LatestVersion is not null)
                await _updateService.SkipVersionAsync(status.LatestVersion);
        }
        finally { _updateDialogOpen = false; }
    }

    private void AddDownloadedDataSettings(StackPanel settingsPanel, Brush iconBrush)
    {
        _weatherCacheStatusText = new TextBlock();
        _radarCacheStatusText = new TextBlock();
        _tideCacheStatusText = new TextBlock();
        var refreshTides = new Button { Content = "Refresh station list" };
        refreshTides.Click += RefreshTideCatalog_Click;
        var header = new TextBlock { Text = "Downloaded data", Style = SettingsStyle("SettingsSectionHeaderStyle") };
        _downloadedDataSettingsSection = header;
        settingsPanel.Children.Add(header);
        settingsPanel.Children.Add(SettingsRow("\uEBD3", iconBrush, "Weather & environment", _weatherCacheStatusText));
        settingsPanel.Children.Add(SettingsRow("\uE707", iconBrush, "Radar city temperatures", _radarCacheStatusText));
        settingsPanel.Children.Add(SettingsRow("ms-appx:///Assets/metrics/tides.svg", iconBrush, "NOAA tide stations", _tideCacheStatusText, refreshTides));
        RefreshDownloadedDataStatus();
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
        ShowSettingsPage("sources");
        // Put the section heading at the top so its rows are visible below it.
        DispatcherQueue.TryEnqueue(() => _downloadedDataSettingsSection?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = true }));
    }

    private async void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _minimizeToTrayToggle is not { IsOn: var enabled }) return;
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

    private async void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _startWithWindowsToggle is not { IsOn: var enabled }) return;
        var applied = AppIdentity.IsPackaged ? await SetPackagedStartupAsync(enabled) : SetStartupRegistration(enabled);
        if (!applied)
        {
            _startWithWindowsToggle.IsOn = false;
            ViewModel.Status = AppIdentity.IsPackaged
                ? "Windows has startup turned off for Forecast Center. Turn it on in Settings > Apps > Startup."
                : "Forecast Center could not update the Windows startup setting.";
            return;
        }
        await ViewModel.SetStartWithWindowsAsync(enabled);
    }

    /// <summary>
    /// Store (MSIX) installs register for startup through the manifest's StartupTask; the
    /// Run registry key is virtualized for packaged apps and would silently do nothing.
    /// </summary>
    private static async Task<bool> SetPackagedStartupAsync(bool enabled)
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(AppIdentity.StartupTaskId);
            if (!enabled)
            {
                task.Disable();
                return true;
            }
            var state = await task.RequestEnableAsync();
            return state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
        }
        catch { return false; }
    }

    /// <summary>Reflects changes the user made in Windows Settings > Apps > Startup.</summary>
    private async Task SyncPackagedStartupToggleAsync()
    {
        if (!AppIdentity.IsPackaged || _startWithWindowsToggle is null) return;
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(AppIdentity.StartupTaskId);
            var enabled = task.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            _startWithWindowsToggle.Toggled -= StartWithWindowsToggle_Toggled;
            _startWithWindowsToggle.IsOn = enabled;
            _startWithWindowsToggle.Toggled += StartWithWindowsToggle_Toggled;
        }
        catch { }
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

    private async void GlobalTideStationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _updatingTideStationSelection || _globalTideStationToggle is not { IsOn: var enabled }) return;
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
        if (_globalTideStationToggle is not null) _globalTideStationToggle.IsOn = ViewModel.UseGlobalTideStation;
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

    private async void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _themePicker?.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
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
        foreach (var webView in RadarWebViews().ToList())
        {
            try { await webView.CoreWebView2.ExecuteScriptAsync($"window.setMapTheme?.('{mapTheme}');"); }
            catch { }
        }
    }

    private void StartLoadingShimmer(FrameworkElement element)
    {
        if (!_uiSettings.AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, -260f);
        animation.InsertKeyFrame(1, 1200f);
        animation.Duration = TimeSpan.FromSeconds(2.2);
        animation.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Offset.X", animation);
    }

    private static void StopLoadingShimmer(FrameworkElement element) =>
        ElementCompositionPreview.GetElementVisual(element).StopAnimation("Offset.X");

    private FrameworkElement? ShimmerFor(FrameworkElement overlay) =>
        ReferenceEquals(overlay, DashboardRadarLoading) ? RadarLoadingShimmer : null;

    private void ShowLoadingOverlay(FrameworkElement overlay)
    {
        // Invalidate any fade-out in flight so it cannot collapse the overlay we just showed.
        _overlayVersions[overlay] = _overlayVersions.GetValueOrDefault(overlay) + 1;
        var visual = ElementCompositionPreview.GetElementVisual(overlay);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        overlay.Opacity = 1;
        var wasVisible = overlay.Visibility == Visibility.Visible;
        overlay.Visibility = Visibility.Visible;
        if (!wasVisible && ShimmerFor(overlay) is { } shimmer) StartLoadingShimmer(shimmer);
    }

    private async void HideLoadingOverlay(FrameworkElement overlay)
    {
        if (overlay.Visibility != Visibility.Visible) return;
        var version = _overlayVersions[overlay] = _overlayVersions.GetValueOrDefault(overlay) + 1;
        var visual = ElementCompositionPreview.GetElementVisual(overlay);
        if (_uiSettings.AnimationsEnabled)
        {
            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0, 1f);
            animation.InsertKeyFrame(1, 0f);
            animation.Duration = TimeSpan.FromMilliseconds(260);
            visual.StartAnimation("Opacity", animation);
            await Task.Delay(280);
            if (_overlayVersions.GetValueOrDefault(overlay) != version) return;
        }
        overlay.Visibility = Visibility.Collapsed;
        overlay.Opacity = 1;
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        if (ShimmerFor(overlay) is { } shimmer) StopLoadingShimmer(shimmer);
    }

    private async Task InitializeRadarAsync(WebView2 webView)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets");
        var target = ReferenceEquals(webView, DashboardRadarWebView) ? "dashboard" : "radar page";
        LogRadarStartup($"Starting {target}; control loaded={webView.IsLoaded}.");
        var runtimeVersion = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        LogRadarStartup($"WebView2 Runtime detected: {runtimeVersion}.");
        await WaitForWebViewLoadedAsync(webView).WaitAsync(TimeSpan.FromSeconds(8));
        LogRadarStartup($"The {target} WebView2 control is loaded.");
        _webViewEnvironmentTask ??= CreateWebViewEnvironmentAsync();
        await webView.EnsureCoreWebView2Async(await _webViewEnvironmentTask);
        LogRadarStartup($"CoreWebView2 created for {target}.");
        webView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        webView.CoreWebView2.WebMessageReceived += (_, e) => RadarWebMessageReceived(webView, e);
        webView.CoreWebView2.NavigationCompleted += (sender, e) =>
        {
            LogRadarStartup($"Navigation completed for {target}; success={e.IsSuccess}; error={e.WebErrorStatus}.");
            // The page reports "radarReady" once the base map has drawn; until then the
            // overlay stays up so a half-loaded map is never revealed. The timer is
            // only a fallback if that message never arrives.
            if (ReferenceEquals(webView, DashboardRadarWebView) && e.IsSuccess)
            {
                _radarRevealFallbackTimer.Stop();
                _radarRevealFallbackTimer.Start();
            }
            if (e.IsSuccess) _ = RefreshRadarAsync();
        };
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping("forecastcenter.local", folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        webView.Source = await GetRadarSourceAsync(ReferenceEquals(webView, DashboardRadarWebView));
        LogRadarStartup($"Navigation requested for {target}: {webView.Source}.");
    }

    private static Task WaitForWebViewLoadedAsync(WebView2 webView)
    {
        if (webView.IsLoaded) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            webView.Loaded -= handler;
            completion.TrySetResult();
        };
        webView.Loaded += handler;
        return completion.Task;
    }

    private static async Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> CreateWebViewEnvironmentAsync()
    {
        return await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
            "",
            Path.Combine(AppIdentity.DataRoot, "WebView2"),
            new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions());
    }

    private Task<Uri> GetRadarSourceAsync(bool dashboard)
    {
        var mapTheme = ResolveMapTheme(ViewModel.Theme);
        var source = new Uri($"https://forecastcenter.local/radar.html?lat={ViewModel.CurrentLocation.Latitude}&lon={ViewModel.CurrentLocation.Longitude}&theme={mapTheme}&ct={Uri.EscapeDataString(ViewModel.Temperature)}&snow={(ViewModel.RadarSnowPossible ? 1 : 0)}&dashboard={(dashboard ? 1 : 0)}&ft=&cities=%5B%5D");
        return Task.FromResult(source);
    }

    private void RadarWebMessageReceived(WebView2 source, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var type = message.RootElement.GetProperty("type").GetString();
            if (type == "radarReady")
            {
                if (ReferenceEquals(source, DashboardRadarWebView))
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _radarRevealFallbackTimer.Stop();
                        HideLoadingOverlay(DashboardRadarLoading);
                    });
                return;
            }
            if (type == "radarHover")
            {
                var visible = message.RootElement.GetProperty("visible").GetBoolean();
                DispatcherQueue.TryEnqueue(() => DashboardRadarExpandHint.Opacity = visible ? 1 : 0);
                return;
            }
            if (type == "expandRadar")
            {
                DispatcherQueue.TryEnqueue(() => OpenRadar_Click(this, new RoutedEventArgs()));
                return;
            }
            if (type != "pageWheel" || WeatherPage.Visibility != Visibility.Visible) return;
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
