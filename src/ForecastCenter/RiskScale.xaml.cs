using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ForecastCenter;

public sealed partial class RiskScale : UserControl
{
    private readonly ToolTip _toolTip = new();
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RiskScale), new PropertyMetadata(0d, Changed));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(RiskScale), new PropertyMetadata("Uv", Changed));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public RiskScale()
    {
        InitializeComponent();
        ToolTipService.SetToolTip(ScaleRoot, _toolTip);
        ScaleRoot.PointerEntered += (_, _) => _toolTip.IsOpen = true;
        ScaleRoot.PointerExited += (_, _) => _toolTip.IsOpen = false;
    }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((RiskScale)sender).Render();
    private void ScaleRoot_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void Render()
    {
        if (ScaleTrack is null || ScaleMarker is null || ScaleRoot.ActualWidth <= 0) return;
        var (minimum, maximum, stops) = Kind switch
        {
            "Aqi" => (0d, 300d, AqiStops()),
            "Comfort" => (30d, 80d, ComfortStops()),
            _ => (0d, 11d, UvStops())
        };
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, .5), EndPoint = new Windows.Foundation.Point(1, .5) };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop { Offset = offset, Color = Color(color) });
        ScaleTrack.Background = brush;
        var position = Math.Clamp((Value - minimum) / (maximum - minimum), 0, 1);
        Canvas.SetLeft(ScaleMarker, position * Math.Max(0, ScaleRoot.ActualWidth - ScaleMarker.Width));
        _toolTip.Content = Kind switch
        {
            "Aqi" => $"U.S. AQI {Value:0} · Good 0–50 · Moderate 51–100 · Unhealthy 151+",
            "Comfort" => $"Dew point {Value:0}°F · Dry below 50° · Comfortable 50–62° · Humid 68°+",
            _ => $"UV {Value:0.#} · Low 0–2 · Moderate 3–5 · High 6–7 · Very high 8–10 · Extreme 11+"
        };
    }

    private static (double, string)[] UvStops() =>
        [(0, "#57C7AE"), (.26, "#57C7AE"), (.27, "#F2D35C"), (.53, "#F2B84B"), (.54, "#F29A4A"), (.71, "#E66E62"), (.72, "#C7638D"), (1, "#9A62C7")];
    private static (double, string)[] AqiStops() =>
        [(0, "#57C7AE"), (.17, "#57C7AE"), (.18, "#D9C95B"), (.34, "#D9C95B"), (.35, "#E5A24F"), (.5, "#E5A24F"), (.51, "#E66E62"), (.67, "#E66E62"), (.68, "#B678D1"), (1, "#89506F")];
    private static (double, string)[] ComfortStops() =>
        [(0, "#70AEEF"), (.3, "#68C7B7"), (.55, "#57C7AE"), (.7, "#D2B85B"), (.82, "#E5A24F"), (1, "#E66E62")];

    private static Windows.UI.Color Color(string hex) => Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
}
