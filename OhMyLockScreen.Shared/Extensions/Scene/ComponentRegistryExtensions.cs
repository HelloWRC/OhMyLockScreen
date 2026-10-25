using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Extensions.Scene;
/// <summary>组件注册扩展。</summary>
public static class ComponentRegistryExtensions
{
    public static IServiceCollection AddComponent<TComponent>(this IServiceCollection services)
        where TComponent : ComponentBase
    {
        return Register(services, typeof(TComponent));
    }

    /// <summary>注册组件及其设置控件，设置数据模型仍由组件基类推断。</summary>
    public static IServiceCollection AddComponent<TComponent, TSettings>(this IServiceCollection services)
        where TComponent : ComponentBase where TSettings : UserControl
    {
        return Register(services, typeof(TComponent), typeof(TSettings));
    }

    private static IServiceCollection Register(IServiceCollection services, Type componentType, Type? settingsType = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (componentType.IsAbstract || componentType.ContainsGenericParameters)
        {
            throw new ArgumentException($"组件 {componentType.FullName} 必须是可实例化的具体类型。");
        }

        if (settingsType != null && (settingsType.IsAbstract || settingsType.ContainsGenericParameters))
        {
            throw new ArgumentException($"设置控件 {settingsType.FullName} 必须是可实例化的具体类型。");
        }

        var info = componentType.GetCustomAttribute<ComponentInfoAttribute>() ?? throw new ArgumentException($"组件 {componentType.FullName} 缺少注册信息。");
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Name);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ComponentRegisterInfo) && descriptor.ImplementationInstance is ComponentRegisterInfo registered && registered.Id == info.Id))
        {
            throw new ArgumentException($"组件 Id {info.Id} 已被占用。");
        }

        Type? clientSettingsType = null;
        for (var baseType = componentType.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(ComponentBase<>))
            {
                clientSettingsType = baseType.GenericTypeArguments[0];
                break;
            }
        }

        var isContainer = componentType.IsDefined(typeof(ContainerComponentAttribute), false);
        var hasChildren = clientSettingsType != null && typeof(IComponentContainerSettings).IsAssignableFrom(clientSettingsType);
        if (isContainer != hasChildren)
        {
            throw new ArgumentException($"组件 {componentType.FullName} 的容器标记与容器设置接口不一致。");
        }

        services.TryAddSingleton<ComponentRegistryService>();
        services.AddSingleton(new ComponentRegisterInfo(info.Id, info.Name, info.Description, componentType, clientSettingsType, settingsType, isContainer, info.IconGlyph, info.Icon));
        services.AddTransient(componentType);
        if (settingsType != null)
        {
            services.AddTransient(settingsType);
        }

        return services;
    }
}