using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Helpers.Serialization;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Models.Features.Appearance;

public partial class BorderFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty]
    private Brush? _borderBrush = CreateDefaultBorderBrush();

    [ObservableProperty]
    [property: JsonConverter(typeof(ThicknessJsonConverter))]
    private Thickness _borderThickness = new(1);

    [ObservableProperty]
    [property: JsonConverter(typeof(CornerRadiusJsonConverter))]
    private CornerRadius _cornerRadius;

    [ObservableProperty]
    [property: JsonConverter(typeof(ThicknessJsonConverter))]
    private Thickness _padding;

    private static SolidColorBrush CreateDefaultBorderBrush()
    {
        // Copy the color: editing a feature must never mutate a shared theme brush.
        var resources = Application.Current?.Resources;
        var color = resources != null
            && resources.TryGetValue("SystemControlForegroundBaseHighBrush", out var value)
            && value is SolidColorBrush brush
                ? brush.Color
                : Colors.White;
        return new SolidColorBrush(color);
    }
}
