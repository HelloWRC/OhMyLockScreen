using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Models.Scene;

public enum WeatherSummaryKind
{
    Condition,
    Humidity,
    Wind,
    AirQuality,
    Pressure,
    FeelsLike
}

[AutoJsonTypeName("omls.weather")]
[Bindable]
public partial class WeatherComponentSettings : ObservableObject, IComponentSettings
{
    [ObservableProperty] private bool _showSummary = true;
    [ObservableProperty] private WeatherSummaryKind _summaryKind;
    [ObservableProperty] private bool _showAlerts = true;
    [ObservableProperty] private WeatherAlertTextMode _alertTextMode = WeatherAlertTextMode.WithoutSpecificIcon;
    [ObservableProperty] private bool _showRainTime = true;
    [ObservableProperty] private bool _approximateLongRainTime = true;
}
