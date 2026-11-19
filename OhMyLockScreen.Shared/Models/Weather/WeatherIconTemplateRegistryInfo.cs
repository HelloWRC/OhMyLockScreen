// Adapted from ClassIsland's weather icon registration API, GPL-3.0.

namespace OhMyLockScreen.Shared.Models.Weather;

public sealed class WeatherIconTemplateRegistryInfo(string id, string name, string resourceKey)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string ResourceKey { get; } = resourceKey;
}

public static class WeatherIconIds
{
    public const string Lucide = "classisland.weatherIcons.lucide";
    public const string FluentDesign = "classisland.weatherIcons.fluentDesign";
    public const string SimpleText = "classisland.weatherIcons.simpleText";
}
