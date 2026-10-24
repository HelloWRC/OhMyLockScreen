using OhMyLockScreen.Shared.Helpers.Scene;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Models.FeatureHost;

/// <summary>特性注册信息，只保存类型和元数据，不保存特性或控件实例。</summary>
public sealed class FeatureRegisterInfo(
    string id,
    string name,
    string description,
    Type featureType,
    Type targetType,
    Type? clientSettingsType,
    Type? settingsType,
    string? iconGlyph = null)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Description { get; } = description;

    /// <summary>图标的原始字元。</summary>
    public string? IconGlyph { get; } = iconGlyph;

    /// <summary>每次读取返回独立的图标源，不在注册表内缓存 UI 对象。</summary>
    public FontIconSource? IconSource => ComponentIconHelper.CreateIconSource(IconGlyph);

    public Type FeatureType { get; } = featureType;

    /// <summary>特性可应用的目标类型，由 FeatureBase 的泛型参数推断。</summary>
    public Type TargetType { get; } = targetType;

    /// <summary>特性自定义设置的数据模型类型，由 FeatureBase 的泛型参数推断。</summary>
    public Type? ClientSettingsType { get; } = clientSettingsType;

    /// <summary>特性设置控件的类型；未注册设置控件时为空。</summary>
    public Type? SettingsType { get; } = settingsType;
}
