using FluentIcons.Common;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Enums.SettingsPages;

namespace OhMyLockScreen.Controls.Components
{
    public sealed class WeatherComponentSettingsControl : ControlWithSettingsBase<WeatherComponentSettings>;

    [ComponentInfo(Id, "天气简报", Icon.WeatherSunny)]
    public sealed class WeatherComponent : ComponentBase<WeatherComponentSettings>
    {
        public const string Id = "omls.components.weather";
        public WeatherComponent(IWeatherService weather, SettingsService settings) { }
    }
}

namespace OhMyLockScreen.Views.SettingsPages
{
    [SettingsPageInfo("weather", "天气", Icon.WeatherPartlyCloudyDay, SettingsPageCategory.Internal)]
    public sealed class WeatherSettingsPage : SettingsPageBase;
}

namespace Windows.Devices.Geolocation
{
    public enum GeolocationAccessStatus { Unspecified, Allowed, Denied }

    public sealed class Geolocator
    {
        public static GeolocationAccessStatus AccessStatus { get; set; }
        public static int AccessRequests { get; set; }

        public static Operation<GeolocationAccessStatus> RequestAccessAsync()
        {
            AccessRequests++;
            return new(AccessStatus);
        }

        public Operation<Geoposition> GetGeopositionAsync(TimeSpan maximumAge, TimeSpan timeout) => new(new());
    }

    public sealed class Operation<T>(T result)
    {
        public Task<T> AsTask(CancellationToken token) => token.IsCancellationRequested ? Task.FromCanceled<T>(token) : Task.FromResult(result);
    }

    public sealed class Geoposition { public Geocoordinate Coordinate { get; } = new(); }
    public sealed class Geocoordinate { public Geopoint Point { get; } = new(); }
    public sealed class Geopoint { public BasicGeoposition Position { get; } = new(23.11111, 113.22222); }
    public sealed record BasicGeoposition(double Latitude, double Longitude);
}
