using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Hosting;
using OhMyLockScreen.Services;
using Windows.Storage;

namespace OhMyLockScreen;

public sealed partial class App
{
    private static IHost CreateHost()
    {
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
            
    }
}