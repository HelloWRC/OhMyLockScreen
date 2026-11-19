using FluentIcons.Common;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.Weather;

public static class WeatherVisuals
{
    public static ViewFluentIcon Icon(Icon icon, double size, bool filled = false) => new()
    {
        Icon = icon,
        IconSize = IconSize.Resizable,
        IconVariant = filled ? IconVariant.Filled : IconVariant.Regular,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static TextBlock Lucide(string glyph, double size) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("ms-appx:///OhMyLockScreen.Shared/Assets/WeatherIcons/lucide.ttf#lucide"),
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    public static Viewbox Pack(IWeatherService weather, string? code, double height)
    {
        var name = weather.GetWeatherTextByCode(code);
        var presenter = new ContentControl
        {
            ContentTemplate = weather.SelectedWeatherIconTemplate is { } entry
                ? Application.Current.Resources[entry.ResourceKey] as DataTemplate
                : null,
            Content = code ?? "99",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (presenter.ContentTemplate == null) presenter.Content = name;
        if (name.Contains('雨') || name == "飑") presenter.Foreground = RainBrush(presenter);
        var viewbox = new Viewbox { Height = height, Child = presenter, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(viewbox, name);
        AutomationProperties.SetName(viewbox, name);
        return viewbox;
    }

    private static readonly bool HasTintLuminosityOpacity =
        ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Media.AcrylicBrush", "TintLuminosityOpacity");

    public static Color RainColor(FrameworkElement element) =>
        IsLight(element) ? Colors.Blue : Colors.DeepSkyBlue;

    public static SolidColorBrush RainBrush(FrameworkElement element) =>
        new(RainColor(element));

    public static AcrylicBrush RainAcrylicBrush(FrameworkElement element)
    {
        var color = RainColor(element);
        var brush = new AcrylicBrush
        {
            BackgroundSource = AcrylicBackgroundSource.Backdrop,
            TintColor = color,
            TintOpacity = 0.2,
            FallbackColor = Color.FromArgb(38, color.R, color.G, color.B)
        };
        if (HasTintLuminosityOpacity && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            brush.TintLuminosityOpacity = 0.25;
        }
        return brush;
    }

    public static AcrylicBrush AlertPillAcrylicBrush(FrameworkElement element)
    {
        var light = IsLight(element);
        var tint = light ? Color.FromArgb(255, 245, 245, 245) : Color.FromArgb(255, 32, 32, 32);
        var brush = new AcrylicBrush
        {
            BackgroundSource = AcrylicBackgroundSource.Backdrop,
            TintColor = tint,
            TintOpacity = light ? 0.45 : 0.35,
            FallbackColor = Color.FromArgb(light ? (byte)180 : (byte)140, tint.R, tint.G, tint.B)
        };
        if (HasTintLuminosityOpacity && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            brush.TintLuminosityOpacity = light ? 0.55 : 0.45;
        }
        return brush;
    }

    public static Brush? AqiBrush(FrameworkElement element, string? value)
    {
        if (!double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var aqi) || !double.IsFinite(aqi)) return null;
        var light = IsLight(element);
        return new SolidColorBrush(aqi switch
        {
            <= 50 => light ? Colors.ForestGreen : Colors.MediumSeaGreen,
            <= 100 => light ? Color.FromArgb(255, 204, 204, 0) : Colors.Yellow,
            <= 150 => light ? Colors.DarkOrange : Colors.Orange,
            <= 200 => Colors.IndianRed,
            <= 300 => Colors.DarkRed,
            _ => light ? Colors.Purple : Colors.MediumPurple
        });
    }

    private static bool IsLight(FrameworkElement element) => element.ActualTheme switch
    {
        ElementTheme.Light => true,
        ElementTheme.Dark => false,
        _ => Application.Current.RequestedTheme == ApplicationTheme.Light
    };
}

public sealed class WeatherGlyphConverter : IValueConverter
{
    public string Kind { get; set; } = "Text";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var code = value as string;
        return Kind switch
        {
            "Lucide" => WeatherVisuals.Lucide(LucideGlyph(code), 16),
            "Fluent" => WeatherVisuals.Icon(WeatherSymbols.ConditionIcon(code), 20),
            _ => new TextBlock { Text = WeatherSymbols.ConditionText(code), VerticalAlignment = VerticalAlignment.Center }
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    // ClassIsland's Lucide weather code mapping uses the same packaged Lucide code points.
    private static string LucideGlyph(string? code) => code switch
    {
        "0" => "\ue2b1",
        "1" => "\ue216",
        "2" => "\ue217",
        "3" => "\ue2fb",
        "4" => "\ue090",
        "5" or "19" => "\ue08f",
        "6" or "14" or "15" or "16" or "17" or "26" or "27" or "28" or "34" or "302" => "\ue094",
        "7" => "\ue08e",
        "8" or "9" or "21" or "22" or "301" => "\ue092",
        "10" or "11" or "12" or "23" or "24" or "25" or "32" => "\ue093",
        "13" => "\ue376",
        "18" or "35" => "\ue214",
        "20" or "31" or "33" => "\ue218",
        "29" or "30" or "53" => "\ue0f4",
        _ => "\ue091"
    };
}
