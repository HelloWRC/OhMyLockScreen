using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Controls.UI;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Models.Features.Appearance;

[AutoJsonTypeName("omls.features.appearance.stroke")]
public partial class StrokeFeatureSettings : ObservableObject, IFeatureSettings
{
    public const double DefaultThickness = 2;
    public const double MaxThickness = 64;
    public const StrokePosition DefaultPosition = StrokePosition.Outside;
    public const bool DefaultIsHollow = false;

    [ObservableProperty]
    private Brush? _strokeBrush = CreateDefaultStrokeBrush();

    private double _thickness = DefaultThickness;

    [ObservableProperty]
    private bool _isHollow = DefaultIsHollow;

    private StrokePosition _position = DefaultPosition;

    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, Sanitize(value, 0, MaxThickness, DefaultThickness));
    }

    public StrokePosition Position
    {
        get => _position;
        set => SetProperty(ref _position, Enum.IsDefined(value) ? value : DefaultPosition);
    }

    internal static SolidColorBrush CreateDefaultStrokeBrush()
    {
        // 复制颜色值，避免修改特性时污染共享的主题画刷实例。
        var resources = Application.Current?.Resources;
        var color = resources != null
            && resources.TryGetValue("SystemControlForegroundBaseHighBrush", out var value)
            && value is SolidColorBrush brush
                ? brush.Color
                : Colors.White;
        return new SolidColorBrush(color);
    }

    private static double Sanitize(double value, double min, double max, double fallback)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        var clamped = Math.Clamp(value, min, max);
        return clamped == 0 ? 0 : clamped;
    }
}
