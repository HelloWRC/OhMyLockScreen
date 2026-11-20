using System.Globalization;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Controls.Weather;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Controls.Rules;

public sealed record WeatherRuleChoice(int Code, string Name, DataTemplate? IconTemplate)
{
    public string IconContent => IconTemplate == null ? Name : Code.ToString(CultureInfo.InvariantCulture);
}

public sealed partial class CurrentWeatherRuleControl : CurrentWeatherRuleControlBase
{
    public IReadOnlyList<WeatherRuleChoice> Choices { get; }

    public CurrentWeatherRuleControl(IWeatherService weather)
    {
        var template = weather.SelectedWeatherIconTemplate is { } info
            ? Application.Current.Resources[info.ResourceKey] as DataTemplate : null;
        Choices = weather.WeatherStatusList.Select(status => new WeatherRuleChoice(status.Code, status.Weather, template)).ToArray();
        InitializeComponent();
    }

    protected override void OnSettingsChanged() => Bindings.Update();

    private void OnIconLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ContentControl { DataContext: WeatherRuleChoice choice } icon && (choice.Name.Contains('雨') || choice.Name == "飑"))
            icon.Foreground = WeatherVisuals.RainBrush(icon);
    }
}
