using System.Globalization;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Weather;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.Weather;
using OhMyLockScreen.Shared.Services.Registry;
using OhMyLockScreen.Views.SettingsPages;
using Windows.Devices.Geolocation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class WeatherTests
{
    public static void Run(Action<string, Action> run)
    {
        run("Weather and its settings page use the normal production registrations", () =>
        {
            var factory = typeof(App).GetMethod("CreateHost", BindingFlags.Static | BindingFlags.NonPublic)!;
            using var host = (IHost)factory.Invoke(null, null)!;
            var components = host.Services.GetRequiredService<IComponentsService>();
            var model = new ComponentSettings { Id = WeatherComponent.Id, Settings = new WeatherComponentSettings { ShowAlerts = false } };
            Check(components.CreateComponent(model) is WeatherComponent, "Weather component was not registered.");
            Check(ConfigureFileHelper.CopyObject(model).Settings is WeatherComponentSettings { ShowAlerts: false, ShowRainTime: true },
                "Weather component settings did not round-trip.");
            Check(host.Services.GetRequiredService<SettingsPageRegistry>().Find("weather")?.PageType == typeof(WeatherSettingsPage),
                "Frame cannot discover the weather page.");
            Check(ReferenceEquals(host.Services.GetRequiredService<IWeatherService>(), host.Services.GetServices<IHostedService>().OfType<WeatherService>().Single()),
                "The UI and timer resolved different weather services.");
        });

        run("Settings drawer commands route to the active settings window and apply the drawer context", () =>
        {
            var first = Window.Current;
            var second = new Window();
            var page = new DrawerPage();
            var firstFrame = new Frame { Content = page };
            var secondFrame = new Frame { Content = new DrawerPage() };
            object? firstContent = null;
            object? secondContent = null;
            using var firstRoute = SettingsPageBase.AttachDrawerRoute(first, firstFrame, content => firstContent = content, () => firstContent = null);
            using var secondRoute = SettingsPageBase.AttachDrawerRoute(second, secondFrame, content => secondContent = content, () => secondContent = null);
            page.Resources["Cities"] = new DataTemplate(() => new FrameworkElement());
            page.Open("Cities");
            Check(firstContent is FrameworkElement element && ReferenceEquals(element.DataContext, page), "Drawer resource or context was lost.");
            Window.Current = second;
            SettingsPageBase.OpenDrawerCommand.Execute("second drawer");
            Check(Equals(secondContent, "second drawer") && firstContent != null, "The command crossed settings windows.");
            SettingsPageBase.CloseDrawerCommand.Execute(null);
            Check(secondContent == null && firstContent != null, "Closing affected another window.");
            Window.Current = first;
        });

        run("Registered weather templates are selected by their persistent ClassIsland ID", () =>
        {
            using var settings = CreateSettings();
            using var service = CreateService(settings, new WeatherHttp());
            const string resourceKey = "TestWeatherTemplate";
            try
            {
                new ServiceCollection().AddWeatherIconTemplate("test.weather", "测试图标", resourceKey);
                settings.Settings.WeatherIconId = "test.weather";
                Check(service.SelectedWeatherIconTemplate?.ResourceKey == resourceKey, "The selected template did not come from the registry.");
                Check(service.GetWeatherTextByCode("32") == "飑", "The original weather code table was lost.");
            }
            finally
            {
                IWeatherService.RegisteredTemplates.Remove(IWeatherService.RegisteredTemplates.Single(item => item.Id == "test.weather"));
            }
        });

        run("Rain presentation uses the ClassIsland signed countdown and half-hour rounding", () =>
        {
            Check(WeatherPresentation.RainText(83, true) == $"~{1.5:0.#}h", "Long rain time was not rounded to a half hour.");
            Check(WeatherPresentation.RainText(-83, false) == "-83min", "Exact remaining rain time lost its sign.");
            Check(WeatherService.GetRainMinutes([1, 1, 0]) == -2 && WeatherService.GetRainMinutes([0, 0, 1]) == 2,
                "Rain series produced incorrect transitions.");
        });

        run("Empty control uses independent per-instance default sticker icons and supports custom IconSource", () =>
        {
            var first = new Empty();
            var second = new Empty();
            Check(Equals(first.DefaultStyleKey, typeof(Empty)), "Empty did not bind its default style key.");
            Check(first.Text == "啥都没有" && first.IconWidth == 64.0 && first.IconHeight == 64.0,
                "Empty defaults did not match ClassIsland.");
            Check(first.Icon is BitmapIconSource
            {
                UriSource.OriginalString: "ms-appx:///Assets/Stickers/水月_疑惑.webp",
                ShowAsMonochrome: false
            }, "Empty did not initialize its default sticker icon source.");
            Check(second.Icon is BitmapIconSource && !ReferenceEquals(first.Icon, second.Icon),
                "Empty instances shared a single IconSource instance.");
            var custom = new FluentIcons.Uwp.FluentIconSource { Icon = FluentIcons.Common.Icon.Warning, FontSize = 48 };
            first.Icon = custom;
            Check(ReferenceEquals(first.Icon, custom), "Empty did not accept a custom IconSource.");
        });
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> run)
    {
        await run("Weather uses one five-minute dispatcher timer and persists its latest successful reading in Settings", async () =>
        {
            using var settings = CreateSettings();
            settings.ReloadSettings();
            settings.Settings.ExcludedWeatherAlerts = "排除";
            var handler = new WeatherHttp();
            using var service = CreateService(settings, handler);
            await service.StartAsync(default);
            var timer = DispatcherTimer.LastCreated!;
            Check(timer.Interval == TimeSpan.FromMinutes(5) && timer.IsEnabled && handler.Requests.Count == 2,
                "Weather did not query at startup and start the five-minute timer.");
            Check(settings.Settings.LastWeatherInfo is { Summary.Temperature: "26℃", Alerts.Count: 2 } info &&
                info.Alerts[0].Title == "新预警" && info.Alerts[0].Icon?.Scheme == "https" && info.Alerts[0].Level == "蓝色" &&
                info.Alerts[1].Title == "暴雨黄色预警" && info.Alerts[1].Icon?.Host == "cdn.cnbj1.fds.api.mi-img.com" && info.Alerts[1].Level == "黄色",
                "The weather or alert filtering did not reach Settings.");
            Check(!WeatherService.IsAllowedXiaomiImageHost("evil-mi-img.com") && WeatherService.IsAllowedXiaomiImageHost("cdn.cnbj1.fds.api.mi-img.com"),
                "Xiaomi alert image host validation was too loose or too narrow.");

            handler.FailWeather = true;
            timer.Advance();
            Check(!service.IsWeatherRefreshed && service.LastWeatherInfo?.Summary.Temperature == "26℃",
                "A failed refresh erased the last successful weather.");
            await service.StopAsync(default);
            settings.ReloadSettings();
            Check(!timer.IsEnabled && settings.Settings.LastWeatherInfo != null, "Stopping lost the persisted weather or left a timer active.");
        });

        await run("Xiaomi city search is escaped and manual coordinates fall back to lower precision", async () =>
        {
            var handler = new WeatherHttp();
            using var settings = CreateSettings();
            using var service = CreateService(settings, handler);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Check((await service.GetCitiesByName(string.Empty)).Count == 1, "The hot cities were not loaded.");
                Check((await service.GetCitiesByName(" 广州 & 市 ")).Single().Name == "广州（广东）", "City search lost the result.");
                settings.Settings.WeatherLocation = new WeatherLocation { Mode = WeatherLocationMode.Coordinates, Coordinates = new(23.1234, 113.9876) };
                var result = await service.QueryWeatherWithResultAsync();
                Check(result is { IsSuccess: true, IsPrecisionDegraded: true, DegradedPrecision: 3 }, "Coordinate fallback lost its reported precision.");
                Check(handler.Requests.All(uri => uri.Scheme == "https") && handler.Requests[1].Query.Contains(Uri.EscapeDataString("广州 & 市")),
                    "Weather requests were insecure or the city query was unescaped.");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });

        await run("Automatic location is attempted once and preserves coordinates if Windows denies location", async () =>
        {
            using var settings = CreateSettings();
            var initial = new WeatherCoordinates(20, 110);
            settings.Settings.WeatherLocation = new WeatherLocation { Mode = WeatherLocationMode.Coordinates, Coordinates = initial };
            settings.Settings.AutoRefreshWeatherLocation = true;
            using var service = CreateService(settings, new WeatherHttp());
            var window = Window.Current;
            window.Activate();
            Geolocator.AccessRequests = 0;
            Geolocator.AccessStatus = GeolocationAccessStatus.Denied;
            await service.TryUpdateAutomaticallyAsync(window.Dispatcher, () => true);
            await service.TryUpdateAutomaticallyAsync(window.Dispatcher, () => true);
            Check(Geolocator.AccessRequests == 1 && settings.Settings.WeatherLocation.Coordinates == initial && service.AutomaticMessage != null,
                "A denied lookup lost the previous location or repeated the authorization request.");
            Geolocator.AccessStatus = GeolocationAccessStatus.Allowed;
            Check(await service.UpdateFromSettingsAsync(window.Dispatcher, default) == null &&
                settings.Settings.WeatherLocation.Coordinates == new WeatherCoordinates(23.1111, 113.2222), "Manual positioning did not recover.");
        });
    }

    private static SettingsService CreateSettings() => new(NullLogger<SettingsService>.Instance, Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json"));
    private static WeatherService CreateService(SettingsService settings, HttpMessageHandler handler) =>
        new(settings, new HttpClient(handler), TimeProvider.System, NullLogger<WeatherService>.Instance);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class DrawerPage : SettingsPageBase { public void Open(string resource) => OpenDrawer(resource); }

    private sealed class WeatherHttp : HttpMessageHandler
    {
        private const string City = """
            [{"locationKey":"weathercn:101010100","name":"广州","affiliation":"广东","latitude":"23.1234","longitude":"113.9876","status":0}]
            """;
        private const string Reading = """
            {"current":{"weather":"1","temperature":{"value":"26","unit":"℃"}},"updateTime":1795078800000,
             "alerts":[{"title":"旧预警","type":"大风","level":"蓝色","pubTime":"2026-11-19T06:30:00+08:00"},
             {"title":"新预警","type":"大风","level":"蓝色","pubTime":"2026-11-19T07:30:00+08:00","images":
                {"icon":"http://f5.market.xiaomi.com/download/Weather/0ac110d2ee20a454ab44f5df30f9fa6ff650e0b72/a.webp"}},
             {"title":"暴雨黄色预警","type":"暴雨","level":"黄色","pubTime":"2026-11-19T07:45:00+08:00","images":
                {"icon":"https://cdn.cnbj1.fds.api.mi-img.com/weather/alerts/rain-yellow.png"}},
             {"title":"排除的最新预警","type":"大风","level":"红色","pubTime":"2026-11-19T08:30:00+08:00"}],
             "minutely":{"precipitation":{"value":[1,1,0]}}}
            """;
        public bool FailWeather { get; set; }
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            var endpoint = uri.Segments[^1];
            if (endpoint == "all" && FailWeather) return Task.FromException<HttpResponseMessage>(new HttpRequestException());
            var body = endpoint switch
            {
                "hots" or "search" or "info" => City,
                "geo" => uri.Query.Contains("latitude=23.123&") ? City : "[]",
                "all" => Reading,
                _ => throw new InvalidOperationException("Unexpected weather endpoint.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
