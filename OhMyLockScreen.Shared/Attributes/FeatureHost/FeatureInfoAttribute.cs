using Windows.UI.Xaml.Controls;
using OhMyLockScreen.Shared.Helpers.Scene;

namespace OhMyLockScreen.Shared.Attributes.FeatureHost;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class FeatureInfoAttribute(string id, string name, string description = "") : Attribute
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Description { get; } = description;

    /// <summary>图标的原始字元，不解析图标表达式。</summary>
    public string? IconGlyph { get; }

    /// <summary>每次读取创建独立图标源，默认使用 FluentIcons.Uwp 字体。</summary>
    public FontIconSource? IconSource => ComponentIconHelper.CreateIconSource(IconGlyph);

    public FeatureInfoAttribute(string id, string name, string iconSource, string description = "")
        : this(id, name, description)
    {
        IconGlyph = iconSource;
    }
}
