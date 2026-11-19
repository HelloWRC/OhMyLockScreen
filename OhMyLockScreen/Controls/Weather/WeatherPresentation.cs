using OhMyLockScreen.Shared.Models.Weather;

namespace OhMyLockScreen.Controls.Weather;

internal static class WeatherPresentation
{
    public static WeatherReading Sample { get; } = new(
        new WeatherSummary("北京", "1", "22℃", "58%", "12 km/h", "38", "1016 hPa", "23℃", DateTimeOffset.MinValue, "90"),
        [new WeatherAlert("大风", "大风蓝色预警", "示例气象预警", DateTimeOffset.MinValue, null, false, "蓝色")], 45, default);

    public static string UpdateText(WeatherSummary summary) => $"更新于 {summary.UpdatedAt.ToLocalTime():M月d日 HH:mm}";

    public static string RainText(int minutes, bool approximate)
    {
        var absolute = Math.Abs(minutes);
        if (!approximate || absolute < 60) return $"{minutes}min";
        var hours = Math.Round(absolute / 30.0, MidpointRounding.AwayFromZero) / 2;
        return minutes < 0 ? $"~-{hours:0.#}h" : $"~{hours:0.#}h";
    }
}
