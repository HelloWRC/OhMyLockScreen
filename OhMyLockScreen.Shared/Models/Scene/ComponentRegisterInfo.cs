using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Helpers.Scene;
using Windows.UI.Xaml.Controls;
using FluentIcons.Common;

namespace OhMyLockScreen.Shared.Models.Scene;
/// <summary>组件注册信息，只保存类型和元数据，不保存控件实例。</summary>
public sealed class ComponentRegisterInfo(
    string id,
    string name,
    string description,
    Type componentType,
    Type? clientSettingsType,
    Type? settingsType,
    bool isComponentContainer,
    string? iconGlyph = null,
    Icon? icon = null,
    Type? defaultFeaturesProviderType = null)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    /// <summary>图标的原始字元。</summary>
    public string? IconGlyph { get; } = iconGlyph;
    public Icon? Icon { get; } = icon;
    /// <summary>每次读取返回独立的图标源，不在注册表内缓存 UI 对象。</summary>
    public FontIconSource? IconSource => Icon is { } registeredIcon ? ComponentIconHelper.CreateIconSource(registeredIcon) : ComponentIconHelper.CreateIconSource(IconGlyph);
    public Type ComponentType { get; } = componentType;
    /// <summary>组件自定义设置的数据模型类型，由 ComponentBase 的泛型参数推断。</summary>
    public Type? ClientSettingsType { get; } = clientSettingsType;
    /// <summary>组件设置控件的类型；未注册设置控件时为空。</summary>
    public Type? SettingsType { get; } = settingsType;
    public bool IsComponentContainer { get; } = isComponentContainer;

    /// <summary>仅在新建组件配置时使用的默认特性提供器类型。</summary>
    public Type? DefaultFeaturesProviderType { get; } = defaultFeaturesProviderType;

    /// <summary>创建独立的组件配置及默认特性；加载已有配置时不应调用此方法。</summary>
    public ComponentSettings CreateSettings()
    {
        var settings = new ComponentSettings
        {
            Id = Id
        };
        if (ClientSettingsType != null)
        {
            settings.GetSettingsOrCreate(ClientSettingsType);
        }

        if (DefaultFeaturesProviderType != null)
        {
            var provider = (IComponentDefaultFeaturesProvider)Activator.CreateInstance(DefaultFeaturesProviderType)!;
            foreach (var feature in provider.CreateDefaultFeatures())
            {
                settings.Features.Add(feature);
            }
        }

        return settings;
    }
}
