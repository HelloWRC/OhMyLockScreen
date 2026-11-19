using System.Collections.ObjectModel;
using System.ComponentModel;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Abstractions.Services.Weather;

public interface IWeatherService : INotifyPropertyChanged
{
    static ObservableCollection<WeatherIconTemplateRegistryInfo> RegisteredTemplates { get; } = [];
    WeatherIconTemplateRegistryInfo? SelectedWeatherIconTemplate { get; }
    bool IsWeatherRefreshed { get; set; }
    bool IsPosUpdated { get; set; }
    IReadOnlyList<WeatherStatusCode> WeatherStatusList { get; }
    Task QueryWeatherAsync();
    Task<WeatherQueryResult> QueryWeatherWithResultAsync();
    string GetWeatherTextByCode(string? code);
    Task<List<WeatherCity>> GetCitiesByName(string text);
}
