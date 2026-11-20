using System.Text.Json;
using FluentIcons.Common;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Models.Ruleset;

public enum RulesetLogicalMode { Or, And }

public interface IRuleState
{
    int State { get; }
}

public sealed class RuleRegistryInfo(string id, string name, string glyph, Type? settingsType, Type? controlType)
{
    public string Id { get; } = id;
    public string Name { get; } = string.IsNullOrEmpty(name) ? id : name;
    public string Glyph { get; } = glyph;
    public Icon? FluentIcon { get; init; }
    public Icon IconName => FluentIcon.GetValueOrDefault();
    public Visibility FluentVisibility => FluentIcon.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlyphVisibility => FluentIcon.HasValue ? Visibility.Collapsed : Visibility.Visible;
    public Type? SettingsType { get; } = settingsType;
    public Type? SettingsControlType { get; } = controlType;
    public delegate bool HandleDelegate(object? settings);
    internal HandleDelegate? Handle { get; set; }

    internal object? ResolveSettings(object? settings) => SettingsType switch
    {
        null => null,
        _ when settings is JsonElement json => json.Deserialize(SettingsType),
        _ when SettingsType.IsInstanceOfType(settings) => settings,
        _ => Activator.CreateInstance(SettingsType)
    };
}
