using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Models.Ruleset;

namespace OhMyLockScreen.Shared.Extensions.Registry;

public static class RulesetRegistryExtensions
{
    public static IServiceCollection AddRule(this IServiceCollection services, string id, string name = "",
        string glyph = "\uE9D5", RuleRegistryInfo.HandleDelegate? onHandle = null) =>
        Register(services, new(id, name, glyph, null, null), onHandle);

    public static IServiceCollection AddRule<TSettings>(this IServiceCollection services, string id, string name = "",
        string glyph = "\uE9D5", RuleRegistryInfo.HandleDelegate? onHandle = null) where TSettings : class, new() =>
        Register(services, new(id, name, glyph, typeof(TSettings), null), onHandle);

    public static IServiceCollection AddRule<TSettings, TControl>(this IServiceCollection services, string id,
        string name = "", string glyph = "\uE9D5", RuleRegistryInfo.HandleDelegate? onHandle = null)
        where TSettings : class, new() where TControl : RuleSettingsControlBase<TSettings>
    {
        Register(services, new(id, name, glyph, typeof(TSettings), typeof(TControl)), onHandle);
        return services.AddTransient<TControl>();
    }

    public static IServiceCollection AddRule<TSettings, TControl>(this IServiceCollection services, string id,
        string name, Icon icon, RuleRegistryInfo.HandleDelegate? onHandle = null)
        where TSettings : class, new() where TControl : RuleSettingsControlBase<TSettings>
    {
        Register(services, new(id, name, string.Empty, typeof(TSettings), typeof(TControl)) { FluentIcon = icon }, onHandle);
        return services.AddTransient<TControl>();
    }
    private static IServiceCollection Register(IServiceCollection services, RuleRegistryInfo info, RuleRegistryInfo.HandleDelegate? handler)
    {
        if (services.Any(x => !x.IsKeyedService && x.ImplementationInstance is RuleRegistryInfo other && other.Id == info.Id))
            throw new InvalidOperationException($"已注册 ID 为 {info.Id} 的规则。");
        info.Handle = handler;
        return services.AddSingleton(info);
    }
}
