using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Models.Rules;
using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;

namespace OhMyLockScreen.Services.Rules;

internal sealed class BuiltinRules(IRulesetService rules, WeatherService weather, IMediaStateService media, TimeProvider clock) : IHostedService
{
    internal const string WeatherId = "classisland.weather.currentWeather";
    internal const FluentIcons.Common.Icon RainIcon = FluentIcons.Common.Icon.WeatherRain;
    internal const string RainId = "classisland.weather.rainTime";
    internal const string PlayingId = "omls.media.isPlaying";
    internal const string FieldId = "omls.media.field";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        rules.RegisterRuleHandler(WeatherId, value => value is CurrentWeatherRuleSettings settings && weather.IsWeatherRefreshed &&
            weather.LastWeatherInfo?.Summary.Condition is { } code && MatchesWeather(settings, code));
        rules.RegisterRuleHandler(RainId, value => value is RainTimeRuleSettings settings && MatchesRain(settings, weather.LastWeatherInfo?.RainMinutes ?? 0));
        rules.RegisterRuleHandler(PlayingId, _ => media.State is { HasSession: true, IsPlaying: true });
        rules.RegisterRuleHandler(FieldId, value => value is MediaFieldRuleSettings settings && settings.Matches(media.State, clock.GetUtcNow()));
        weather.PropertyChanged += OnWeatherChanged;
        media.PropertyChanged += OnMediaChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        weather.PropertyChanged -= OnWeatherChanged;
        media.PropertyChanged -= OnMediaChanged;
        return Task.CompletedTask;
    }

    private void OnMediaChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(media.State)) rules.NotifyStatusChanged();
    }

    private void OnWeatherChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(weather.LastWeatherInfo) or nameof(weather.IsWeatherRefreshed)) rules.NotifyStatusChanged();
    }

    internal static bool MatchesRain(RainTimeRuleSettings settings, int minutes)
    {
        var remaining = settings.IsRemainingTime ? -minutes : minutes;
        return remaining > 0 && remaining <= settings.RainTimeMinutes;
    }

    internal static bool MatchesWeather(CurrentWeatherRuleSettings settings, string code)
    {
        var target = settings.WeatherId.ToString(CultureInfo.InvariantCulture);
        return settings.IsFuzzyMatch ? IsWeatherCodeMatched(code, target) : code == target;
    }

    // ClassIsland 的组合天气码按覆盖集合的交集匹配，而不是按名称包含关系匹配。
    private static bool IsWeatherCodeMatched(string actualWeatherCode, string targetWeatherCode)
    {
        if (actualWeatherCode == targetWeatherCode)
        {
            return true;
        }

        var actualCodes = ExpandWeatherCode(actualWeatherCode);
        var targetCodes = ExpandWeatherCode(targetWeatherCode);

        return actualCodes.Overlaps(targetCodes);
    }

    private static HashSet<string> ExpandWeatherCode(string weatherCode)
    {
        return weatherCode switch
        {
            "21" => ["7", "8", "21"],
            "22" => ["8", "9", "22"],
            "23" => ["9", "10", "23"],
            "24" => ["10", "11", "24"],
            "25" => ["11", "12", "25"],
            "26" => ["14", "15", "26"],
            "27" => ["15", "16", "27"],
            "28" => ["16", "17", "28"],
            "301" => ["3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "19", "21", "22", "23", "24", "25", "301"],
            "302" => ["6", "13", "14", "15", "16", "17", "26", "27", "28", "34", "302"],
            _ => [weatherCode]
        };
    }
}
