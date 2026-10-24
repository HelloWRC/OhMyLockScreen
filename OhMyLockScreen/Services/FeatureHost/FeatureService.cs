using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.FeatureHost;

namespace OhMyLockScreen.Services.FeatureHost;

public sealed class FeatureService(
    FeatureRegistryService registry,
    IServiceProvider services,
    ILogger<FeatureService> logger) : IFeatureService
{
    public FeatureBase? CreateFeature(FeatureInfo settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var info = Find(settings.Id);
        if (info == null)
        {
            return null;
        }

        var clientSettings = info.ClientSettingsType != null ? settings.GetSettingsOrCreate(info.ClientSettingsType) : null;
        var feature = (FeatureBase)services.GetRequiredService(info.FeatureType);
        feature.SetSettings(clientSettings);
        settings.Settings = clientSettings;
        return feature;
    }

    public ControlWithSettingsBase? CreateSettingsControl(FeatureInfo settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var info = Find(settings.Id);
        if (info?.SettingsType == null)
        {
            return null;
        }

        var clientSettings = info.ClientSettingsType != null ? settings.GetSettingsOrCreate(info.ClientSettingsType) : null;
        var control = (ControlWithSettingsBase)services.GetRequiredService(info.SettingsType);
        control.SetSettings(clientSettings);
        control.DataContext = clientSettings;
        settings.Settings = clientSettings;
        return control;
    }

    private FeatureRegisterInfo? Find(string id)
    {
        var info = registry.Find(id);
        if (info == null)
        {
            logger.LogWarning("未注册特性：{FeatureId}", id);
        }

        return info;
    }
}
