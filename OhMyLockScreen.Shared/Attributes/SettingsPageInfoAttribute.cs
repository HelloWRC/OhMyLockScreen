using FluentIcons.Common;
using OhMyLockScreen.Shared.Enums.SettingsPages;

namespace OhMyLockScreen.Shared.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SettingsPageInfoAttribute(string id, string name, Icon icon,
    SettingsPageCategory category = SettingsPageCategory.External) : Attribute
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public Icon Icon { get; } = icon;
    public SettingsPageCategory Category { get; } = category;
    public bool UseFullWidth { get; internal set; }
    public bool HidePageTitle { get; internal set; }
}
