// Weather code names adapted from ClassIsland's Xiaomi weather data, GPL-3.0.
// https://github.com/ClassIsland/ClassIsland/blob/fb3ad30221d4140bae52cb058439a0668a3cfcdd/ClassIsland/Assets/XiaomiWeather/xiaomi_weather_status.json
using FluentIcons.Common;

namespace OhMyLockScreen.Shared.Models.Weather;

public static class WeatherSymbols
{
    private static readonly string[] ConditionNames =
    [
        "晴", "多云", "阴", "阵雨", "雷阵雨", "雷阵雨并伴有冰雹", "雨夹雪", "小雨", "中雨", "大雨", "暴雨", "大暴雨", "特大暴雨",
        "阵雪", "小雪", "中雪", "大雪", "暴雪", "雾", "冻雨", "沙尘暴", "小雨-中雨", "中雨-大雨", "大雨-暴雨", "暴雨-大暴雨",
        "大暴雨-特大暴雨", "小雪-中雪", "中雪-大雪", "大雪-暴雪", "浮尘", "扬沙", "强沙尘暴", "飑", "龙卷风", "弱高吹雪", "轻雾"
    ];

    public static IReadOnlyList<WeatherStatusCode> StatusList { get; } = Array.AsReadOnly(ConditionNames
        .Select((name, code) => new WeatherStatusCode(code, name))
        .Concat([new(53, "霾"), new(99, "未知"), new(301, "雨"), new(302, "雪")]).ToArray());

    public static string ConditionText(string? code) => int.TryParse(code, out var number) ? number switch
    {
        >= 0 and < 36 => ConditionNames[number],
        53 => "霾",
        301 => "雨",
        302 => "雪",
        _ => "未知"
    } : "未知";

    public static Icon ConditionIcon(string? code) => code switch
    {
        "0" => Icon.WeatherSunny,
        "1" => Icon.WeatherPartlyCloudyDay,
        "2" => Icon.WeatherCloudy,
        "3" => Icon.WeatherRainShowersDay,
        "4" or "5" => Icon.WeatherThunderstorm,
        "6" => Icon.WeatherRainSnow,
        "7" => Icon.WeatherDrizzle,
        "8" or "9" or "10" or "11" or "12" or "21" or "22" or "23" or "24" or "25" or "301" => Icon.WeatherRain,
        "13" => Icon.WeatherSnowShowerDay,
        "14" or "15" or "16" or "17" or "26" or "27" or "28" or "34" or "302" => Icon.WeatherSnow,
        "18" or "35" => Icon.WeatherFog,
        "19" => Icon.WeatherSnowflake,
        "20" or "30" or "31" => Icon.WeatherDuststorm,
        "29" or "53" => Icon.WeatherHaze,
        "32" or "33" => Icon.WeatherSqualls,
        _ => Icon.QuestionCircle
    };
}
