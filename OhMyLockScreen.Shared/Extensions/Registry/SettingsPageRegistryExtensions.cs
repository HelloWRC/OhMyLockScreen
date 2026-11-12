using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Models.SettingsPages;
using OhMyLockScreen.Shared.Services.Registry;

namespace OhMyLockScreen.Shared.Extensions.Registry;

public static class SettingsPageRegistryExtensions
{
    /// <summary>向当前应用主机注册可由 UWP Frame 创建的设置页。</summary>
    public static IServiceCollection AddSettingsPage<T>(this IServiceCollection services) where T : SettingsPageBase
    {
        ArgumentNullException.ThrowIfNull(services);

        var type = typeof(T);
        if (!type.IsVisible || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new ArgumentException($"设置页 {type.FullName} 必须是具有公开无参构造函数的公开具体类型。");
        }

        var info = type.GetCustomAttribute<SettingsPageInfoAttribute>()
            ?? throw new ArgumentException($"设置页 {type.FullName} 缺少注册信息。");
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Name);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SettingsPageRegistration)
            && descriptor.ImplementationInstance is SettingsPageRegistration page && page.Info.Id == info.Id))
        {
            throw new ArgumentException($"设置页 ID {info.Id} 已被占用。");
        }

        info.UseFullWidth = type.IsDefined(typeof(FullWidthPageAttribute), inherit: false);
        info.HidePageTitle = type.IsDefined(typeof(HidePageTitleAttribute), inherit: false);

        services.TryAddSingleton<SettingsPageRegistry>();
        services.AddSingleton(new SettingsPageRegistration(type, info));
        return services;
    }
}
