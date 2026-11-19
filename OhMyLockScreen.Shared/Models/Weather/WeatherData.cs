using System.Text.Json.Serialization;

namespace OhMyLockScreen.Shared.Models.Weather;

public enum WeatherLocationMode
{
    City,
    Coordinates
}

public enum WeatherIconStyle
{
    Fluent,
    Text
}

public enum WeatherAlertTextMode
{
    Never,
    WithoutSpecificIcon,
    Always
}

public sealed record WeatherCity(string Id, string Name)
{
    public static WeatherCity Beijing { get; } = new("weathercn:101010100", "北京（北京，中国）");
}

public readonly record struct WeatherCoordinates(double Latitude, double Longitude)
{
    [JsonIgnore]
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;

    public WeatherCoordinates Round(int precision) => new(Math.Round(Latitude, precision), Math.Round(Longitude, precision));
}

public sealed record WeatherLocation
{
    public WeatherLocationMode Mode { get; init; }
    public WeatherCity City { get; init; } = WeatherCity.Beijing;
    public WeatherCoordinates? Coordinates { get; init; }

    [JsonIgnore]
    public WeatherQuery Query => Mode == WeatherLocationMode.Coordinates
        ? new(null, Coordinates is { IsValid: true } value ? value.Round(4) : null)
        : new(City?.Id ?? WeatherCity.Beijing.Id, null);
}

public readonly record struct WeatherQuery(string? CityId, WeatherCoordinates? Coordinates)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(CityId) || Coordinates is { IsValid: true };
}

public sealed record WeatherSummary(
    string CityName, string? Condition, string? Temperature, string? Humidity, string? Wind,
    string? AirQuality, string? Pressure, string? FeelsLike, DateTimeOffset UpdatedAt, string? WindDirection = null);

public sealed record WeatherAlert(string Type, string Title, string Detail, DateTimeOffset PublishedAt, Uri? Icon, bool IsDefaultIcon, string? Level = null);

public sealed record WeatherReading(WeatherSummary Summary, IReadOnlyList<WeatherAlert> Alerts, int RainMinutes, WeatherQuery Location);

public sealed record WeatherStatusCode(int Code, string Weather);

public sealed record WeatherQueryResult(bool IsSuccess = false, string? ErrorMessage = null, bool IsPrecisionDegraded = false, int DegradedPrecision = 4);


