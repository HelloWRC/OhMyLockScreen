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
        services.AddSingleton<ISceneService>(provider => new SceneService(provider.GetRequiredService<SettingsService>(), provider.GetRequiredService<ILogger<SceneService>>(), ApplicationData.Current.LocalFolder.Path));
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<SceneEditorViewModel>();
        services.AddSingleton<ComponentRegistryService>();
        services.AddSingleton<FeatureRegistryService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<IComponentsService, ComponentsService>();
        // Components
        services.AddComponent<TextComponent, TextSettingsControl>();
        services.AddComponent<ClockComponent, ClockSettingsControl>();
        services.AddComponent<DateComponent>();
        services.AddComponent<ContainerComponent, ContainerSettingsControl>();
        // Features
        services.AddFeature<LayoutFeature, LayoutSettingsControl>();
        services.AddFeature<GridLayoutFeature, GridLayoutSettingsControl>();
        services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
        services.AddFeature<BorderFeature, BorderSettingsControl>();
        services.AddFeature<ForegroundFeature, ForegroundSettingsControl>();
    }
}
