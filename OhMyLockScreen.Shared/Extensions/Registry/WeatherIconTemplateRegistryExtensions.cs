using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Models.Weather;

namespace OhMyLockScreen.Shared.Extensions.Registry;

public static class WeatherIconTemplateRegistryExtensions
{
    public static IServiceCollection AddWeatherIconTemplate(this IServiceCollection services, string id, string name, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (!IWeatherService.RegisteredTemplates.Any(entry => entry.Id == id))
        {
            IWeatherService.RegisteredTemplates.Add(new WeatherIconTemplateRegistryInfo(id, name, resourceKey));
        }

        return services;
    }
}
