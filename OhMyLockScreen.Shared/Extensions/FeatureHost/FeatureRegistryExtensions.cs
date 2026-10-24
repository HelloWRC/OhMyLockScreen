using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.FeatureHost;

namespace OhMyLockScreen.Shared.Extensions.FeatureHost;

/// <summary>特性注册扩展。</summary>
public static class FeatureRegistryExtensions
{
    /// <summary>注册派生自 FeatureBase 的特性，目标类型和设置模型由基类推断。</summary>
    public static IServiceCollection AddFeature<TFeature>(this IServiceCollection services)
        where TFeature : class
    {
        return Register(services, typeof(TFeature));
    }

    /// <summary>注册特性及其设置控件，设置数据模型仍由特性基类推断。</summary>
    public static IServiceCollection AddFeature<TFeature, TSettings>(this IServiceCollection services)
        where TFeature : class
        where TSettings : ControlWithSettingsBase
    {
        return Register(services, typeof(TFeature), typeof(TSettings));
    }

    private static IServiceCollection Register(IServiceCollection services, Type featureType, Type? settingsType = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (featureType.IsAbstract || featureType.ContainsGenericParameters)
        {
            throw new ArgumentException($"特性 {featureType.FullName} 必须是可实例化的具体类型。");
        }

        if (settingsType != null && (settingsType.IsAbstract || settingsType.ContainsGenericParameters))
        {
            throw new ArgumentException($"设置控件 {settingsType.FullName} 必须是可实例化的具体类型。");
        }

        Type? targetType = null;
        Type? clientSettingsType = null;
        for (var baseType = featureType.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (!baseType.IsGenericType)
            {
                continue;
            }

            var definition = baseType.GetGenericTypeDefinition();
            if (definition == typeof(FeatureBase<,>))
            {
                clientSettingsType = baseType.GenericTypeArguments[1];
            }
            else if (definition == typeof(FeatureBase<>))
            {
                targetType = baseType.GenericTypeArguments[0];
                break;
            }
        }

        if (targetType == null)
        {
            throw new ArgumentException($"特性 {featureType.FullName} 必须派生自 FeatureBase。");
        }

        for (var baseType = settingsType?.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(ControlWithSettingsBase<>))
            {
                if (clientSettingsType == null || !baseType.GenericTypeArguments[0].IsAssignableFrom(clientSettingsType))
                {
                    throw new ArgumentException($"设置控件 {settingsType!.FullName} 的设置类型与特性 {featureType.FullName} 不一致。");
                }

                break;
            }
        }

        var info = featureType.GetCustomAttribute<FeatureInfoAttribute>()
            ?? throw new ArgumentException($"特性 {featureType.FullName} 缺少注册信息。");
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Name);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FeatureRegisterInfo)
            && descriptor.ImplementationInstance is FeatureRegisterInfo registered
            && registered.Id == info.Id))
        {
            throw new ArgumentException($"特性 Id {info.Id} 已被占用。");
        }

        services.TryAddSingleton<FeatureRegistryService>();
        services.AddSingleton(new FeatureRegisterInfo(
            info.Id, info.Name, info.Description, featureType, targetType, clientSettingsType, settingsType,
            info.IconGlyph));
        services.AddTransient(featureType);
        if (settingsType != null)
        {
            services.AddTransient(settingsType);
        }

        return services;
    }
}
