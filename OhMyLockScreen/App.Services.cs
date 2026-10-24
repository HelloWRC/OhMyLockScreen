using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Controls.Scene;
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
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;

namespace OhMyLockScreen;

public sealed partial class App
{
    private static IHost CreateHost()
    {
        AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
        AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(App).Assembly);

        return new HostBuilder()
            .ConfigureLogging(logging => logging.AddDebug())
            .ConfigureServices(ConfigureServices)
            .Build();
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<IHostLifetime, UwpHostLifetime>();
        services.AddSingleton(provider => new SettingsService(
            provider.GetRequiredService<ILogger<SettingsService>>(),
            Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json")));
        services.AddSingleton<ComponentRegistryService>();
        services.AddSingleton<FeatureRegistryService>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<IComponentsService, ComponentsService>();
        // Components
        services.AddComponent<TextComponent>();
        // Features
        services.AddFeature<LayoutFeature>();
        services.AddFeature<GridLayoutFeature>();
    }
}
