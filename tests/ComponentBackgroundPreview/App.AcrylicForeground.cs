using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.Audio;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Services.Media;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Services.SystemStatus;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.SystemStatus;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchAcrylicForegroundPreview()
    {
        try
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(App).Assembly);
            var settingsPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "acrylic-test-settings.json");
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            var dispatcher = Window.Current.Dispatcher;

            IAppHost.Host = new HostBuilder().ConfigureLogging(logging => logging.AddProvider(new ProbeLoggerProvider(_report))).ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddWeatherIconTemplate(WeatherIconIds.Lucide, "Lucide（默认）", "LucideWeatherIconTemplate");
                services.AddWeatherIconTemplate(WeatherIconIds.FluentDesign, "Fluent Design", "FluentDesignWeatherIconTemplate");
                services.AddWeatherIconTemplate(WeatherIconIds.SimpleText, "纯文本", "SimpleTextWeatherIconTemplate");

                services.AddSingleton(provider => new SettingsService(provider.GetRequiredService<ILogger<SettingsService>>(), settingsPath));
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton(provider => new WeatherService(
                    provider.GetRequiredService<SettingsService>(),
                    new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<WeatherService>>(),
                    dispatcher));
                services.AddSingleton<IWeatherService>(provider => provider.GetRequiredService<WeatherService>());
                services.AddSingleton<ISystemStatusSource, WindowsSystemStatusSource>();
                services.AddSingleton<SystemStatusService>();
                services.AddSingleton<ISystemStatusService>(provider => provider.GetRequiredService<SystemStatusService>());
                services.AddSingleton<IMediaSessionSource, WindowsMediaSessionSource>();
                services.AddSingleton<MediaStateService>();
                services.AddSingleton<IMediaStateService>(provider => provider.GetRequiredService<MediaStateService>());
                services.AddSingleton<IAudioSpectrumSource, WindowsAudioSpectrumSource>();
                services.AddSingleton<AudioSpectrumService>();
                services.AddSingleton<IAudioSpectrumService>(provider => provider.GetRequiredService<AudioSpectrumService>());
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddFeature<BackgroundFeature>();
                services.AddFeature<BorderFeature>();
                services.AddFeature<TextFeature>();
                services.AddFeature<FontFeature>();
                services.AddFeature<ForegroundFeature>();
                services.AddFeature<LayoutFeature>();
                services.AddFeature<GridLayoutFeature>();
                services.AddComponent<TextComponent>();
                services.AddComponent<ClockComponent>();
                services.AddComponent<DateComponent>();
                services.AddComponent<ContainerComponent>();
                services.AddComponent<SystemStatusComponent>();
                services.AddComponent<WeatherComponent>();
                services.AddComponent<MediaComponent>();
                services.AddComponent<AudioSpectrumComponent>();
            }).Build();

            var settingsService = IAppHost.GetRequiredService<SettingsService>();
            var location = new WeatherLocation { Mode = WeatherLocationMode.City, City = WeatherCity.Beijing };
            var query = location.Query;
            settingsService.Settings.WeatherLocation = location;
            settingsService.Settings.LastWeatherInfo = new WeatherReading(
                new WeatherSummary("北京", "0", "18℃", "45%", "10 km/h", "30", "1018 hPa", "18℃", DateTimeOffset.Now, "180"),
                [], 0, query);

            var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 24, 34, 52)), Width = 1280, Height = 720 };
            Window.Current.Content = root;
            Window.Current.Activate();
            await Task.Delay(150);

            // Case 1: Editor -> Preview -> Editor with DefaultScene + Weather in nested container
            var scene1 = BuildSceneWithWeather();
            var editorSurface = new ScenePresenter { IsEditing = true, Scene = scene1, Width = 1280, Height = 720 };
            root.Children.Add(editorSurface);
            await WaitLayoutAsync(root, 400);
            await ProbeSceneAsync("Case1-Editor-Initial", editorSurface);

            editorSurface.IsEditing = false;
            await WaitLayoutAsync(root, 300);
            await ProbeSceneAsync("Case1-Preview", editorSurface);

            editorSurface.IsEditing = true;
            await WaitLayoutAsync(root, 300);
            await ProbeSceneAsync("Case1-ExitPreview", editorSurface);

            root.Children.Remove(editorSurface);
            await Task.Delay(100);

            // Case 2: LockScreen-like MainPage (IsEditing=false, Scene set after Loaded, WindowTransparency enabled)
            WindowTransparency.Enable(Window.Current);
            root.Background = new SolidColorBrush(Colors.Transparent);
            var lockSurface = new ScenePresenter { Width = 1280, Height = 720 };
            lockSurface.Loaded += (_, _) => lockSurface.Scene = BuildSceneWithWeather();
            root.Children.Add(lockSurface);
            await WaitLayoutAsync(root, 600);
            await ProbeSceneAsync("Case2-LockScreen-Initial", lockSurface);

            // Simulate async WeatherService refresh completing (IsWeatherRefreshed false -> true, condition changes)
            var weatherService = IAppHost.GetRequiredService<WeatherService>();
            settingsService.Settings.LastWeatherInfo = new WeatherReading(
                new WeatherSummary("北京", "1", "19℃", "48%", "12 km/h", "32", "1017 hPa", "19℃", DateTimeOffset.Now, "90"),
                [], 0, query);
            weatherService.IsWeatherRefreshed = true;
            await WaitLayoutAsync(root, 300);
            await ProbeSceneAsync("Case2-LockScreen-AfterWeatherRefresh", lockSurface);

            // Case 3: Direct SystemStatus Volume/Bluetooth (async update) under AcrylicBrush
            var scene3 = new SceneSettings
            {
                Layers =
                [
                    new LayerSettings
                    {
                        Kind = LayoutKind.StackPanel,
                        Spacing = 12,
                        Features =
                        [
                            new FeatureInfo
                            {
                                Id = "omls.features.appearance.foreground",
                                Settings = new ForegroundFeatureSettings
                                {
                                    Foreground = new AcrylicBrush
                                    {
                                        BackgroundSource = AcrylicBackgroundSource.Backdrop,
                                        TintColor = Colors.White,
                                        TintOpacity = 0.8,
                                        FallbackColor = Colors.White
                                    }
                                }
                            }
                        ],
                        Children =
                        [
                            new ComponentSettings { Id = SystemStatusComponent.Id, Settings = new SystemStatusComponentSettings { Kind = SystemStatusKind.Battery, ShowIcon = true, ShowText = true, IconSize = 32 } },
                            new ComponentSettings { Id = SystemStatusComponent.Id, Settings = new SystemStatusComponentSettings { Kind = SystemStatusKind.Network, ShowIcon = true, ShowText = true, IconSize = 32 } },
                            new ComponentSettings { Id = SystemStatusComponent.Id, Settings = new SystemStatusComponentSettings { Kind = SystemStatusKind.Volume, ShowIcon = true, ShowText = true, IconSize = 32 } },
                            new ComponentSettings { Id = SystemStatusComponent.Id, Settings = new SystemStatusComponentSettings { Kind = SystemStatusKind.Bluetooth, ShowIcon = true, ShowText = true, IconSize = 32 } },
                            new ComponentSettings { Id = SystemStatusComponent.Id, Settings = new SystemStatusComponentSettings { Kind = SystemStatusKind.Battery, ShowIcon = true, ShowText = false, IconSize = 32 } }
                        ]
                    }
                ]
            };
            root.Children.Remove(lockSurface);
            var statusSurface = new ScenePresenter { Scene = scene3, Width = 600, Height = 400 };
            root.Children.Add(statusSurface);
            await WaitLayoutAsync(root, 800);
            await ProbeSceneAsync("Case3-AllSystemStatus-Acrylic", statusSurface);

            // Case 4: SystemStatus under AcrylicBrush after post-render updates (Kind/ShowText/ShowIcon)
            var ssPresenters = Descendants(statusSurface).OfType<ComponentPresenter>()
                .Where(p => p.Content is SystemStatusComponent).ToArray();
            var batteryComp = (SystemStatusComponent)ssPresenters[0].Content;
            batteryComp.Settings.ShowText = false;
            await WaitLayoutAsync(root, 200);
            await ProbeSceneAsync("Case4-AfterShowTextFalse", statusSurface);
            batteryComp.Settings.ShowText = true;
            await WaitLayoutAsync(root, 200);
            await ProbeSceneAsync("Case4-AfterShowTextTrue", statusSurface);
            batteryComp.Settings.Kind = SystemStatusKind.Volume;
            await WaitLayoutAsync(root, 300);
            await ProbeSceneAsync("Case4-AfterKindChangeToVolume", statusSurface);

            // Case 5: Multi-view (secondary CoreApplicationView, like LockScreen activating while Editor is alive)
            var secondViewDone = new TaskCompletionSource();
            var secondView = Windows.ApplicationModel.Core.CoreApplication.CreateNewView();
            await secondView.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _ = RunSecondViewAsync();
            });
            await secondViewDone.Task;

            async Task RunSecondViewAsync()
            {
                var v2Dispatcher = secondView.Dispatcher;
                try
                {
                    WindowTransparency.Enable(Window.Current);
                    var v2Root = new Grid { Width = 1280, Height = 720, Background = new SolidColorBrush(Colors.Transparent) };
                    var v2Surface = new ScenePresenter { Width = 1280, Height = 720 };
                    v2Surface.Loaded += (_, _) => v2Surface.Scene = BuildSceneWithWeather();
                    v2Root.Children.Add(v2Surface);
                    Window.Current.Content = v2Root;
                    Window.Current.Activate();
                    await Task.Delay(700).ConfigureAwait(false);
                    var probeDone = new TaskCompletionSource();
                    await v2Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        try
                        {
                            v2Root.UpdateLayout();
                            await ProbeSceneAsync("Case5-SecondView-Initial", v2Surface);
                        }
                        catch (Exception ex)
                        {
                            _report.Add("Case5-Probe-FAIL: " + ex);
                        }
                        finally
                        {
                            probeDone.TrySetResult();
                        }
                    });
                    await probeDone.Task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _report.Add("Case5-FAIL: " + ex);
                }
                finally
                {
                    secondViewDone.TrySetResult();
                }
            }
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        var reportFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("acrylic-foreground-report.txt", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(reportFile, string.Join(Environment.NewLine, _report));
        Application.Current.Exit();
    }

    private static SceneSettings BuildSceneWithWeather()
    {
        static AcrylicBrush MakeAcrylic(double tintOpacity) => new()
        {
            BackgroundSource = AcrylicBackgroundSource.Backdrop,
            TintColor = Colors.White,
            TintOpacity = tintOpacity,
            FallbackColor = Colors.White
        };

        return new SceneSettings
        {
            Layers =
            [
                new LayerSettings
                {
                    Name = "Content",
                    Kind = LayoutKind.Grid,
                    Spacing = 8,
                    Features =
                    [
                        new FeatureInfo
                        {
                            Id = "omls.features.appearance.foreground",
                            Settings = new ForegroundFeatureSettings { Foreground = MakeAcrylic(0.8) }
                        }
                    ],
                    Children =
                    [
                        new ComponentSettings
                        {
                            Id = ContainerComponent.Id,
                            Features =
                            [
                                new FeatureInfo
                                {
                                    Id = "omls.features.layout.layout",
                                    Settings = new OhMyLockScreen.Models.Features.Layout.LayoutFeatureSettings
                                    {
                                        IsAutoWidth = true, IsAutoHeight = true,
                                        VerticalAlignment = VerticalAlignment.Bottom,
                                        HorizontalAlignment = HorizontalAlignment.Left,
                                        Margin = new Thickness(64, 0, 0, 64)
                                    }
                                },
                                new FeatureInfo
                                {
                                    Id = "omls.features.appearance.foreground",
                                    Settings = new ForegroundFeatureSettings { Foreground = MakeAcrylic(0.75) }
                                }
                            ],
                            Settings = new ContainerComponentSettings
                            {
                                Kind = LayoutKind.StackPanel,
                                Orientation = Orientation.Vertical,
                                Children =
                                [
                                    new ComponentSettings
                                    {
                                        Id = ClockComponent.Id,
                                        Features = [new FeatureInfo { Id = TextFeature.Id, Settings = new TextFeatureSettings { FontSize = 90 } }]
                                    },
                                    new ComponentSettings
                                    {
                                        Id = DateComponent.Id,
                                        Features = [new FeatureInfo { Id = TextFeature.Id, Settings = new TextFeatureSettings { FontSize = 28 } }]
                                    }
                                ]
                            }
                        },
                        new ComponentSettings
                        {
                            Id = ContainerComponent.Id,
                            Features =
                            [
                                new FeatureInfo
                                {
                                    Id = "omls.features.layout.layout",
                                    Settings = new OhMyLockScreen.Models.Features.Layout.LayoutFeatureSettings
                                    {
                                        IsAutoWidth = true, IsAutoHeight = true,
                                        VerticalAlignment = VerticalAlignment.Bottom,
                                        HorizontalAlignment = HorizontalAlignment.Right,
                                        Margin = new Thickness(0, 0, 64, 64)
                                    }
                                }
                            ],
                            Settings = new ContainerComponentSettings
                            {
                                Kind = LayoutKind.StackPanel,
                                Spacing = 16,
                                Orientation = Orientation.Vertical,
                                Children =
                                [
                                    new ComponentSettings
                                    {
                                        Id = WeatherComponent.Id,
                                        Features = [new FeatureInfo { Id = TextFeature.Id, Settings = new TextFeatureSettings { FontSize = 18 } }],
                                        Settings = new WeatherComponentSettings { ShowSummary = true, ShowAlerts = true, ShowRainTime = true }
                                    },
                                    new ComponentSettings
                                    {
                                        Id = ContainerComponent.Id,
                                        Settings = new ContainerComponentSettings
                                        {
                                            Kind = LayoutKind.StackPanel,
                                            Spacing = 12,
                                            Orientation = Orientation.Horizontal,
                                            Children =
                                            [
                                                new ComponentSettings
                                                {
                                                    Id = SystemStatusComponent.Id,
                                                    Features = [new FeatureInfo { Id = TextFeature.Id, Settings = new TextFeatureSettings { FontSize = 16 } }],
                                                    Settings = new SystemStatusComponentSettings { Kind = SystemStatusKind.Battery, ShowIcon = true, ShowText = true, IconSize = 32 }
                                                },
                                                new ComponentSettings
                                                {
                                                    Id = SystemStatusComponent.Id,
                                                    Features = [new FeatureInfo { Id = TextFeature.Id, Settings = new TextFeatureSettings { FontSize = 16 } }],
                                                    Settings = new SystemStatusComponentSettings { Kind = SystemStatusKind.Network, ShowIcon = true, ShowText = true, IconSize = 32 }
                                                }
                                            ]
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        };
    }

    private static async Task WaitLayoutAsync(FrameworkElement root, int delayMs)
    {
        root.UpdateLayout();
        await Task.Delay(delayMs);
        root.UpdateLayout();
        await Task.Delay(50);
    }

    private async Task ProbeSceneAsync(string stage, ScenePresenter surface)
    {
        foreach (var presenter in Descendants(surface).OfType<ComponentPresenter>())
        {
            if (presenter.Content == null)
            {
                _report.Add($"{stage} | NULL_CONTENT | id={presenter.Settings?.Id} | presenterFg={presenter.ContentForeground?.GetType().Name ?? "null"}");
                throw new InvalidOperationException($"{stage}: {presenter.Settings?.Id} did not load.");
            }

            if (presenter.Content is ClockComponent or DateComponent or SystemStatusComponent or WeatherComponent)
            {
                var content = (FrameworkElement)presenter.Content;
                var name = content.GetType().Name;
                if (content is SystemStatusComponent ss)
                {
                    name += $"({ss.Settings.Kind},text={ss.Settings.ShowText})";
                }

                var painted = await CountPaintedPixelsAsync(content);
                var label = content switch
                {
                    WeatherComponent weather => (TextBlock)weather.TextElement,
                    SystemStatusComponent status when status.Settings.ShowText => (TextBlock)status.TextElement,
                    _ => null
                };
                if (label != null)
                {
                    var labelPixels = await CountPaintedPixelsAsync(label);
                    _report.Add($"{stage} | {name} | labelPixels={labelPixels}");
                    if (labelPixels <= 0 || label.Foreground is not AcrylicBrush)
                    {
                        throw new InvalidOperationException($"{stage}: {name} text is transparent or lost its acrylic foreground.");
                    }
                }
                var fgType = presenter.ContentForeground?.GetType().Name ?? "null";
                var textBlocks = Descendants(content).OfType<TextBlock>().ToArray();
                var textInfo = string.Join(" | ", textBlocks.Select(tb => $"'{tb.Text}'(vis={tb.Visibility},fg={tb.Foreground?.GetType().Name ?? "null"})"));
                _report.Add($"{stage} | {name} | size={content.ActualWidth:0}x{content.ActualHeight:0} | painted={painted} | presenterFg={fgType} | texts=[{textInfo}]");
            }
        }
    }

    private static async Task<int> CountPaintedPixelsAsync(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return -1;
        }

        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 0)
            {
                count++;
            }
        }
        return count;
    }

    private sealed class ProbeLoggerProvider(List<string> report) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ProbeLogger(categoryName, report);
        public void Dispose() { }
    }

    private sealed class ProbeLogger(string category, List<string> report) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (report)
            {
                report.Add($"LOG[{logLevel}][{category}]: {formatter(state, exception)} {exception}");
            }
        }
    }
}
