using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Helpers.Serialization;
using Windows.UI;

namespace OhMyLockScreen.Models.Features.Appearance;

[AutoJsonTypeName("omls.features.appearance.shadow")]
public partial class ShadowFeatureSettings : ObservableObject, IFeatureSettings
{
    public const double DefaultOpacity = 0.5;
    public const double DefaultBlurRadius = 16;
    public const double DefaultOffsetX = 0;
    public const double DefaultOffsetY = 4;
    public const double MaxBlurRadius = 100;
    public const double MaxOffset = 500;

    public static Color DefaultColor => Colors.Black;

    private Color _color = DefaultColor;
    private double _opacity = DefaultOpacity;
    private double _blurRadius = DefaultBlurRadius;
    private double _offsetX = DefaultOffsetX;
    private double _offsetY = DefaultOffsetY;

    [JsonConverter(typeof(ColorJsonConverter))]
    public Color Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Sanitize(value, 0, 1, DefaultOpacity));
    }

    public double BlurRadius
    {
        get => _blurRadius;
        set => SetProperty(ref _blurRadius, Sanitize(value, 0, MaxBlurRadius, DefaultBlurRadius));
    }

    public double OffsetX
    {
        get => _offsetX;
        set => SetProperty(ref _offsetX, Sanitize(value, -MaxOffset, MaxOffset, DefaultOffsetX));
    }

    public double OffsetY
    {
        get => _offsetY;
        set => SetProperty(ref _offsetY, Sanitize(value, -MaxOffset, MaxOffset, DefaultOffsetY));
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
