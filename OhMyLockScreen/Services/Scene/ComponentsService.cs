using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Scene;

public sealed class ComponentsService(ComponentRegistryService registry, IServiceProvider services, ILogger<ComponentsService> logger) : IComponentsService
{
    public UserControl? CreateSettingsControl(ComponentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var info = registry.Find(settings.Id);
        if (info?.SettingsType == null)
        {
            return null;
        }

        var model = info.ClientSettingsType != null ? settings.GetSettingsOrCreate(info.ClientSettingsType) : null;
        var control = (UserControl)services.GetRequiredService(info.SettingsType);
        if (control is ControlWithSettingsBase settingsControl)
        {
            settingsControl.SetSettings(model);
        }

        control.DataContext = model;
        return control;
    }

    public ComponentBase? CreateComponent(ComponentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var info = registry.Find(settings.Id);
        if (info == null)
        {
            logger.LogWarning("未注册组件：{ComponentId}", settings.Id);
            return null;
        }

        var clientSettings = info.ClientSettingsType != null ? settings.GetSettingsOrCreate(info.ClientSettingsType) : null;
        var component = (ComponentBase)services.GetRequiredService(info.ComponentType);
        component.SetSettings(clientSettings);
        // Only commit default settings after the component has initialized successfully.
        settings.Settings = clientSettings;
        return component;
    }
}