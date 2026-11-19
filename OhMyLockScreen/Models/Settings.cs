using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Models.Weather;
using System.Text.Json.Serialization;

namespace OhMyLockScreen.Models;
/// <summary>应用设置。在此添加使用 SetProperty 通知变更的持久化属性。</summary>
public partial class Settings : ObservableObject
{
    [ObservableProperty]
    private Guid? _activeSceneId;
    [ObservableProperty]
    private Guid? _lastEditedSceneId;
    [ObservableProperty]
    private bool _compatibilityUnlockMode;
    [ObservableProperty]
    private WeatherLocation _weatherLocation = new();
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private WeatherIconStyle? _weatherIconStyle;
    [ObservableProperty]
    private string? _weatherIconId;
    [ObservableProperty]
    private WeatherReading? _lastWeatherInfo;
    [ObservableProperty]
    private bool _autoRefreshWeatherLocation;
    [ObservableProperty]
    private string _excludedWeatherAlerts = string.Empty;
}
