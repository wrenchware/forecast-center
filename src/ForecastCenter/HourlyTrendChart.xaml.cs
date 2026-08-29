using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ForecastCenter.ViewModels;

namespace ForecastCenter;

public sealed partial class HourlyTrendChart : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(object), typeof(HourlyTrendChart), new PropertyMetadata(null, ItemsChanged));
    public static readonly DependencyProperty StartIndexProperty = DependencyProperty.Register(
        nameof(StartIndex), typeof(int), typeof(HourlyTrendChart), new PropertyMetadata(0, RangeChanged));
    public static readonly DependencyProperty VisibleCountProperty = DependencyProperty.Register(
        nameof(VisibleCount), typeof(int), typeof(HourlyTrendChart), new PropertyMetadata(1, RangeChanged));
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(HourlyTrendChart), new PropertyMetadata(150d, RangeChanged));

    private INotifyCollectionChanged? _observableItems;
    private IReadOnlyList<HourlyTrendPoint> _visiblePoints = [];
    private double _itemPitch;
    private int _hoveredIndex = -1;
    public object? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public int StartIndex { get => (int)GetValue(StartIndexProperty); set => SetValue(StartIndexProperty, value); }
    public int VisibleCount { get => (int)GetValue(VisibleCountProperty); set => SetValue(VisibleCountProperty, value); }
    public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }

    public HourlyTrendChart()
    {
        InitializeComponent();
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
    }

    private static void ItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var chart = (HourlyTrendChart)sender;
        if (chart._observableItems is not null) chart._observableItems.CollectionChanged -= chart.CollectionChanged;
        chart._observableItems = args.NewValue as INotifyCollectionChanged;
        if (chart._observableItems is not null) chart._observableItems.CollectionChanged += chart.CollectionChanged;
        chart.Render();
    }

    private static void RangeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((HourlyTrendChart)sender).Render();

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Render();
    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void Render()
    {
        CloseActiveToolTip();
        if (ChartCanvas is null || Items is not IEnumerable<HourlyTrendPoint> source) return;
        var points = source.Skip(Math.Max(0, StartIndex)).Take(Math.Max(1, VisibleCount)).ToList();
        _visiblePoints = points;
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (points.Count < 2 || width <= 0 || height <= 0) return;

        ChartCanvas.Children.Clear();
        const double itemSpacing = 8;
        // The chart lives inside a 14px padded card while the forecast tiles begin
        // at the dashboard edge. Offset back through that padding so every point
        // lands at the center of its corresponding tile.
        const double chartCardLeftPadding = 14;
        var itemWidth = Math.Max(1, ItemWidth);
        var itemPitch = itemWidth + itemSpacing;
        _itemPitch = itemPitch;
        var lineBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 98, 215, 242));
        var min = points.Min(point => point.DewPoint);
        var max = points.Max(point => point.DewPoint);
        var range = Math.Max(2, max - min);
        var coordinates = new PointCollection();

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var barHeight = Math.Max(2, point.PrecipitationProbability / 100d * (height - 18));
            var itemLeft = (i * itemPitch) - chartCardLeftPadding;
            var bar = new Rectangle { Width = Math.Max(4, itemWidth - 20), Height = barHeight, RadiusX = 3, RadiusY = 3, Fill = PrecipitationBrush(point), IsHitTestVisible = false };
            Canvas.SetLeft(bar, itemLeft + 10);
            Canvas.SetTop(bar, height - barHeight);
            ChartCanvas.Children.Add(bar);

            var x = itemLeft + itemWidth / 2;
            var y = 8 + (max - point.DewPoint) / range * (height - 28);
            coordinates.Add(new Windows.Foundation.Point(x, y));
        }

        ChartCanvas.Children.Add(new Polyline { Points = coordinates, Stroke = lineBrush, StrokeThickness = 2.2, IsHitTestVisible = false });
        for (var i = 0; i < coordinates.Count; i++)
        {
            var coordinate = coordinates[i];
            var point = points[i];
            var dot = new Ellipse { Width = 8, Height = 8, Fill = lineBrush, IsHitTestVisible = false };
            Canvas.SetLeft(dot, coordinate.X - 4);
            Canvas.SetTop(dot, coordinate.Y - 4);
            ChartCanvas.Children.Add(dot);
        }
    }

    private void ChartCanvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_visiblePoints.Count == 0 || _itemPitch <= 0) return;
        const double chartCardLeftPadding = 14;
        var position = e.GetCurrentPoint(ChartRoot).Position;
        var x = position.X;
        var index = Math.Clamp((int)Math.Floor((x + chartCardLeftPadding) / _itemPitch), 0, _visiblePoints.Count - 1);
        if (index != _hoveredIndex)
        {
            _hoveredIndex = index;
            var point = _visiblePoints[index];
            HoverTipText.Text = $"{point.Time:h tt}\nDew point {point.DewPoint:0}°\nPrecipitation {point.PrecipitationProbability}%";
        }
        // Render beyond the compact chart's bounds without adding another
        // pointer target. ChartRoot remains the sole hover surface.
        HoverTip.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var tipSize = HoverTip.DesiredSize;
        // Popup offsets are relative to this control, not to the window.
        var popupX = position.X + 14;
        if (popupX + tipSize.Width > ChartRoot.ActualWidth - 4)
            popupX = position.X - tipSize.Width - 10;

        var popupY = position.Y - tipSize.Height - 10;

        HoverPopup.HorizontalOffset = Math.Clamp(popupX, 4, Math.Max(4, ChartRoot.ActualWidth - tipSize.Width - 4));
        HoverPopup.VerticalOffset = popupY;
        HoverPopup.IsOpen = true;
    }

    private void ChartCanvas_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hoveredIndex = -1;
        CloseActiveToolTip();
    }

    private void CloseActiveToolTip()
    {
        HoverPopup.IsOpen = false;
    }

    private static SolidColorBrush PrecipitationBrush(HourlyTrendPoint point)
    {
        var (red, green, blue) = point.WeatherCode switch
        {
            71 or 73 or 75 or 77 or 85 or 86 => (167, 139, 250), // Snow
            56 or 57 or 66 or 67 => (103, 232, 249),             // Freezing precipitation
            95 or 96 or 99 => (251, 146, 60),                    // Thunderstorms
            51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => (51, 153, 255),
            _ => (80, 143, 205)
        };
        var alpha = (byte)Math.Clamp(82 + point.PrecipitationProbability, 82, 182);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, (byte)red, (byte)green, (byte)blue));
    }
}
