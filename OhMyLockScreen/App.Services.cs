using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.ViewModels;
using OhMyLockScreen.Hosting;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.Storage;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Services.Media;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Services.SystemStatus;
using OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;
using OhMyLockScreen.Services.Audio;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Views.SettingsPages;

namespace OhMyLockScreen;

public sealed partial class App
{
    private static IHost CreateHost()
    {
        AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
        AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(App).Assembly);
        return new HostBuilder().ConfigureLogging(logging => logging.AddDebug()).ConfigureServices(ConfigureServices).Build();
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<IHostLifetime, UwpHostLifetime>();
        services.AddSingleton(provider => new SettingsService(provider.GetRequiredService<ILogger<SettingsService>>(), Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json")));
        services.AddSingleton(provider => new SceneService(provider.GetRequiredService<SettingsService>(), provider.GetRequiredService<ILogger<SceneService>>(), ApplicationData.Current.LocalFolder.Path));
        services.AddSingleton<ISceneService>(provider => provider.GetRequiredService<SceneService>());
        services.AddSingleton<ISceneBlobStorageService, SceneBlobStorageService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAudioSpectrumSource, WindowsAudioSpectrumSource>();
        services.AddSingleton<AudioSpectrumService>();
        services.AddSingleton<IAudioSpectrumService>(provider => provider.GetRequiredService<AudioSpectrumService>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<AudioSpectrumService>());
        services.AddSingleton<ISystemStatusSource, WindowsSystemStatusSource>();
        services.AddSingleton<SystemStatusService>();
        services.AddSingleton<ISystemStatusService>(provider => provider.GetRequiredService<SystemStatusService>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<SystemStatusService>());
        services.AddSingleton<IMediaSessionSource, WindowsMediaSessionSource>();
        services.AddSingleton<MediaStateService>();
        services.AddSingleton<IMediaStateService>(provider => provider.GetRequiredService<MediaStateService>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<MediaStateService>());
        services.AddSingleton<FontCatalogService>();
        services.AddTransient<SceneEditorViewModel>();
        services.AddSingleton<ComponentRegistryService>();
        services.AddSingleton<FeatureRegistryService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<IComponentsService, ComponentsService>();
        services.AddSettingsPage<GeneralSettingsPage>();
        services.AddSettingsPage<AboutSettingsPage>();
        // Components
        services.AddComponent<TextComponent, TextSettingsControl>();
        services.AddComponent<ClockComponent, ClockSettingsControl>();
        services.AddComponent<DateComponent>();
        services.AddComponent<SystemStatusComponent, SystemStatusSettingsControl>();
        services.AddComponent<AudioSpectrumComponent, AudioSpectrumSettingsControl>();
        services.AddComponent<ContainerComponent, ContainerSettingsControl>();
        services.AddComponent<MediaComponent, MediaSettingsControl>();
        services.AddComponent<MediaTextComponent, MediaTextSettingsControl>();
        services.AddComponent<MediaControllerComponent, MediaControllerSettingsControl>();
        services.AddComponent<MediaCoverComponent, MediaCoverSettingsControl>();
        services.AddComponent<MediaProgressComponent, MediaProgressSettingsControl>();
        // Features
        services.AddFeature<LayoutFeature, LayoutSettingsControl>();
        services.AddFeature<GridLayoutFeature, GridLayoutSettingsControl>();
        services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
        services.AddFeature<BorderFeature, BorderSettingsControl>();
        services.AddFeature<ForegroundFeature, ForegroundSettingsControl>();
        services.AddFeature<TextFeature, TextFeatureSettingsControl>();
        services.AddFeature<FontFeature, FontSettingsControl>();
    }
}
