using OhMyLockScreen.Shared.Helpers.Scene;
using Windows.UI.Xaml.Controls;
using FluentIcons.Common;

namespace OhMyLockScreen.Shared.Attributes.Scene;
/// <summary>组件的稳定标识和展示信息。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentInfoAttribute(string id, string name, string description = "") : Attribute
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    /// <summary>图标的原始字元，不解析图标表达式。</summary>
    public string? IconGlyph
    {
        get;
    }
    public Icon? Icon
    {
        get;
    }
    /// <summary>每次读取创建独立图标源，默认使用 FluentIcons.Uwp 字体。</summary>
    public FontIconSource? IconSource => Icon is { } icon ? ComponentIconHelper.CreateIconSource(icon) : ComponentIconHelper.CreateIconSource(IconGlyph);

    public ComponentInfoAttribute(string id, string name, Icon icon, string description = "") : this(id, name, description)
    {
        Icon = icon;
    }

    public ComponentInfoAttribute(string id, string name, string iconSource, string description = "") : this(id, name, description)
    {
        IconGlyph = iconSource;
    }
}